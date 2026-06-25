using System.Globalization;
using System.Net;
using MergeDuo.Aggregates.Domain.Abstractions;
using MergeDuo.Aggregates.Domain.Documents;
using MergeDuo.Aggregates.Domain.Exceptions;
using MergeDuo.Aggregates.Domain.Options;
using MergeDuo.Aggregates.Domain.Rules;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Aggregates.Infra.Cosmos;

public sealed class MonthlyAggregatesRepository : IMonthlyAggregatesRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;

    public MonthlyAggregatesRepository(CosmosClient client, CosmosOptions options, ICosmosDiagnosticsRecorder diagnostics)
    {
        _container = client.GetContainer(options.Database, options.MonthlyAggregatesContainer);
        _diagnostics = diagnostics;
    }

    public async Task<MonthlyAggregateDocument?> GetMonthAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<MonthlyAggregateDocument>(
                AggregateDocumentId.For(userId, yearMonth),
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
            _diagnostics.RecordCosmosOperation("monthlyAggregates", "read_month", response.RequestCharge, throttled: false);
            response.Resource.ETag = response.ETag;
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            RecordFailure("monthlyAggregates", "read_month", ex);
            throw Dependency(ex);
        }
    }

    public async Task<IReadOnlyList<MonthlyAggregateDocument>> ListYearAsync(string userId, int year, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT * FROM c
                WHERE c.userId = @userId
                  AND c.year = @year
                ORDER BY c.monthIdx ASC
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@year", year);

        try
        {
            using var iterator = _container.GetItemQueryIterator<MonthlyAggregateDocument>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(userId),
                    MaxItemCount = 12
                });

            var results = new List<MonthlyAggregateDocument>(12);
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("monthlyAggregates", "list_year", page.RequestCharge, throttled: false);
                results.AddRange(page.Resource);
            }

            return results;
        }
        catch (CosmosException ex)
        {
            RecordFailure("monthlyAggregates", "list_year", ex);
            throw Dependency(ex);
        }
    }

    public async Task<MonthlyAggregateDocument?> GetLatestBeforeAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT TOP 1 *
                FROM c
                WHERE c.userId = @userId
                  AND c.yearMonth < @yearMonth
                ORDER BY c.yearMonth DESC
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@yearMonth", yearMonth.Value);

        try
        {
            using var iterator = _container.GetItemQueryIterator<MonthlyAggregateDocument>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(userId),
                    MaxItemCount = 1
                });

            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("monthlyAggregates", "latest_before", page.RequestCharge, throttled: false);
                return page.Resource.FirstOrDefault();
            }

            return null;
        }
        catch (CosmosException ex)
        {
            RecordFailure("monthlyAggregates", "latest_before", ex);
            throw Dependency(ex);
        }
    }

    public async Task UpsertComputedAsync(MonthlyAggregateDocument aggregate, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.UpsertItemAsync(
                aggregate,
                new PartitionKey(aggregate.UserId),
                new ItemRequestOptions { EnableContentResponseOnWrite = false },
                cancellationToken);
            _diagnostics.RecordCosmosOperation("monthlyAggregates", "upsert_computed", response.RequestCharge, throttled: false);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed ||
                                        ex.StatusCode == HttpStatusCode.Conflict)
        {
            RecordFailure("monthlyAggregates", "upsert_computed", ex);
            throw new AggregatesConflictException("aggregate_write_conflict", "Aggregate write conflict.");
        }
        catch (CosmosException ex)
        {
            RecordFailure("monthlyAggregates", "upsert_computed", ex);
            throw Dependency(ex);
        }
    }

    public async Task<YearMonth?> GetLastAggregateMonthAsync(string userId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT TOP 1 c.yearMonth
                FROM c
                WHERE c.userId = @userId
                ORDER BY c.yearMonth DESC
                """)
            .WithParameter("@userId", userId);

        try
        {
            using var iterator = _container.GetItemQueryIterator<LastAggregateProjection>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(userId),
                    MaxItemCount = 1
                });

            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("monthlyAggregates", "last_month", page.RequestCharge, throttled: false);
                var value = page.Resource.FirstOrDefault()?.YearMonth;
                return YearMonth.TryParse(value, out var yearMonth) ? yearMonth : null;
            }

            return null;
        }
        catch (CosmosException ex)
        {
            RecordFailure("monthlyAggregates", "last_month", ex);
            throw Dependency(ex);
        }
    }

    private sealed class LastAggregateProjection
    {
        public string YearMonth { get; set; } = "";
    }

    private void RecordFailure(string container, string operation, CosmosException ex) =>
        _diagnostics.RecordCosmosOperation(container, operation, ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);

    private static AggregatesDependencyException Dependency(Exception ex) =>
        new("aggregates_dependency_unavailable", "Aggregates dependency unavailable.", ex);
}

public sealed class TransactionsProjectionRepository : ITransactionsProjectionRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;

    public TransactionsProjectionRepository(CosmosClient client, CosmosOptions options, ICosmosDiagnosticsRecorder diagnostics)
    {
        _container = client.GetContainer(options.Database, options.TransactionsContainer);
        _diagnostics = diagnostics;
    }

    public async Task<IReadOnlyList<TransactionProjection>> ListActiveMonthAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT c.id, c.docType, c.userId, c.yearMonth, c.date, c.purchaseDate, c.category, c.description,
                       c.kind, c.amount, c.currency, c.cardId, c.fixedRuleId, c.updatedAt, c.deletedAt
                FROM c
                WHERE c.userId = @userId
                  AND c.yearMonth = @yearMonth
                  AND IS_NULL(c.deletedAt)
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@yearMonth", yearMonth.Value);

        try
        {
            using var iterator = _container.GetItemQueryIterator<TransactionProjection>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKeyBuilder().Add(userId).Add(yearMonth.Value).Build(),
                    MaxItemCount = 100
                });

            var results = new List<TransactionProjection>();
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("transactions", "list_active_month", page.RequestCharge, throttled: false);
                results.AddRange(page.Resource);
            }

            return results;
        }
        catch (CosmosException ex)
        {
            RecordFailure("transactions", "list_active_month", ex);
            throw Dependency(ex);
        }
    }

    public async Task<IReadOnlyList<TransactionProjection>> ListActiveRangeAsync(
        string userId,
        DateOnly fromDate,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        if (fromDate > throughDate)
        {
            return [];
        }

        var query = new QueryDefinition(
                """
                SELECT c.id, c.docType, c.userId, c.yearMonth, c.date, c.purchaseDate, c.category,
                       c.kind, c.amount, c.currency, c.cardId, c.fixedRuleId, c.updatedAt, c.deletedAt
                FROM c
                WHERE c.userId = @userId
                  AND c.date >= @fromDate
                  AND c.date <= @throughDate
                  AND IS_NULL(c.deletedAt)
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@fromDate", fromDate.ToString("yyyy-MM-dd"))
            .WithParameter("@throughDate", throughDate.ToString("yyyy-MM-dd"));

        try
        {
            using var iterator = _container.GetItemQueryIterator<TransactionProjection>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKeyBuilder().Add(userId).Build(),
                    MaxItemCount = 100
                });

            var results = new List<TransactionProjection>();
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("transactions", "list_active_range", page.RequestCharge, throttled: false);
                results.AddRange(page.Resource);
            }

            return results;
        }
        catch (CosmosException ex)
        {
            RecordFailure("transactions", "list_active_range", ex);
            throw Dependency(ex);
        }
    }

    public async Task<SourceWatermarkDocument> GetMonthWatermarkAsync(
        string userId,
        YearMonth yearMonth,
        CancellationToken cancellationToken)
    {
        var partitionKey = new PartitionKeyBuilder().Add(userId).Add(yearMonth.Value).Build();
        try
        {
            return new SourceWatermarkDocument
            {
                MaxTransactionUpdatedAt = await GetMonthMaxUpdatedAtAsync(userId, yearMonth, partitionKey, cancellationToken),
                ActiveTransactionsCount = await GetActiveMonthCountAsync(userId, yearMonth, partitionKey, cancellationToken)
            };
        }
        catch (CosmosException ex)
        {
            RecordFailure("transactions", "month_watermark", ex);
            throw Dependency(ex);
        }
    }

    public async Task<IReadOnlyDictionary<YearMonth, SourceWatermarkDocument>> GetYearWatermarksAsync(
        string userId,
        int year,
        CancellationToken cancellationToken)
    {
        var partitionKey = new PartitionKeyBuilder().Add(userId).Build();
        var results = new Dictionary<YearMonth, SourceWatermarkDocument>();

        try
        {
            await LoadYearMaxUpdatedAtAsync(userId, year, partitionKey, results, cancellationToken);
            await LoadYearActiveCountsAsync(userId, year, partitionKey, results, cancellationToken);
            return results;
        }
        catch (CosmosException ex)
        {
            RecordFailure("transactions", "year_watermarks", ex);
            throw Dependency(ex);
        }
    }

    private async Task<DateTimeOffset?> GetMonthMaxUpdatedAtAsync(
        string userId,
        YearMonth yearMonth,
        PartitionKey partitionKey,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT VALUE MAX(c.updatedAt)
                FROM c
                WHERE c.userId = @userId
                  AND c.yearMonth = @yearMonth
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@yearMonth", yearMonth.Value);

        using var iterator = _container.GetItemQueryIterator<string?>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = partitionKey,
                MaxItemCount = 1
            });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            _diagnostics.RecordCosmosOperation("transactions", "month_watermark_max_updated_at", page.RequestCharge, throttled: false);
            var value = page.Resource.FirstOrDefault();
            return ParseDateTimeOffset(value);
        }

        return null;
    }

    private async Task<int> GetActiveMonthCountAsync(
        string userId,
        YearMonth yearMonth,
        PartitionKey partitionKey,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT VALUE COUNT(1)
                FROM c
                WHERE c.userId = @userId
                  AND c.yearMonth = @yearMonth
                  AND IS_NULL(c.deletedAt)
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@yearMonth", yearMonth.Value);

        using var iterator = _container.GetItemQueryIterator<int>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = partitionKey,
                MaxItemCount = 1
            });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            _diagnostics.RecordCosmosOperation("transactions", "month_watermark_active_count", page.RequestCharge, throttled: false);
            return page.Resource.FirstOrDefault();
        }

        return 0;
    }

    private async Task LoadYearMaxUpdatedAtAsync(
        string userId,
        int year,
        PartitionKey partitionKey,
        Dictionary<YearMonth, SourceWatermarkDocument> results,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT c.yearMonth, MAX(c.updatedAt) AS maxTransactionUpdatedAt
                FROM c
                WHERE c.userId = @userId
                  AND STARTSWITH(c.yearMonth, @yearPrefix)
                GROUP BY c.yearMonth
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@yearPrefix", $"{year}-");

        using var iterator = _container.GetItemQueryIterator<YearMaxUpdatedAtProjection>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = partitionKey,
                MaxItemCount = 100
            });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            _diagnostics.RecordCosmosOperation("transactions", "year_watermarks_max_updated_at", page.RequestCharge, throttled: false);
            foreach (var item in page.Resource)
            {
                if (!YearMonth.TryParse(item.YearMonth, out var yearMonth)) continue;
                var watermark = GetOrCreateWatermark(results, yearMonth);
                watermark.MaxTransactionUpdatedAt = ParseDateTimeOffset(item.MaxTransactionUpdatedAt);
            }
        }
    }

    private async Task LoadYearActiveCountsAsync(
        string userId,
        int year,
        PartitionKey partitionKey,
        Dictionary<YearMonth, SourceWatermarkDocument> results,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT c.yearMonth, COUNT(1) AS activeTransactionsCount
                FROM c
                WHERE c.userId = @userId
                  AND STARTSWITH(c.yearMonth, @yearPrefix)
                  AND IS_NULL(c.deletedAt)
                GROUP BY c.yearMonth
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@yearPrefix", $"{year}-");

        using var iterator = _container.GetItemQueryIterator<YearActiveCountProjection>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = partitionKey,
                MaxItemCount = 100
            });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            _diagnostics.RecordCosmosOperation("transactions", "year_watermarks_active_count", page.RequestCharge, throttled: false);
            foreach (var item in page.Resource)
            {
                if (!YearMonth.TryParse(item.YearMonth, out var yearMonth)) continue;
                var watermark = GetOrCreateWatermark(results, yearMonth);
                watermark.ActiveTransactionsCount = item.ActiveTransactionsCount;
            }
        }
    }

    private static SourceWatermarkDocument GetOrCreateWatermark(
        Dictionary<YearMonth, SourceWatermarkDocument> watermarks,
        YearMonth yearMonth)
    {
        if (!watermarks.TryGetValue(yearMonth, out var watermark))
        {
            watermark = new SourceWatermarkDocument();
            watermarks[yearMonth] = watermark;
        }

        return watermark;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;

    public async Task<MovementTotals> SumTotalsThroughAsync(string userId, DateOnly throughDate, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT c.kind, SUM(c.amount) AS amount
                FROM c
                WHERE c.userId = @userId
                  AND c.date <= @throughDate
                  AND IS_NULL(c.deletedAt)
                GROUP BY c.kind
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@throughDate", throughDate.ToString("yyyy-MM-dd"));

        try
        {
            using var iterator = _container.GetItemQueryIterator<TotalsByKindProjection>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKeyBuilder().Add(userId).Build(),
                    MaxItemCount = 3
                });

            decimal entradas = 0m;
            decimal saidas = 0m;
            decimal aportes = 0m;
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("transactions", "sum_totals", page.RequestCharge, throttled: false);
                foreach (var item in page.Resource)
                {
                    switch (item.Kind)
                    {
                        case AggregateKinds.In:
                            entradas += item.Amount;
                            break;
                        case AggregateKinds.Out:
                            saidas += item.Amount;
                            break;
                        case AggregateKinds.Invest:
                            aportes += item.Amount;
                            break;
                    }
                }
            }

            return new MovementTotals(entradas, saidas, aportes);
        }
        catch (CosmosException ex)
        {
            RecordFailure("transactions", "sum_totals", ex);
            throw Dependency(ex);
        }
    }

    public async Task<decimal> SumInvestmentsThroughAsync(string userId, DateOnly throughDate, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT VALUE SUM(c.amount)
                FROM c
                WHERE c.userId = @userId
                  AND c.kind = "invest"
                  AND c.date <= @throughDate
                  AND IS_NULL(c.deletedAt)
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@throughDate", throughDate.ToString("yyyy-MM-dd"));

        try
        {
            using var iterator = _container.GetItemQueryIterator<decimal?>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKeyBuilder().Add(userId).Build(),
                    MaxItemCount = 1
                });

            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("transactions", "sum_investments", page.RequestCharge, throttled: false);
                return page.Resource.FirstOrDefault() ?? 0m;
            }

            return 0m;
        }
        catch (CosmosException ex)
        {
            RecordFailure("transactions", "sum_investments", ex);
            throw Dependency(ex);
        }
    }

    private void RecordFailure(string container, string operation, CosmosException ex) =>
        _diagnostics.RecordCosmosOperation(container, operation, ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);

    private static AggregatesDependencyException Dependency(Exception ex) =>
        new("aggregates_dependency_unavailable", "Aggregates dependency unavailable.", ex);

    private sealed class TotalsByKindProjection
    {
        public string Kind { get; set; } = "";
        public decimal Amount { get; set; }
    }

    private sealed class YearMaxUpdatedAtProjection
    {
        public string YearMonth { get; set; } = "";
        public string? MaxTransactionUpdatedAt { get; set; }
    }

    private sealed class YearActiveCountProjection
    {
        public string YearMonth { get; set; } = "";
        public int ActiveTransactionsCount { get; set; }
    }
}

