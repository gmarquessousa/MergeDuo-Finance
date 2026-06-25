using MergeDuo.Transactions.Domain.Abstractions;
using MergeDuo.Transactions.Domain.Documents;
using MergeDuo.Transactions.Domain.Exceptions;

namespace MergeDuo.Transactions.Tests.Fakes;

public sealed class FakeReadinessProbe : ITransactionsReadinessProbe
{
    public bool Ready { get; set; } = true;
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(Ready);
}

public sealed class FakeTransactionIdGenerator(params string[] transactionIds) : ITransactionIdGenerator
{
    private readonly Queue<string> _ids = new(transactionIds.Length == 0 ? ["tx_test_01", "tx_test_02", "tx_test_03"] : transactionIds);
    private int _groupIndex = 1;

    public string NewTransactionId() => _ids.Count == 0 ? $"tx_test_{Guid.NewGuid():N}" : _ids.Dequeue();
    public string NewGroupId() => $"txg_test_{_groupIndex++:00}";

    public string FromIdempotencyKey(string prefix, string userId, string idempotencyKey, string payloadHash, int index) =>
        $"{prefix}_idem_{Math.Abs(HashCode.Combine(userId, idempotencyKey, index)):x}";
}

public sealed class InMemoryAuxRepositories : ICardsReadRepository, IFixedRulesReadRepository, IPartnershipsReadRepository
{
    private readonly Dictionary<(string UserId, string Id), CardDocument> _cards = [];
    private readonly Dictionary<(string UserId, string Id), FixedRuleDocument> _rules = [];
    private readonly Dictionary<string, PartnershipDocument> _partners = [];

    public void Seed(CardDocument card) => _cards[(card.UserId, card.Id)] = card;
    public void Seed(FixedRuleDocument rule) => _rules[(rule.UserId, rule.Id)] = rule;
    public void Seed(PartnershipDocument partnership) => _partners[partnership.UserId] = partnership;

    Task<CardDocument?> ICardsReadRepository.GetActiveAsync(string userId, string cardId, CancellationToken cancellationToken) =>
        Task.FromResult(_cards.TryGetValue((userId, cardId), out var card) && card.DeletedAt is null ? card : null);

    Task<FixedRuleDocument?> IFixedRulesReadRepository.GetActiveAsync(string userId, string fixedRuleId, CancellationToken cancellationToken) =>
        Task.FromResult(_rules.TryGetValue((userId, fixedRuleId), out var rule) && rule.Active && rule.DeletedAt is null ? rule : null);

    Task<IReadOnlyList<string>> IFixedRulesReadRepository.ListActiveTagsAsync(string userId, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> tags = _rules.Values
            .Where(x => x.UserId == userId && x.Active && x.DeletedAt is null)
            .SelectMany(x => x.Tags ?? [])
            .Select(x => (x ?? "").Trim().ToLowerInvariant())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(tags);
    }

    public Task<PartnershipDocument?> GetActivePartnerAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult(_partners.TryGetValue(userId, out var partner) && partner.Status == "active" ? partner : null);

    public Task<bool> IsActivePartnerAsync(string userId, string partnerUserId, CancellationToken cancellationToken) =>
        Task.FromResult(_partners.TryGetValue(userId, out var partner)
            && partner.Status == "active"
            && partner.PartnerUserId == partnerUserId);
}

