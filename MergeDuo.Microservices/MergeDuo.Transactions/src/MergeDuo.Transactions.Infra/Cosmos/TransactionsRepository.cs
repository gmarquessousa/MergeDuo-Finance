using System.Net;
using MergeDuo.Transactions.Domain.Abstractions;
using MergeDuo.Transactions.Domain.Documents;
using MergeDuo.Transactions.Domain.Exceptions;
using MergeDuo.Transactions.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Transactions.Infra.Cosmos;

public sealed class TransactionsRepository : ITransactionsRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;

    public TransactionsRepository(CosmosClient client, CosmosOptions options, ICosmosDiagnosticsRecorder diagnostics)
    {
        _container = client.GetContainer(options.Database, options.TransactionsContainer);
        _diagnostics = diagnostics;
    }

    public async Task<TransactionsPage> ListMonthAsync(
        string userId,
        string yearMonth,
        TransactionListFilters filters,
        int pageSize,
        string? continuationToken,
        SortDirection sort,
        CancellationToken cancellationToken)
    {
        var order = sort == SortDirection.DateAsc ? "ASC" : "DESC";
        var sql =
            $"""
             SELECT * FROM c
             WHERE c.userId = @userId
               AND c.yearMonth = @yearMonth
               AND IS_NULL(c.deletedAt)
               {(filters.Category is null ? "" : "AND c.category = @category")}
               {(filters.CardId is null ? "" : "AND c.cardId = @cardId")}
             ORDER BY c.date {order}
             """;

        var query = new QueryDefinition(sql)
            .WithParameter("@userId", userId)
            .WithParameter("@yearMonth", yearMonth);

        if (filters.Category is not null)
        {
            query.WithParameter("@category", filters.Category);
        }

        if (filters.CardId is not null)
        {
            query.WithParameter("@cardId", filters.CardId);
        }

        try
        {
            using var iterator = _container.GetItemQueryIterator<TransactionDocument>(
                query,
                continuationToken,
                new QueryRequestOptions
                {
                    PartitionKey = FullPk(userId, yearMonth),
                    MaxItemCount = pageSize
                });

            if (!iterator.HasMoreResults)
            {
                return new TransactionsPage([], null);
            }

            var page = await iterator.ReadNextAsync(cancellationToken);
            _diagnostics.RecordCosmosOperation("transactions", "list_month", page.RequestCharge, throttled: false);

            var items = page.Resource.ToArray();
            await EnsureEtagsAsync(items, cancellationToken);

            return new TransactionsPage(items, page.ContinuationToken);
        }
        catch (CosmosException ex)
        {
            RecordFailure("list_month", ex);
            throw Dependency(ex);
        }
    }

    public async Task<TransactionDocument?> GetByIdAsync(
        string userId,
        string yearMonth,
        string transactionId,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<TransactionDocument>(
                transactionId,
                FullPk(userId, yearMonth),
                cancellationToken: cancellationToken);
            _diagnostics.RecordCosmosOperation("transactions", "read_item", response.RequestCharge, throttled: false);
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
            throw Dependency(ex);
        }
    }

    public async Task CreateAsync(TransactionDocument transaction, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.CreateItemAsync(
                transaction,
                FullPk(transaction.UserId, transaction.YearMonth),
                requestOptions: new ItemRequestOptions { EnableContentResponseOnWrite = false },
                cancellationToken: cancellationToken);
            _diagnostics.RecordCosmosOperation("transactions", "create_item", response.RequestCharge, throttled: false);
            transaction.ETag = response.ETag;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            throw new TransactionsConflictException("transaction_conflict", "Transaction conflict.");
        }
        catch (CosmosException ex)
        {
            RecordFailure("create_item", ex);
            throw Dependency(ex);
        }
    }

    public async Task<TransactionDocument> PatchAsync(
        TransactionDocument transaction,
        TransactionPatch patch,
        string ifMatchEtag,
        bool clientProvidedEtag,
        CancellationToken cancellationToken)
    {
        var replacement = new TransactionDocument
        {
            Id = transaction.Id,
            DocType = transaction.DocType,
            SchemaVersion = transaction.SchemaVersion,
            UserId = transaction.UserId,
            YearMonth = transaction.YearMonth,
            Date = patch.Date,
            PurchaseDate = patch.PurchaseDate,
            Category = patch.Category,
            Kind = patch.Kind,
            Description = patch.Description,
            Amount = patch.Amount,
            Currency = patch.Currency,
            OwnerLabel = patch.OwnerLabel,
            CardId = patch.CardId,
            FixedRuleId = patch.FixedRuleId,
            Installments = transaction.Installments,
            Tags = patch.Tags,
            Notes = patch.Notes,
            Source = new TransactionSourceDocument { Channel = patch.FixedRuleId is null ? "manual" : "fixed_rule" },
            CreatedAt = transaction.CreatedAt,
            UpdatedAt = patch.UpdatedAt,
            DeletedAt = transaction.DeletedAt
        };

        try
        {
            var response = await _container.ReplaceItemAsync(
                replacement,
                replacement.Id,
                FullPk(transaction.UserId, transaction.YearMonth),
                new ItemRequestOptions
                {
                    IfMatchEtag = ifMatchEtag,
                    EnableContentResponseOnWrite = true
                },
                cancellationToken);
            _diagnostics.RecordCosmosOperation("transactions", "replace_item", response.RequestCharge, throttled: false);
            response.Resource.ETag = response.ETag;
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw clientProvidedEtag
                ? new TransactionsPreconditionFailedException("precondition_failed", "Transaction precondition failed.")
                : new TransactionsConflictException("transaction_conflict", "Transaction update conflicted.");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new TransactionsNotFoundException("transaction_not_found", "Transaction not found.");
        }
        catch (CosmosException ex)
        {
            RecordFailure("patch_item", ex);
            throw Dependency(ex);
        }
    }

    public async Task CreateMovedCopyAsync(TransactionDocument transaction, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.CreateItemAsync(
                transaction,
                FullPk(transaction.UserId, transaction.YearMonth),
                requestOptions: new ItemRequestOptions { EnableContentResponseOnWrite = true },
                cancellationToken);
            _diagnostics.RecordCosmosOperation("transactions", "create_moved_copy", response.RequestCharge, throttled: false);
            transaction.ETag = response.ETag;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            throw new TransactionsConflictException("transaction_conflict", "Transaction move conflicted.");
        }
        catch (CosmosException ex)
        {
            RecordFailure("create_moved_copy", ex);
            throw Dependency(ex);
        }
    }

    public async Task SoftDeleteAsync(
        TransactionDocument transaction,
        DateTimeOffset deletedAt,
        string ifMatchEtag,
        bool clientProvidedEtag,
        CancellationToken cancellationToken)
    {
        var operations = new[]
        {
            PatchOperation.Set("/deletedAt", deletedAt),
            PatchOperation.Set("/updatedAt", deletedAt)
        };

        try
        {
            var response = await _container.PatchItemAsync<TransactionDocument>(
                transaction.Id,
                FullPk(transaction.UserId, transaction.YearMonth),
                operations,
                new PatchItemRequestOptions
                {
                    IfMatchEtag = ifMatchEtag,
                    EnableContentResponseOnWrite = false
                },
                cancellationToken);
            _diagnostics.RecordCosmosOperation("transactions", "soft_delete", response.RequestCharge, throttled: false);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw clientProvidedEtag
                ? new TransactionsPreconditionFailedException("precondition_failed", "Transaction precondition failed.")
                : new TransactionsConflictException("transaction_conflict", "Transaction delete conflicted.");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new TransactionsNotFoundException("transaction_not_found", "Transaction not found.");
        }
        catch (CosmosException ex)
        {
            RecordFailure("soft_delete", ex);
            throw Dependency(ex);
        }
    }

    public async Task<IReadOnlyList<TransactionDocument>> ListGroupAsync(string userId, string groupId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT * FROM c
                WHERE c.userId = @userId
                  AND c.installments.groupId = @groupId
                  AND IS_NULL(c.deletedAt)
                ORDER BY c.installments.index ASC
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@groupId", groupId);

        try
        {
            using var iterator = _container.GetItemQueryIterator<TransactionDocument>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = UserPk(userId),
                    MaxItemCount = 100
                });
            var results = new List<TransactionDocument>();
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("transactions", "list_group", page.RequestCharge, throttled: false);
                results.AddRange(page.Resource);
            }

            return results;
        }
        catch (CosmosException ex)
        {
            RecordFailure("list_group", ex);
            throw Dependency(ex);
        }
    }

    public async Task<IReadOnlyList<TransactionDocument>> ListTaggedAsync(
        string userId,
        bool includeTransactionDetails,
        CancellationToken cancellationToken)
    {
        var projection = includeTransactionDetails
            ? "c.id, c.userId, c.yearMonth, c.date, c.purchaseDate, c.category, c.kind, c.description, c.amount, c.currency, c.ownerLabel, c.cardId, c.fixedRuleId, c.installments, c.tags, c.notes, c.source, c.createdAt, c.updatedAt"
            : "c.tags, c.kind, c.amount";
        var query = new QueryDefinition(
                $"""
                SELECT {projection} FROM c
                WHERE c.userId = @userId
                  AND (NOT IS_DEFINED(c.deletedAt) OR IS_NULL(c.deletedAt))
                  AND IS_DEFINED(c.tags)
                  AND IS_ARRAY(c.tags)
                  AND ARRAY_LENGTH(c.tags) > 0
                """)
            .WithParameter("@userId", userId);

        try
        {
            using var iterator = _container.GetItemQueryIterator<TransactionDocument>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = UserPk(userId),
                    MaxItemCount = 200
                });
            var results = new List<TransactionDocument>();
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("transactions", "tag_analytics", page.RequestCharge, throttled: false);
                results.AddRange(page.Resource);
            }

            return results;
        }
        catch (CosmosException ex)
        {
            RecordFailure("tag_analytics", ex);
            throw Dependency(ex);
        }
    }

    public async Task<CardUsageTotals> GetCardUsageAsync(string userId, string yearMonth, string cardId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT c.amount, c.installments
                FROM c
                WHERE c.userId = @userId
                  AND c.yearMonth = @yearMonth
                  AND c.cardId = @cardId
                  AND c.category = "credit_card"
                  AND IS_NULL(c.deletedAt)
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@yearMonth", yearMonth)
            .WithParameter("@cardId", cardId);

        try
        {
            using var iterator = _container.GetItemQueryIterator<CardUsageProjection>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = FullPk(userId, yearMonth),
                    MaxItemCount = 100
                });
            decimal total = 0;
            var count = 0;
            var installmentCount = 0;
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("transactions", "card_usage", page.RequestCharge, throttled: false);
                foreach (var item in page.Resource)
                {
                    total += item.Amount;
                    count++;
                    if (item.Installments is not null)
                    {
                        installmentCount++;
                    }
                }
            }

            return new CardUsageTotals(cardId, yearMonth, "BRL", total, count, installmentCount);
        }
        catch (CosmosException ex)
        {
            RecordFailure("card_usage", ex);
            throw Dependency(ex);
        }
    }

    private static PartitionKey FullPk(string userId, string yearMonth) =>
        new PartitionKeyBuilder().Add(userId).Add(yearMonth).Build();

    private static PartitionKey UserPk(string userId) =>
        new PartitionKeyBuilder().Add(userId).Build();

    private void RecordFailure(string operation, CosmosException ex) =>
        _diagnostics.RecordCosmosOperation("transactions", operation, ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);

    private static TransactionsDependencyException Dependency(Exception ex) =>
        new("transactions_dependency_unavailable", "Transactions dependency unavailable.", ex);

    private async Task EnsureEtagsAsync(IEnumerable<TransactionDocument> transactions, CancellationToken cancellationToken)
    {
        foreach (var transaction in transactions)
        {
            if (!string.IsNullOrWhiteSpace(transaction.ETag))
            {
                continue;
            }

            try
            {
                var response = await _container.ReadItemAsync<TransactionDocument>(
                    transaction.Id,
                    FullPk(transaction.UserId, transaction.YearMonth),
                    cancellationToken: cancellationToken);
                _diagnostics.RecordCosmosOperation("transactions", "list_read_etag", response.RequestCharge, throttled: false);
                transaction.ETag = response.ETag;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // The item changed between query and ETag hydration. Keep the list response best-effort.
            }
        }
    }

    private sealed class CardUsageProjection
    {
        public decimal Amount { get; set; }
        public InstallmentDocument? Installments { get; set; }
    }
}