public sealed class UsersReadRepository : IUsersReadRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;

    public UsersReadRepository(CosmosClient client, CosmosOptions options, ICosmosDiagnosticsRecorder diagnostics)
    {
        _container = client.GetContainer(options.Database, options.UsersContainer);
        _diagnostics = diagnostics;
    }

    public async Task<decimal> GetStartingBalanceAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<UserDocument>(
                userId,
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
            _diagnostics.RecordCosmosOperation("users", "read_starting_balance", response.RequestCharge, throttled: false);
            return response.Resource.DeletedAt is null ? response.Resource.Financial.StartingBalance : 0m;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return 0m;
        }
        catch (CosmosException ex)
        {
            _diagnostics.RecordCosmosOperation("users", "read_starting_balance", ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);
            throw new AggregatesDependencyException("aggregates_dependency_unavailable", "Aggregates dependency unavailable.", ex);
        }
    }
}

public sealed class FixedRulesProjectionRepository : IFixedRulesProjectionRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;

    public FixedRulesProjectionRepository(CosmosClient client, CosmosOptions options, ICosmosDiagnosticsRecorder diagnostics)
    {
        _container = client.GetContainer(options.Database, options.FixedRulesContainer);
        _diagnostics = diagnostics;
    }

    public async Task<IReadOnlyList<FixedRuleDocument>> ListActiveCandidatesAsync(
        string userId,
        DateOnly fromDate,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT *
                FROM c
                WHERE c.docType = "fixedRule"
                  AND c.userId = @userId
                  AND c.active = true
                  AND (NOT IS_DEFINED(c.deletedAt) OR IS_NULL(c.deletedAt))
                  AND c.startsAt <= @throughDate
                  AND (NOT IS_DEFINED(c.endsAt) OR IS_NULL(c.endsAt) OR c.endsAt >= @fromDate)
                """)
            .WithParameter("@userId", userId)
            .WithParameter("@fromDate", fromDate.ToString("yyyy-MM-dd"))
            .WithParameter("@throughDate", throughDate.ToString("yyyy-MM-dd"));

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
                _diagnostics.RecordCosmosOperation("fixedRules", "list_projection_candidates", page.RequestCharge, throttled: false);
                results.AddRange(page.Resource);
            }

            return results;
        }
        catch (CosmosException ex)
        {
            _diagnostics.RecordCosmosOperation("fixedRules", "list_projection_candidates", ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);
            throw new AggregatesDependencyException("aggregates_dependency_unavailable", "Aggregates dependency unavailable.", ex);
        }
    }
}

public sealed class CardsProjectionRepository : ICardsProjectionRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;

    public CardsProjectionRepository(CosmosClient client, CosmosOptions options, ICosmosDiagnosticsRecorder diagnostics)
    {
        _container = client.GetContainer(options.Database, options.CardsContainer);
        _diagnostics = diagnostics;
    }

    public async Task<CardDocument?> GetActiveAsync(string userId, string cardId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<CardDocument>(
                cardId,
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
            _diagnostics.RecordCosmosOperation("cards", "read_projection_card", response.RequestCharge, throttled: false);
            var card = response.Resource;
            return card.DeletedAt is null && card.UserId == userId ? card : null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            _diagnostics.RecordCosmosOperation("cards", "read_projection_card", ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);
            throw new AggregatesDependencyException("aggregates_dependency_unavailable", "Aggregates dependency unavailable.", ex);
        }
    }
}

public sealed class PartnershipsReadRepository : IPartnershipsReadRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;

    public PartnershipsReadRepository(CosmosClient client, CosmosOptions options, ICosmosDiagnosticsRecorder diagnostics)
    {
        _container = client.GetContainer(options.Database, options.PartnershipsContainer);
        _diagnostics = diagnostics;
    }

    public async Task<PartnershipDocument?> GetActivePartnerAsync(string userId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT * FROM c
                WHERE c.docType = "partnership"
                  AND c.userId = @userId
                  AND c.status = "active"
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
                _diagnostics.RecordCosmosOperation("partnerships", "get_active_partner", page.RequestCharge, throttled: false);
                return page.Resource.FirstOrDefault();
            }

            return null;
        }
        catch (CosmosException ex)
        {
            RecordFailure("partnerships", "get_active_partner", ex);
            throw Dependency(ex);
        }
    }

    public async Task<bool> IsActivePartnerAsync(string userId, string partnerUserId, CancellationToken cancellationToken)
    {
        var partner = await GetActivePartnerAsync(userId, cancellationToken);
        return partner is not null &&
               string.Equals(partner.PartnerUserId, partnerUserId, StringComparison.Ordinal);
    }

    private void RecordFailure(string container, string operation, CosmosException ex) =>
        _diagnostics.RecordCosmosOperation(container, operation, ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);

    private static AggregatesDependencyException Dependency(Exception ex) =>
        new("aggregates_dependency_unavailable", "Aggregates dependency unavailable.", ex);
}

public sealed class CosmosReadinessProbe : IAggregatesReadinessProbe
{
    private readonly IReadOnlyList<Container> _containers;

    public CosmosReadinessProbe(CosmosClient client, CosmosOptions options, ChangeFeedOptions changeFeedOptions)
    {
        var containers = new List<Container>
        {
            client.GetContainer(options.Database, options.MonthlyAggregatesContainer),
            client.GetContainer(options.Database, options.TransactionsContainer),
            client.GetContainer(options.Database, options.PartnershipsContainer),
            client.GetContainer(options.Database, options.UsersContainer),
            client.GetContainer(options.Database, options.FixedRulesContainer),
            client.GetContainer(options.Database, options.CardsContainer)
        };

        if (changeFeedOptions.Enabled)
        {
            containers.Add(client.GetContainer(options.Database, options.LeaseContainer));
        }

        _containers = containers;
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        foreach (var container in _containers)
        {
            await container.ReadContainerAsync(cancellationToken: cancellationToken);
        }

        return true;
    }
}
