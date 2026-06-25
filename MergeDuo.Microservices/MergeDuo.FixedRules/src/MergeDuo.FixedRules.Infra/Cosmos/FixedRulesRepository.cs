using System.Net;
using MergeDuo.FixedRules.Domain.Abstractions;
using MergeDuo.FixedRules.Domain.Documents;
using MergeDuo.FixedRules.Domain.Exceptions;
using MergeDuo.FixedRules.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.FixedRules.Infra.Cosmos;

public sealed class FixedRulesRepository : IFixedRulesRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;

    public FixedRulesRepository(CosmosClient client, CosmosOptions options, ICosmosDiagnosticsRecorder diagnostics)
    {
        _container = client.GetContainer(options.Database, options.FixedRulesContainer);
        _diagnostics = diagnostics;
    }

    public async Task<IReadOnlyList<FixedRuleDocument>> ListAsync(
        string userId,
        FixedRuleListFilter filter,
        CancellationToken cancellationToken)
    {
        var conditions = new List<string>
        {
            "c.userId = @userId",
            "IS_NULL(c.deletedAt)"
        };

        if (filter.Active != FixedRuleActiveFilter.All)
        {
            conditions.Add("c.active = @active");
        }

        if (filter.Category is not null)
        {
            conditions.Add("c.category = @category");
        }

        var query = new QueryDefinition(
                $"""
                SELECT * FROM c
                WHERE {string.Join("\n  AND ", conditions)}
                """)
            .WithParameter("@userId", userId);

        if (filter.Active != FixedRuleActiveFilter.All)
        {
            query.WithParameter("@active", filter.Active == FixedRuleActiveFilter.Active);
        }

        if (filter.Category is not null)
        {
            query.WithParameter("@category", filter.Category);
        }

        try
        {
            using var iterator = _container.GetItemQueryIterator<FixedRuleDocument>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(userId),
                    MaxItemCount = 100
                });

            var results = new List<FixedRuleDocument>();
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("fixedRules", "list", page.RequestCharge, throttled: false);
                results.AddRange(page.Resource);
            }

            await EnsureEtagsAsync(results, cancellationToken);

            return results
                .OrderBy(x => x.StartsAt, StringComparer.Ordinal)
                .ThenBy(x => x.Description, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (CosmosException ex)
        {
            RecordFailure("list", ex);
            throw new FixedRulesDependencyException("fixed_rules_dependency_unavailable", "FixedRules dependency unavailable.", ex);
        }
    }

    public async Task<FixedRuleDocument?> GetByIdAsync(
        string userId,
        string fixedRuleId,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<FixedRuleDocument>(
                fixedRuleId,
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
            _diagnostics.RecordCosmosOperation("fixedRules", "read_item", response.RequestCharge, throttled: false);
            response.Resource.ETag = response.ETag;

            if (!includeDeleted && response.Resource.DeletedAt is not null)
            {
                return null;
            }

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            RecordFailure("read_item", ex);
            throw new FixedRulesDependencyException("fixed_rules_dependency_unavailable", "FixedRules dependency unavailable.", ex);
        }
    }

    public async Task CreateAsync(FixedRuleDocument rule, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.CreateItemAsync(
                rule,
                new PartitionKey(rule.UserId),
                requestOptions: new ItemRequestOptions { EnableContentResponseOnWrite = false },
                cancellationToken: cancellationToken);
            _diagnostics.RecordCosmosOperation("fixedRules", "create_item", response.RequestCharge, throttled: false);
            rule.ETag = response.ETag;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            throw new FixedRulesConflictException("fixed_rule_conflict", "Fixed rule conflict.");
        }
        catch (CosmosException ex)
        {
            RecordFailure("create_item", ex);
            throw new FixedRulesDependencyException("fixed_rules_dependency_unavailable", "FixedRules dependency unavailable.", ex);
        }
    }

    public async Task<FixedRuleDocument> PatchAsync(
        FixedRuleDocument rule,
        FixedRulePatch patch,
        string ifMatchEtag,
        bool clientProvidedEtag,
        CancellationToken cancellationToken)
    {
        var operations = BuildPatchOperations(patch);
        var options = new PatchItemRequestOptions
        {
            IfMatchEtag = ifMatchEtag,
            EnableContentResponseOnWrite = true
        };

        try
        {
            var response = await _container.PatchItemAsync<FixedRuleDocument>(
                rule.Id,
                new PartitionKey(rule.UserId),
                operations,
                options,
                cancellationToken);
            _diagnostics.RecordCosmosOperation("fixedRules", "patch_item", response.RequestCharge, throttled: false);
            response.Resource.ETag = response.ETag;
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw clientProvidedEtag
                ? new FixedRulesPreconditionFailedException("precondition_failed", "Fixed rule precondition failed.")
                : new FixedRulesConflictException("fixed_rule_conflict", "Fixed rule update conflicted.");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FixedRulesNotFoundException("fixed_rule_not_found", "Fixed rule not found.");
        }
        catch (CosmosException ex)
        {
            RecordFailure("patch_item", ex);
            throw new FixedRulesDependencyException("fixed_rules_dependency_unavailable", "FixedRules dependency unavailable.", ex);
        }
    }

    public async Task SoftDeleteAsync(
        FixedRuleDocument rule,
        DateTimeOffset deletedAt,
        string ifMatchEtag,
        bool clientProvidedEtag,
        CancellationToken cancellationToken)
    {
        var operations = new[]
        {
            PatchOperation.Set("/active", false),
            PatchOperation.Set("/deletedAt", deletedAt),
            PatchOperation.Set("/updatedAt", deletedAt)
        };

        var options = new PatchItemRequestOptions
        {
            IfMatchEtag = ifMatchEtag,
            EnableContentResponseOnWrite = false
        };

        try
        {
            var response = await _container.PatchItemAsync<FixedRuleDocument>(
                rule.Id,
                new PartitionKey(rule.UserId),
                operations,
                options,
                cancellationToken);
            _diagnostics.RecordCosmosOperation("fixedRules", "soft_delete", response.RequestCharge, throttled: false);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw clientProvidedEtag
                ? new FixedRulesPreconditionFailedException("precondition_failed", "Fixed rule precondition failed.")
                : new FixedRulesConflictException("fixed_rule_conflict", "Fixed rule delete conflicted.");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FixedRulesNotFoundException("fixed_rule_not_found", "Fixed rule not found.");
        }
        catch (CosmosException ex)
        {
            RecordFailure("soft_delete", ex);
            throw new FixedRulesDependencyException("fixed_rules_dependency_unavailable", "FixedRules dependency unavailable.", ex);
        }
    }

    private static List<PatchOperation> BuildPatchOperations(FixedRulePatch patch)
    {
        var operations = new List<PatchOperation>();
        if (patch.HasCategory)
        {
            operations.Add(PatchOperation.Set("/category", patch.Category));
        }

        if (patch.HasDescription)
        {
            operations.Add(PatchOperation.Set("/description", patch.Description));
        }

        if (patch.HasAmount)
        {
            operations.Add(PatchOperation.Set("/amount", patch.Amount!.Value));
        }

        if (patch.HasCardId)
        {
            operations.Add(PatchOperation.Set("/cardId", patch.CardId));
        }

        if (patch.HasTags)
        {
            operations.Add(PatchOperation.Set("/tags", patch.Tags ?? []));
        }

        if (patch.HasSchedule)
        {
            operations.Add(PatchOperation.Set("/schedule", patch.Schedule));
        }

        if (patch.HasStartsAt)
        {
            operations.Add(PatchOperation.Set("/startsAt", patch.StartsAt));
        }

        if (patch.HasEndsAt)
        {
            operations.Add(PatchOperation.Set("/endsAt", patch.EndsAt));
        }

        if (patch.HasActive)
        {
            operations.Add(PatchOperation.Set("/active", patch.Active!.Value));
        }

        operations.Add(PatchOperation.Set("/updatedAt", patch.UpdatedAt));
        return operations;
    }

    private void RecordFailure(string operation, CosmosException ex) =>
        _diagnostics.RecordCosmosOperation("fixedRules", operation, ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);

    private async Task EnsureEtagsAsync(IEnumerable<FixedRuleDocument> rules, CancellationToken cancellationToken)
    {
        foreach (var rule in rules)
        {
            if (!string.IsNullOrWhiteSpace(rule.ETag))
            {
                continue;
            }

            try
            {
                var response = await _container.ReadItemAsync<FixedRuleDocument>(
                    rule.Id,
                    new PartitionKey(rule.UserId),
                    cancellationToken: cancellationToken);
                _diagnostics.RecordCosmosOperation("fixedRules", "list_read_etag", response.RequestCharge, throttled: false);
                rule.ETag = response.ETag;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // The item changed between query and ETag hydration. Keep the list response best-effort.
            }
        }
    }
}
