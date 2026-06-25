using System.Net;
using MergeDuo.Partnership.Domain.Abstractions;
using MergeDuo.Partnership.Domain.Documents;
using MergeDuo.Partnership.Domain.Exceptions;
using MergeDuo.Partnership.Domain.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace MergeDuo.Partnership.Infra.Cosmos;

public sealed class UsersReadRepository : IUsersReadRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;
    private readonly ILogger<UsersReadRepository> _logger;

    public UsersReadRepository(
        CosmosClient client,
        CosmosOptions options,
        ICosmosDiagnosticsRecorder diagnostics,
        ILogger<UsersReadRepository> logger)
    {
        _container = client.GetContainer(options.Database, options.UsersContainer);
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public async Task<UserSummaryDocument?> GetByIdAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<UserSummaryDocument>(
                userId,
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
            _diagnostics.RecordCosmosOperation("users", "read_item", response.RequestCharge, throttled: false);
            response.Resource.ETag = response.ETag;
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            throw CosmosFailureHandler.Classify(
                _logger,
                _diagnostics,
                "users",
                "read_item",
                "Failed to read user.",
                "invalid_user_request",
                ex);
        }
    }

    public async Task<UserSummaryDocument?> GetActiveUserSummaryAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var user = await GetByIdAsync(userId, cancellationToken);
        return user?.DeletedAt is null ? user : null;
    }

    public Task<UserSummaryDocument?> GetUserForPartnershipAsync(
        string userId,
        CancellationToken cancellationToken) =>
        GetActiveUserSummaryAsync(userId, cancellationToken);
}

