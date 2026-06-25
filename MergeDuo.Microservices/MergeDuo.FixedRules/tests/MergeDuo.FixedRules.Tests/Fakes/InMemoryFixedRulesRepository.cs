using MergeDuo.FixedRules.Domain.Abstractions;
using MergeDuo.FixedRules.Domain.Documents;
using MergeDuo.FixedRules.Domain.Exceptions;
using MergeDuo.FixedRules.Domain.Rules;

namespace MergeDuo.FixedRules.Tests.Fakes;

public sealed class InMemoryFixedRulesRepository : IFixedRulesRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<(string UserId, string Id), FixedRuleDocument> _rules = [];

    public void Seed(FixedRuleDocument rule)
    {
        lock (_gate)
        {
            rule.ETag ??= NewEtag();
            _rules[(rule.UserId, rule.Id)] = Clone(rule);
        }
    }

    public FixedRuleDocument? Stored(string userId, string fixedRuleId)
    {
        lock (_gate)
        {
            return _rules.TryGetValue((userId, fixedRuleId), out var rule) ? Clone(rule) : null;
        }
    }

    public Task<IReadOnlyList<FixedRuleDocument>> ListAsync(
        string userId,
        FixedRuleListFilter filter,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var query = _rules.Values.Where(x => x.UserId == userId && x.DeletedAt is null);
            query = filter.Active switch
            {
                FixedRuleActiveFilter.Active => query.Where(x => x.Active),
                FixedRuleActiveFilter.Inactive => query.Where(x => !x.Active),
                _ => query
            };

            if (filter.Category is not null)
            {
                query = query.Where(x => x.Category == filter.Category);
            }

            return Task.FromResult<IReadOnlyList<FixedRuleDocument>>(query.Select(Clone).ToArray());
        }
    }

    public Task<FixedRuleDocument?> GetByIdAsync(
        string userId,
        string fixedRuleId,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_rules.TryGetValue((userId, fixedRuleId), out var rule))
            {
                return Task.FromResult<FixedRuleDocument?>(null);
            }

            if (!includeDeleted && rule.DeletedAt is not null)
            {
                return Task.FromResult<FixedRuleDocument?>(null);
            }

            return Task.FromResult<FixedRuleDocument?>(Clone(rule));
        }
    }

    public Task CreateAsync(FixedRuleDocument rule, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var key = (rule.UserId, rule.Id);
            if (_rules.ContainsKey(key))
            {
                throw new FixedRulesConflictException("fixed_rule_conflict", "Fixed rule conflict.");
            }

            rule.ETag = NewEtag();
            _rules[key] = Clone(rule);
            return Task.CompletedTask;
        }
    }

    public Task<FixedRuleDocument> PatchAsync(
        FixedRuleDocument rule,
        FixedRulePatch patch,
        string ifMatchEtag,
        bool clientProvidedEtag,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_rules.TryGetValue((rule.UserId, rule.Id), out var stored) || stored.DeletedAt is not null)
            {
                throw new FixedRulesNotFoundException("fixed_rule_not_found", "Fixed rule not found.");
            }

            EnsureEtag(stored, ifMatchEtag, clientProvidedEtag);

            if (patch.HasCategory) stored.Category = patch.Category!;
            if (patch.HasDescription) stored.Description = patch.Description!;
            if (patch.HasAmount) stored.Amount = patch.Amount!.Value;
            if (patch.HasCardId) stored.CardId = patch.CardId;
            if (patch.HasTags) stored.Tags = patch.Tags ?? [];
            if (patch.HasSchedule) stored.Schedule = ScheduleRules.Clone(patch.Schedule!);
            if (patch.HasStartsAt) stored.StartsAt = patch.StartsAt!;
            if (patch.HasEndsAt) stored.EndsAt = patch.EndsAt;
            if (patch.HasActive) stored.Active = patch.Active!.Value;
            stored.UpdatedAt = patch.UpdatedAt;
            stored.ETag = NewEtag();
            return Task.FromResult(Clone(stored));
        }
    }

    public Task SoftDeleteAsync(
        FixedRuleDocument rule,
        DateTimeOffset deletedAt,
        string ifMatchEtag,
        bool clientProvidedEtag,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_rules.TryGetValue((rule.UserId, rule.Id), out var stored) || stored.DeletedAt is not null)
            {
                throw new FixedRulesNotFoundException("fixed_rule_not_found", "Fixed rule not found.");
            }

            EnsureEtag(stored, ifMatchEtag, clientProvidedEtag);

            stored.Active = false;
            stored.DeletedAt = deletedAt;
            stored.UpdatedAt = deletedAt;
            stored.ETag = NewEtag();
            return Task.CompletedTask;
        }
    }

    private static void EnsureEtag(FixedRuleDocument stored, string ifMatchEtag, bool clientProvidedEtag)
    {
        if (!string.Equals(stored.ETag, ifMatchEtag, StringComparison.Ordinal))
        {
            throw clientProvidedEtag
                ? new FixedRulesPreconditionFailedException("precondition_failed", "Fixed rule precondition failed.")
                : new FixedRulesConflictException("fixed_rule_conflict", "Fixed rule conflicted.");
        }
    }

    private static string NewEtag() => Guid.NewGuid().ToString("N");

    private static FixedRuleDocument Clone(FixedRuleDocument rule) =>
        new()
        {
            Id = rule.Id,
            DocType = rule.DocType,
            SchemaVersion = rule.SchemaVersion,
            UserId = rule.UserId,
            Category = rule.Category,
            Description = rule.Description,
            Amount = rule.Amount,
            CardId = rule.CardId,
            Tags = rule.Tags?.ToArray() ?? [],
            Schedule = ScheduleRules.Clone(rule.Schedule),
            StartsAt = rule.StartsAt,
            EndsAt = rule.EndsAt,
            Active = rule.Active,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt,
            DeletedAt = rule.DeletedAt,
            ETag = rule.ETag
        };
}
