using System.Net;
using MergeDuo.Profile.Domain.Abstractions;
using MergeDuo.Profile.Domain.Documents;
using MergeDuo.Profile.Domain.Exceptions;
using MergeDuo.Profile.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Profile.Infra.Cosmos;

public sealed class UsersRepository : IUsersRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;

    public UsersRepository(CosmosClient client, CosmosOptions options, ICosmosDiagnosticsRecorder diagnostics)
    {
        _container = client.GetContainer(options.Database, options.UsersContainer);
        _diagnostics = diagnostics;
    }

    public async Task<UserDocument?> GetByIdAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<UserDocument>(
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
            RecordFailure("users", "read_item", ex);
            throw new ProfileDependencyException("Failed to read user profile.", ex);
        }
    }

    public async Task<IReadOnlyList<UserDocument>> FindByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.docType = 'user' AND c.handle = @handle AND IS_NULL(c.deletedAt)")
            .WithParameter("@handle", handle);

        try
        {
            using var iterator = _container.GetItemQueryIterator<UserDocument>(
                query,
                requestOptions: new QueryRequestOptions { MaxItemCount = 2 });

            var results = new List<UserDocument>(capacity: 2);
            while (iterator.HasMoreResults && results.Count < 2)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("users", "query_by_handle", page.RequestCharge, throttled: false);
                results.AddRange(page.Resource);
            }

            return results;
        }
        catch (CosmosException ex)
        {
            RecordFailure("users", "query_by_handle", ex);
            throw new ProfileDependencyException("Failed to query profile by handle.", ex);
        }
    }

    public async Task PatchStatsAsync(
        string userId,
        UserStats stats,
        DateTimeOffset updatedAt,
        string ifMatchEtag,
        CancellationToken cancellationToken)
    {
        var operations = new[]
        {
            PatchOperation.Set("/stats", stats),
            PatchOperation.Set("/updatedAt", updatedAt)
        };

        var options = new PatchItemRequestOptions
        {
            IfMatchEtag = ifMatchEtag,
            EnableContentResponseOnWrite = false
        };

        try
        {
            var response = await _container.PatchItemAsync<UserDocument>(
                userId,
                new PartitionKey(userId),
                operations,
                options,
                cancellationToken);
            _diagnostics.RecordCosmosOperation("users", "patch_stats", response.RequestCharge, throttled: false);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new ProfileConflictException("stats_conflict", "Stats update conflicted.");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ProfileNotFoundException("profile_not_found", "Profile not found.");
        }
        catch (CosmosException ex)
        {
            RecordFailure("users", "patch_stats", ex);
            throw new ProfileDependencyException("Failed to patch user stats.", ex);
        }
    }

    private void RecordFailure(string container, string operation, CosmosException ex) =>
        _diagnostics.RecordCosmosOperation(container, operation, ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);
}

public sealed class PartnershipsRepository : IPartnershipsRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;

    public PartnershipsRepository(CosmosClient client, CosmosOptions options, ICosmosDiagnosticsRecorder diagnostics)
    {
        _container = client.GetContainer(options.Database, options.PartnershipsContainer);
        _diagnostics = diagnostics;
    }

    public async Task<PartnershipDocument?> GetRelationshipAsync(
        string currentUserId,
        string targetUserId,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT * FROM c
                WHERE c.docType = 'partnership'
                  AND c.userId = @userId
                  AND c.partnerUserId = @partnerUserId
                  AND c.status = 'active'
                """)
            .WithParameter("@userId", currentUserId)
            .WithParameter("@partnerUserId", targetUserId);

        try
        {
            using var iterator = _container.GetItemQueryIterator<PartnershipDocument>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(currentUserId),
                    MaxItemCount = 1
                });

            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("partnerships", "query_relationship", page.RequestCharge, throttled: false);
                var relationship = page.Resource.FirstOrDefault();
                if (relationship is not null)
                {
                    return relationship;
                }
            }

            return null;
        }
        catch (CosmosException ex)
        {
            _diagnostics.RecordCosmosOperation("partnerships", "query_relationship", ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);
            throw new ProfileDependencyException("Failed to query relationship.", ex);
        }
    }
}

public sealed class TransactionsStatsRepository : ITransactionsStatsRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;

    public TransactionsStatsRepository(CosmosClient client, CosmosOptions options, ICosmosDiagnosticsRecorder diagnostics)
    {
        _container = client.GetContainer(options.Database, options.TransactionsContainer);
        _diagnostics = diagnostics;
    }

    public Task<int> CountTrackedAsync(string userId, CancellationToken cancellationToken) =>
        ReadSingleIntAsync(
            "count_tracked",
            new QueryDefinition(
                    """
                    SELECT VALUE COUNT(1)
                    FROM c
                    WHERE c.userId = @userId
                      AND IS_NULL(c.deletedAt)
                    """)
                .WithParameter("@userId", userId),
            userId,
            cancellationToken);

    public async Task<IReadOnlyList<string>> ListActiveMonthsAsync(string userId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT DISTINCT VALUE c.yearMonth
                FROM c
                WHERE c.userId = @userId
                  AND IS_NULL(c.deletedAt)
                """)
            .WithParameter("@userId", userId);

        try
        {
            using var iterator = _container.GetItemQueryIterator<string>(
                query,
                requestOptions: QueryByUserOptions(userId));

            var results = new List<string>();
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("transactions", "list_active_months", page.RequestCharge, throttled: false);
                results.AddRange(page.Resource.Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            return results;
        }
        catch (CosmosException ex)
        {
            _diagnostics.RecordCosmosOperation("transactions", "list_active_months", ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);
            throw new ProfileDependencyException("Failed to list active transaction months.", ex);
        }
    }

    private async Task<int> ReadSingleIntAsync(
        string operation,
        QueryDefinition query,
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var iterator = _container.GetItemQueryIterator<int>(
                query,
                requestOptions: QueryByUserOptions(userId));

            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("transactions", operation, page.RequestCharge, throttled: false);
                var value = page.Resource.FirstOrDefault();
                return value;
            }

            return 0;
        }
        catch (CosmosException ex)
        {
            _diagnostics.RecordCosmosOperation("transactions", operation, ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);
            throw new ProfileDependencyException("Failed to query transaction stats.", ex);
        }
    }

    private static QueryRequestOptions QueryByUserOptions(string userId) =>
        new()
        {
            PartitionKey = new PartitionKeyBuilder().Add(userId).Build(),
            MaxItemCount = 100
        };
}