public sealed class InvitesRepository : IInvitesRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;
    private readonly ILogger<InvitesRepository> _logger;

    public InvitesRepository(
        CosmosClient client,
        CosmosOptions options,
        ICosmosDiagnosticsRecorder diagnostics,
        ILogger<InvitesRepository> logger)
    {
        _container = client.GetContainer(options.Database, options.InvitesContainer);
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public async Task<MergeInviteDocument?> GetPendingForInviterAsync(
        string inviterUserId,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT * FROM c
                WHERE c.docType = 'mergeInvite'
                  AND c.inviterUserId = @inviterUserId
                  AND c.status = 'pending'
                ORDER BY c.createdAt DESC
                """)
            .WithParameter("@inviterUserId", inviterUserId);

        try
        {
            using var iterator = _container.GetItemQueryIterator<MergeInviteDocument>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(inviterUserId),
                    MaxItemCount = 1
                });

            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("mergeInvites", "query_pending_inviter", page.RequestCharge, throttled: false);
                var invite = page.Resource.FirstOrDefault();
                if (invite is not null)
                {
                    invite.ETag = invite.ETag;
                    return invite;
                }
            }

            return null;
        }
        catch (CosmosException ex)
        {
            throw CosmosFailureHandler.Classify(
                _logger,
                _diagnostics,
                "mergeInvites",
                "query_pending_inviter",
                "Failed to query invites.",
                "invite_query_invalid",
                ex);
        }
    }

    public async Task<IReadOnlyList<MergeInviteDocument>> FindByTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT * FROM c
                WHERE c.docType = 'mergeInvite'
                  AND c.token = @token
                """)
            .WithParameter("@token", token);

        try
        {
            using var iterator = _container.GetItemQueryIterator<MergeInviteDocument>(
                query,
                requestOptions: new QueryRequestOptions { MaxItemCount = 2 });

            var results = new List<MergeInviteDocument>(capacity: 2);
            while (iterator.HasMoreResults && results.Count < 2)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("mergeInvites", "query_token", page.RequestCharge, throttled: false);
                results.AddRange(page.Resource.Take(2 - results.Count));
            }

            return results;
        }
        catch (CosmosException ex)
        {
            throw CosmosFailureHandler.Classify(
                _logger,
                _diagnostics,
                "mergeInvites",
                "query_token",
                "Failed to query invite.",
                "invite_token_invalid",
                ex);
        }
    }

    public async Task CreateAsync(MergeInviteDocument invite, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.CreateItemAsync(
                invite,
                new PartitionKey(invite.InviterUserId),
                cancellationToken: cancellationToken);
            _diagnostics.RecordCosmosOperation("mergeInvites", "create", response.RequestCharge, throttled: false);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            throw new PartnershipConflictException("duplicate_invite_token_detected", "Duplicate invite detected.");
        }
        catch (CosmosException ex)
        {
            throw CosmosFailureHandler.Classify(
                _logger,
                _diagnostics,
                "mergeInvites",
                "create",
                "Failed to create invite.",
                "invite_payload_invalid",
                ex);
        }
    }

    public async Task MarkAcceptedAsync(
        MergeInviteDocument invite,
        AcceptedBySnapshot acceptedBy,
        string partnershipId,
        DateTimeOffset acceptedAt,
        string ifMatchEtag,
        CancellationToken cancellationToken)
    {
        var operations = new[]
        {
            PatchOperation.Set("/status", InviteStatuses.Accepted),
            PatchOperation.Set("/acceptedBy", acceptedBy),
            PatchOperation.Set("/partnershipId", partnershipId),
            PatchOperation.Set("/acceptedAt", acceptedAt),
            PatchOperation.Set("/updatedAt", acceptedAt),
            PatchOperation.Set("/ttl", -1)
        };

        await PatchInviteAsync(invite, operations, ifMatchEtag, "mark_accepted", cancellationToken);
    }

    public async Task MarkRevokedAsync(
        MergeInviteDocument invite,
        DateTimeOffset revokedAt,
        string ifMatchEtag,
        CancellationToken cancellationToken)
    {
        var operations = new[]
        {
            PatchOperation.Set("/status", InviteStatuses.Revoked),
            PatchOperation.Set("/revokedAt", revokedAt),
            PatchOperation.Set("/updatedAt", revokedAt)
        };

        await PatchInviteAsync(invite, operations, ifMatchEtag, "mark_revoked", cancellationToken);
    }

    public async Task MarkExpiredAsync(
        MergeInviteDocument invite,
        DateTimeOffset expiredAt,
        string ifMatchEtag,
        CancellationToken cancellationToken)
    {
        var operations = new[]
        {
            PatchOperation.Set("/status", InviteStatuses.Expired),
            PatchOperation.Set("/updatedAt", expiredAt)
        };

        await PatchInviteAsync(invite, operations, ifMatchEtag, "mark_expired", cancellationToken);
    }

    private async Task PatchInviteAsync(
        MergeInviteDocument invite,
        IReadOnlyList<PatchOperation> operations,
        string ifMatchEtag,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.PatchItemAsync<MergeInviteDocument>(
                invite.Id,
                new PartitionKey(invite.InviterUserId),
                operations,
                new PatchItemRequestOptions
                {
                    IfMatchEtag = ifMatchEtag,
                    EnableContentResponseOnWrite = false
                },
                cancellationToken);
            _diagnostics.RecordCosmosOperation("mergeInvites", operation, response.RequestCharge, throttled: false);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new PartnershipConflictException("invite_already_accepted", "Invite update conflicted.");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new PartnershipNotFoundException("invite_not_found", "Invite not found.");
        }
        catch (CosmosException ex)
        {
            throw CosmosFailureHandler.Classify(
                _logger,
                _diagnostics,
                "mergeInvites",
                operation,
                "Failed to update invite.",
                "invite_patch_invalid",
                ex);
        }
    }
}