public sealed class InMemoryTransactionsRepository : ITransactionsRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<(string UserId, string YearMonth, string Id), TransactionDocument> _items = [];

    public void Seed(TransactionDocument transaction)
    {
        lock (_gate)
        {
            transaction.ETag ??= NewEtag();
            _items[(transaction.UserId, transaction.YearMonth, transaction.Id)] = Clone(transaction);
        }
    }

    public TransactionDocument? Stored(string userId, string yearMonth, string id)
    {
        lock (_gate)
        {
            return _items.TryGetValue((userId, yearMonth, id), out var item) ? Clone(item) : null;
        }
    }

    public Task<TransactionsPage> ListMonthAsync(
        string userId,
        string yearMonth,
        TransactionListFilters filters,
        int pageSize,
        string? continuationToken,
        SortDirection sort,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IEnumerable<TransactionDocument> query = _items.Values
                .Where(x => x.UserId == userId && x.YearMonth == yearMonth && x.DeletedAt is null);

            if (filters.Category is not null)
            {
                query = query.Where(x => x.Category == filters.Category);
            }

            if (filters.CardId is not null)
            {
                query = query.Where(x => x.CardId == filters.CardId);
            }

            query = sort == SortDirection.DateAsc
                ? query.OrderBy(x => x.Date).ThenBy(x => x.CreatedAt)
                : query.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt);

            var items = query.Take(pageSize).Select(Clone).ToArray();
            return Task.FromResult(new TransactionsPage(items, null));
        }
    }

    public Task<TransactionDocument?> GetByIdAsync(string userId, string yearMonth, string transactionId, bool includeDeleted, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_items.TryGetValue((userId, yearMonth, transactionId), out var item))
            {
                return Task.FromResult<TransactionDocument?>(null);
            }

            return Task.FromResult(!includeDeleted && item.DeletedAt is not null ? null : Clone(item));
        }
    }

    public Task CreateAsync(TransactionDocument transaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var key = (transaction.UserId, transaction.YearMonth, transaction.Id);
            if (_items.ContainsKey(key))
            {
                throw new TransactionsConflictException("transaction_conflict", "Transaction conflict.");
            }

            transaction.ETag = NewEtag();
            _items[key] = Clone(transaction);
            return Task.CompletedTask;
        }
    }

    public Task<TransactionDocument> PatchAsync(TransactionDocument transaction, TransactionPatch patch, string ifMatchEtag, bool clientProvidedEtag, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var key = (transaction.UserId, transaction.YearMonth, transaction.Id);
            if (!_items.TryGetValue(key, out var stored) || stored.DeletedAt is not null)
            {
                throw new TransactionsNotFoundException("transaction_not_found", "Transaction not found.");
            }

            EnsureEtag(stored, ifMatchEtag, clientProvidedEtag);
            stored.Category = patch.Category;
            stored.Kind = patch.Kind;
            stored.Date = patch.Date;
            stored.PurchaseDate = patch.PurchaseDate;
            stored.Description = patch.Description;
            stored.Amount = patch.Amount;
            stored.Currency = patch.Currency;
            stored.OwnerLabel = patch.OwnerLabel;
            stored.CardId = patch.CardId;
            stored.FixedRuleId = patch.FixedRuleId;
            stored.Tags = patch.Tags;
            stored.Notes = patch.Notes;
            stored.Source.Channel = patch.FixedRuleId is null ? "manual" : "fixed_rule";
            stored.UpdatedAt = patch.UpdatedAt;
            stored.ETag = NewEtag();
            return Task.FromResult(Clone(stored));
        }
    }

    public Task CreateMovedCopyAsync(TransactionDocument transaction, CancellationToken cancellationToken) => CreateAsync(transaction, cancellationToken);

    public Task SoftDeleteAsync(TransactionDocument transaction, DateTimeOffset deletedAt, string ifMatchEtag, bool clientProvidedEtag, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var key = (transaction.UserId, transaction.YearMonth, transaction.Id);
            if (!_items.TryGetValue(key, out var stored) || stored.DeletedAt is not null)
            {
                throw new TransactionsNotFoundException("transaction_not_found", "Transaction not found.");
            }

            EnsureEtag(stored, ifMatchEtag, clientProvidedEtag);
            stored.DeletedAt = deletedAt;
            stored.UpdatedAt = deletedAt;
            stored.ETag = NewEtag();
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<TransactionDocument>> ListGroupAsync(string userId, string groupId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<TransactionDocument> items = _items.Values
                .Where(x => x.UserId == userId && x.Installments?.GroupId == groupId && x.DeletedAt is null)
                .OrderBy(x => x.Installments!.Index)
                .Select(Clone)
                .ToArray();
            return Task.FromResult(items);
        }
    }

    public Task<IReadOnlyList<TransactionDocument>> ListTaggedAsync(
        string userId,
        bool includeTransactionDetails,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<TransactionDocument> items = _items.Values
                .Where(x => x.UserId == userId && x.DeletedAt is null && x.Tags is { Length: > 0 })
                .Select(Clone)
                .ToArray();
            return Task.FromResult(items);
        }
    }

    public Task<CardUsageTotals> GetCardUsageAsync(string userId, string yearMonth, string cardId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var items = _items.Values
                .Where(x => x.UserId == userId && x.YearMonth == yearMonth && x.CardId == cardId && x.Category == "credit_card" && x.DeletedAt is null)
                .ToArray();
            return Task.FromResult(new CardUsageTotals(cardId, yearMonth, "BRL", items.Sum(x => x.Amount), items.Length, items.Count(x => x.Installments is not null)));
        }
    }

    private static void EnsureEtag(TransactionDocument stored, string ifMatchEtag, bool clientProvidedEtag)
    {
        if (!string.Equals(stored.ETag, ifMatchEtag, StringComparison.Ordinal))
        {
            throw clientProvidedEtag
                ? new TransactionsPreconditionFailedException("precondition_failed", "Transaction precondition failed.")
                : new TransactionsConflictException("transaction_conflict", "Transaction conflict.");
        }
    }

    private static string NewEtag() => Guid.NewGuid().ToString("N");

    private static TransactionDocument Clone(TransactionDocument transaction) =>
        new()
        {
            Id = transaction.Id,
            DocType = transaction.DocType,
            SchemaVersion = transaction.SchemaVersion,
            UserId = transaction.UserId,
            YearMonth = transaction.YearMonth,
            Date = transaction.Date,
            PurchaseDate = transaction.PurchaseDate,
            Category = transaction.Category,
            Kind = transaction.Kind,
            Description = transaction.Description,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            OwnerLabel = transaction.OwnerLabel,
            CardId = transaction.CardId,
            FixedRuleId = transaction.FixedRuleId,
            Installments = transaction.Installments is null
                ? null
                : new InstallmentDocument { Index = transaction.Installments.Index, Total = transaction.Installments.Total, GroupId = transaction.Installments.GroupId },
            Tags = transaction.Tags.ToArray(),
            Notes = transaction.Notes,
            Source = new TransactionSourceDocument { Channel = transaction.Source.Channel },
            CreatedAt = transaction.CreatedAt,
            UpdatedAt = transaction.UpdatedAt,
            DeletedAt = transaction.DeletedAt,
            ETag = transaction.ETag
        };
}