public sealed class PartnershipsRepository : IPartnershipsRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;
    private readonly ILogger<PartnershipsRepository> _logger;

    public PartnershipsRepository(
        CosmosClient client,
        CosmosOptions options,
        ICosmosDiagnosticsRecorder diagnostics,
        ILogger<PartnershipsRepository> logger)
    {
        _container = client.GetContainer(options.Database, options.PartnershipsContainer);
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public async Task<PartnershipDocument?> GetCurrentAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT * FROM c
                WHERE c.docType = 'partnership'
                  AND c.userId = @userId
                  AND (c.status = 'active' OR c.status = 'paused')
                ORDER BY c.updatedAt DESC
                """)
            .WithParameter("@userId", userId);

        try
        {
            using var iterator = _container.GetItemQueryIterator<PartnershipDocument>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(userId),
                    MaxItemCount = 1
                });

            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("partnerships", "query_current", page.RequestCharge, throttled: false);
                var document = page.Resource.FirstOrDefault();
                if (document is not null)
                {
                    return document;
                }
            }

            return null;
        }
        catch (CosmosException ex)
        {
            throw CosmosFailureHandler.Classify(
                _logger,
                _diagnostics,
                "partnerships",
                "query_current",
                "Failed to query partnership.",
                "partnership_query_invalid",
                ex);
        }
    }

    public async Task<PartnershipDocument?> GetByIdAsync(
        string userId,
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<PartnershipDocument>(
                id,
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
            _diagnostics.RecordCosmosOperation("partnerships", "read_item", response.RequestCharge, throttled: false);
            response.Resource.ETag = response.ETag;
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            throw CosmosFailureHandler.Classify(
                _logger,
                _diagnostics,
                "partnerships",
                "read_item",
                "Failed to read partnership.",
                "partnership_read_invalid",
                ex);
        }
    }

    public async Task CreateIfAbsentAsync(PartnershipDocument document, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.CreateItemAsync(
                document,
                new PartitionKey(document.UserId),
                cancellationToken: cancellationToken);
            _diagnostics.RecordCosmosOperation("partnerships", "create", response.RequestCharge, throttled: false);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
        }
        catch (CosmosException ex)
        {
            throw CosmosFailureHandler.Classify(
                _logger,
                _diagnostics,
                "partnerships",
                "create",
                "Failed to create partnership.",
                "partnership_payload_invalid",
                ex);
        }
    }

    public async Task EnsurePairAsync(
        PartnershipDocument ownerDocument,
        PartnershipDocument mirrorDocument,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await CreateIfAbsentAsync(ownerDocument, cancellationToken);
                await CreateIfAbsentAsync(mirrorDocument, cancellationToken);
                return;
            }
            catch (PartnershipDependencyException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
            }
        }

        await CreateIfAbsentAsync(ownerDocument, cancellationToken);
        await CreateIfAbsentAsync(mirrorDocument, cancellationToken);
    }

    public async Task PatchStatusAsync(
        string userId,
        string id,
        string status,
        DateTimeOffset updatedAt,
        DateTimeOffset? endedAt,
        string ifMatchEtag,
        CancellationToken cancellationToken)
    {
        var operations = new List<PatchOperation>
        {
            PatchOperation.Set("/status", status),
            PatchOperation.Set("/updatedAt", updatedAt)
        };

        if (endedAt is not null)
        {
            operations.Add(PatchOperation.Set("/endedAt", endedAt));
        }

        try
        {
            var response = await _container.PatchItemAsync<PartnershipDocument>(
                id,
                new PartitionKey(userId),
                operations,
                new PatchItemRequestOptions
                {
                    IfMatchEtag = ifMatchEtag,
                    EnableContentResponseOnWrite = false
                },
                cancellationToken);
            _diagnostics.RecordCosmosOperation("partnerships", "patch_status", response.RequestCharge, throttled: false);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new PartnershipConflictException("partnership_already_exists", "Partnership update conflicted.");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new PartnershipNotFoundException("partnership_not_found", "Partnership not found.");
        }
        catch (CosmosException ex)
        {
            throw CosmosFailureHandler.Classify(
                _logger,
                _diagnostics,
                "partnerships",
                "patch_status",
                "Failed to update partnership.",
                "partnership_patch_invalid",
                ex);
        }
    }
}
