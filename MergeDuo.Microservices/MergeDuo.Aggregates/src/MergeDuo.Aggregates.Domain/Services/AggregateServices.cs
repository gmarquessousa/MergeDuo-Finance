using MergeDuo.Aggregates.Domain.Abstractions;
using MergeDuo.Aggregates.Domain.Contracts;
using MergeDuo.Aggregates.Domain.Documents;
using MergeDuo.Aggregates.Domain.Exceptions;
using MergeDuo.Aggregates.Domain.Options;
using MergeDuo.Aggregates.Domain.Rules;

namespace MergeDuo.Aggregates.Domain.Services;

public sealed class AggregateQueryService(
    IMonthlyAggregatesRepository aggregates,
    ITransactionsProjectionRepository transactions,
    IPartnershipsReadRepository partnerships,
    IUsersReadRepository users,
    IAggregateRecomputeService recompute,
    AggregatesOptions options,
    TimeProvider clock) : IAggregateQueryService
{
    private readonly TimeSpan _staleAfter = TimeSpan.FromMinutes(options.StaleAfterMinutes);

    public async Task<MonthlyAggregateResponse> GetMonthAsync(
        string requesterUserId,
        string targetUserId,
        YearMonth yearMonth,
        CancellationToken cancellationToken)
    {
        await EnsureCanReadAsync(requesterUserId, targetUserId, cancellationToken);
        var document = await aggregates.GetMonthAsync(targetUserId, yearMonth, cancellationToken);
        var refreshed = await RefreshCurrentOrFutureMonthAsync(targetUserId, yearMonth, document, cancellationToken);
        if (refreshed.Document is not null)
        {
            return AggregateMapping.ToResponse(
                refreshed.Document,
                refreshed.Source,
                refreshed.Freshness.IsStale,
                refreshed.Freshness.Reason);
        }

        return document is null
            ? await CarryForwardFactory.MonthAsync(targetUserId, yearMonth, aggregates, users, options.SourceVersion, cancellationToken)
            : AggregateMapping.ToResponse(document, "stored", (await ResolveFreshnessAsync(document, cancellationToken)).IsStale);
    }

    private async Task<(MonthlyAggregateDocument? Document, string Source, AggregateFreshness Freshness)> RefreshCurrentOrFutureMonthAsync(
        string targetUserId,
        YearMonth yearMonth,
        MonthlyAggregateDocument? document,
        CancellationToken cancellationToken)
    {
        var currentMonth = YearMonth.FromDate(BusinessClock.Today(clock, options.BusinessTimeZone));
        var freshness = document is null
            ? AggregateFreshness.Stale("missing")
            : await ResolveFreshnessAsync(document, cancellationToken);

        if (yearMonth.IsBeforeOrEqual(currentMonth.AddMonths(-1)))
        {
            if (document is not null && freshness.Reason == "source_behind")
            {
                var recomputed = await TryRecomputeOnReadAsync(targetUserId, yearMonth, cancellationToken);
                if (recomputed is not null)
                {
                    return (recomputed, "recomputed", await ResolveFreshnessAsync(recomputed, cancellationToken));
                }
            }

            return (document, "stored", freshness);
        }

        if (!NeedsRecomputeOnRead(document, freshness))
        {
            return (document, "stored", freshness);
        }

        var refreshed = await TryRecomputeOnReadAsync(targetUserId, yearMonth, cancellationToken);
        if (refreshed is null)
        {
            return (document, "stored", freshness);
        }

        return (refreshed, document is null ? "cold_start" : "recomputed", await ResolveFreshnessAsync(refreshed, cancellationToken));
    }

    private bool NeedsRecomputeOnRead(MonthlyAggregateDocument? document, AggregateFreshness freshness)
    {
        if (document is null) return true;
        if (document.DailyBalances.Count == 0) return true;
        return freshness.IsStale;
    }

    public async Task<YearAggregatesResponse> GetYearAsync(
        string requesterUserId,
        string targetUserId,
        int year,
        CancellationToken cancellationToken)
    {
        _ = YearMonth.FromRoute(year, 1);
        await EnsureCanReadAsync(requesterUserId, targetUserId, cancellationToken);

        var documents = await aggregates.ListYearAsync(targetUserId, year, cancellationToken);
        var sourceWatermarks = await transactions.GetYearWatermarksAsync(targetUserId, year, cancellationToken);
        var byMonth = new Dictionary<int, MonthlyAggregateDocument>();
        foreach (var document in documents)
        {
            if (!byMonth.TryAdd(document.MonthIdx, document))
            {
                throw new AggregatesConflictException("duplicate_aggregate_detected", "Duplicate aggregate detected.");
            }
        }

        var months = new List<MonthlyAggregateResponse>(12);
        var carry = await aggregates.GetLatestBeforeAsync(targetUserId, new YearMonth(year, 1), cancellationToken);
        var carriedSaldo = carry?.Totals.Saldo ?? await users.GetStartingBalanceAsync(targetUserId, cancellationToken);
        var carriedInvestido = carry?.Totals.Investido ?? 0m;
        var carriedIncludesProjected = carry?.Projection.IncludesProjected ?? false;
        var carriedProjectedCount = carry?.Projection.ProjectedCount ?? 0;
        var carriedProjectionAsOfDate = carry?.Projection.AsOfDate;

        foreach (var month in Enumerable.Range(1, 12))
        {
            var yearMonth = new YearMonth(year, month);
            if (byMonth.TryGetValue(yearMonth.MonthIdx, out var document))
            {
                var sourceWatermark = sourceWatermarks.GetValueOrDefault(yearMonth) ?? new SourceWatermarkDocument();
                var freshness = ResolveFreshness(document, sourceWatermark);
                if (freshness.Reason == "source_behind")
                {
                    var recomputed = await TryRecomputeOnReadAsync(targetUserId, yearMonth, cancellationToken);
                    if (recomputed is not null)
                    {
                        document = recomputed;
                        freshness = await ResolveFreshnessAsync(document, cancellationToken);
                    }
                }

                carriedSaldo = document.Totals.Saldo;
                carriedInvestido = document.Totals.Investido;
                carriedIncludesProjected = document.Projection.IncludesProjected;
                carriedProjectedCount = document.Projection.ProjectedCount;
                carriedProjectionAsOfDate = document.Projection.AsOfDate;
                months.Add(AggregateMapping.ToResponse(document, "stored", freshness.IsStale, freshness.Reason));
                continue;
            }

            months.Add(CarryForwardFactory.Month(
                targetUserId,
                yearMonth,
                carriedSaldo,
                carriedInvestido,
                options.SourceVersion,
                carriedIncludesProjected,
                carriedProjectedCount,
                carriedProjectionAsOfDate));
        }

        return new YearAggregatesResponse(targetUserId, year, months);
    }

    private async Task EnsureCanReadAsync(string requesterUserId, string targetUserId, CancellationToken cancellationToken)
    {
        if (!UserIdRules.IsValid(targetUserId))
        {
            throw new AggregatesBadRequestException("invalid_user_id", "Invalid user id.");
        }

        if (string.Equals(requesterUserId, targetUserId, StringComparison.Ordinal))
        {
            return;
        }

        if (!await partnerships.IsActivePartnerAsync(requesterUserId, targetUserId, cancellationToken))
        {
            throw new AggregatesForbiddenException("aggregate_access_denied", "Aggregate access denied.");
        }
    }

    private async Task<AggregateFreshness> ResolveFreshnessAsync(
        MonthlyAggregateDocument document,
        CancellationToken cancellationToken)
    {
        if (document.SourceVersion < options.SourceVersion)
        {
            return AggregateFreshness.Stale("source_version");
        }

        if (document.ComputedAt <= clock.GetUtcNow().Subtract(_staleAfter))
        {
            return AggregateFreshness.Stale("age");
        }

        var sourceWatermark = await transactions.GetMonthWatermarkAsync(
            document.UserId,
            new YearMonth(document.Year, document.MonthIdx + 1),
            cancellationToken);
        return ResolveFreshness(document, sourceWatermark);
    }

    private AggregateFreshness ResolveFreshness(
        MonthlyAggregateDocument document,
        SourceWatermarkDocument sourceWatermark)
    {
        var aggregateWatermark = document.SourceWatermark ?? new SourceWatermarkDocument();

        if (sourceWatermark.MaxTransactionUpdatedAt > aggregateWatermark.MaxTransactionUpdatedAt ||
            sourceWatermark.ActiveTransactionsCount != aggregateWatermark.ActiveTransactionsCount)
        {
            return AggregateFreshness.Stale("source_behind");
        }

        return AggregateFreshness.Fresh();
    }

    private async Task<MonthlyAggregateDocument?> TryRecomputeOnReadAsync(
        string targetUserId,
        YearMonth yearMonth,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(Math.Clamp(options.DependencyTimeoutSeconds, 1, 5)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            await recompute.RecomputeMonthAsync(targetUserId, yearMonth, linked.Token);
            return await aggregates.GetMonthAsync(targetUserId, yearMonth, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (AggregatesDependencyException)
        {
            return null;
        }
    }
}

public sealed record AggregateFreshness(bool IsStale, string? Reason)
{
    public static AggregateFreshness Fresh() => new(false, null);
    public static AggregateFreshness Stale(string reason) => new(true, reason);
}

public sealed class AggregateRecomputeService(
    IMonthlyAggregatesRepository aggregates,
    ITransactionsProjectionRepository transactions,
    IPartnershipsReadRepository partnerships,
    IUsersReadRepository users,
    FixedRuleProjectionService projectionService,
    AggregateCalculator calculator,
    AggregateRebuildPlanner planner,
    TimeProvider clock,
    AggregatesOptions options) : IAggregateRecomputeService
{
    public async Task RecomputeForChangeAsync(string userId, YearMonth changedMonth, CancellationToken cancellationToken)
    {
        var months = await planner.PlanAsync(userId, changedMonth, cancellationToken);
        var partner = await partnerships.GetActivePartnerAsync(userId, cancellationToken);
        foreach (var month in months)
        {
            await RecomputeMonthAsync(userId, month, cancellationToken);

            if (partner is { Status: "active" } && YearMonth.FromDate(partner.MergedSince).IsBeforeOrEqual(month))
            {
                await RecomputeMonthAsync(partner.PartnerUserId, month, cancellationToken);
            }
        }
    }

    public async Task RecomputeForFixedRuleChangeAsync(FixedRuleDocument fixedRule, CancellationToken cancellationToken)
    {
        if (!UserIdRules.IsValid(fixedRule.UserId))
        {
            throw new AggregatesBadRequestException("invalid_user_id", "Invalid user id.");
        }

        if (!FixedRuleProjectionService.TryParseDate(fixedRule.StartsAt, out _))
        {
            throw new AggregatesBadRequestException("invalid_fixed_rule", "Invalid fixed rule.");
        }

        var businessDate = BusinessClock.Today(clock, options.BusinessTimeZone);
        await RecomputeForChangeAsync(fixedRule.UserId, YearMonth.FromDate(businessDate), cancellationToken);
    }

    public async Task RecomputeForPartnershipChangeAsync(PartnershipDocument partnership, CancellationToken cancellationToken)
    {
        if (!UserIdRules.IsValid(partnership.UserId) || !UserIdRules.IsValid(partnership.PartnerUserId))
        {
            throw new AggregatesBadRequestException("invalid_user_id", "Invalid user id.");
        }

        var from = YearMonth.FromDate(partnership.MergedSince);
        var to = await PartnershipRecomputeEndAsync(partnership, cancellationToken);
        var current = from;
        while (current.IsBeforeOrEqual(to))
        {
            await RecomputeMonthAsync(partnership.UserId, current, cancellationToken);
            await RecomputeMonthAsync(partnership.PartnerUserId, current, cancellationToken);
            current = current.AddMonths(1);
        }
    }

    public async Task RecomputeMonthAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken)
    {
        var ownerTransactions = await transactions.ListActiveMonthAsync(userId, yearMonth, cancellationToken);
        var sourceWatermark = await transactions.GetMonthWatermarkAsync(userId, yearMonth, cancellationToken);
        var businessDate = BusinessClock.Today(clock, options.BusinessTimeZone);
        var startingBalance = await users.GetStartingBalanceAsync(userId, cancellationToken);
        var accumulatedActual = await transactions.SumTotalsThroughAsync(userId, yearMonth.LastDay, cancellationToken);
        IReadOnlyList<TransactionProjection> projectedThrough = [];
        if (businessDate < yearMonth.LastDay)
        {
            var projectionRangeStart = new DateOnly(businessDate.Year, businessDate.Month, 1);
            var actualRange = await transactions.ListActiveRangeAsync(userId, projectionRangeStart, yearMonth.LastDay, cancellationToken);
            projectedThrough = await projectionService.ProjectAsync(
                userId,
                businessDate,
                yearMonth.LastDay,
                actualRange,
                cancellationToken);
        }
        var projectedMonth = projectedThrough
            .Where(x => x.YearMonth == yearMonth.Value)
            .ToArray();
        var ownerTransactionsWithProjections = ownerTransactions.Concat(projectedMonth).ToArray();
        var projectedTotalsThrough = AggregateCalculator.CalculateTotals(projectedThrough);
        var projectedSaldoDelta = projectedTotalsThrough.Entradas - projectedTotalsThrough.Saidas - projectedTotalsThrough.Aportes;

        var saldo = startingBalance + accumulatedActual.SaldoDelta + projectedSaldoDelta;
        var investido = accumulatedActual.Aportes + projectedTotalsThrough.Aportes;

        decimal saldoHoje = 0m;
        decimal investidoHoje = 0m;
        if (yearMonth == YearMonth.FromDate(businessDate))
        {
            var todayActual = await transactions.SumTotalsThroughAsync(userId, businessDate, cancellationToken);
            saldoHoje = startingBalance + todayActual.SaldoDelta;
            investidoHoje = todayActual.Aportes;
        }

        IReadOnlyList<TransactionProjection> partnerTransactions = [];
        string? partnerUserId = null;
        var partner = await partnerships.GetActivePartnerAsync(userId, cancellationToken);
        if (partner is { Status: "active" } && YearMonth.FromDate(partner.MergedSince).IsBeforeOrEqual(yearMonth))
        {
            partnerUserId = partner.PartnerUserId;
            partnerTransactions = await transactions.ListActiveMonthAsync(partner.PartnerUserId, yearMonth, cancellationToken);
        }

        var aggregate = calculator.Compute(
            userId,
            yearMonth,
            ownerTransactionsWithProjections,
            saldo,
            investido,
            saldoHoje,
            investidoHoje,
            businessDate,
            projectedThrough.Count > 0,
            projectedThrough.Count,
            partnerUserId,
            partnerTransactions,
            clock.GetUtcNow());
        aggregate.SourceWatermark = sourceWatermark;

        await aggregates.UpsertComputedAsync(aggregate, cancellationToken);
    }

    public async Task BackfillAggregatesAsync(string userId, YearMonth from, YearMonth to, CancellationToken cancellationToken)
    {
        var current = from;
        while (current.IsBeforeOrEqual(to))
        {
            await RecomputeMonthAsync(userId, current, cancellationToken);
            current = current.AddMonths(1);
        }
    }

    public Task BackfillYearAsync(string userId, int year, CancellationToken cancellationToken) =>
        BackfillAggregatesAsync(userId, new YearMonth(year, 1), new YearMonth(year, 12), cancellationToken);

    private async Task<YearMonth> PartnershipRecomputeEndAsync(PartnershipDocument partnership, CancellationToken cancellationToken)
    {
        var businessToday = BusinessClock.Today(clock, options.BusinessTimeZone);
        var currentMonth = YearMonth.FromDate(businessToday);
        var ownerLast = await aggregates.GetLastAggregateMonthAsync(partnership.UserId, cancellationToken);
        var partnerLast = await aggregates.GetLastAggregateMonthAsync(partnership.PartnerUserId, cancellationToken);
        var end = Max(currentMonth, ownerLast ?? currentMonth);
        return Max(end, partnerLast ?? currentMonth);
    }

    private static YearMonth Max(YearMonth left, YearMonth right) =>
        left.IsBeforeOrEqual(right) ? right : left;
}

public sealed class AggregateRebuildPlanner(
    IMonthlyAggregatesRepository aggregates,
    TimeProvider clock,
    AggregatesOptions options)
{
    public async Task<IReadOnlyList<YearMonth>> PlanAsync(string userId, YearMonth changedMonth, CancellationToken cancellationToken)
    {
        var businessToday = BusinessClock.Today(clock, options.BusinessTimeZone);
        var currentMonth = YearMonth.FromDate(businessToday);
        var lastAggregateMonth = await aggregates.GetLastAggregateMonthAsync(userId, cancellationToken);
        var projectionEnd = currentMonth.AddMonths(Math.Max(0, options.ProjectionMonthsAhead));
        var end = Max(Max(projectionEnd, lastAggregateMonth ?? changedMonth), changedMonth);

        var results = new List<YearMonth>();
        var cursor = changedMonth;
        while (cursor.IsBeforeOrEqual(end) && results.Count < options.MaxRebuildMonthsPerChange)
        {
            results.Add(cursor);
            cursor = cursor.AddMonths(1);
        }

        return results;
    }

    private static YearMonth Max(YearMonth left, YearMonth right) =>
        left.IsBeforeOrEqual(right) ? right : left;
}

public sealed class FixedRuleProjectionService(
    IFixedRulesProjectionRepository fixedRules,
    ICardsProjectionRepository cards)
{
    public async Task<IReadOnlyList<TransactionProjection>> ProjectAsync(
        string userId,
        DateOnly businessDate,
        DateOnly throughDate,
        IReadOnlyList<TransactionProjection> actualTransactions,
        CancellationToken cancellationToken)
    {
        if (throughDate <= businessDate)
        {
            return [];
        }

        var fromDate = businessDate.AddDays(1);
        var candidateFrom = fromDate.AddMonths(-2);
        var rules = await fixedRules.ListActiveCandidatesAsync(userId, candidateFrom, throughDate, cancellationToken);
        if (rules.Count == 0)
        {
            return [];
        }

        var materialized = actualTransactions
            .Where(x => !string.IsNullOrWhiteSpace(x.FixedRuleId))
            .Select(x => OccurrenceKey(x.FixedRuleId!, x.PurchaseDate ?? x.Date))
            .ToHashSet(StringComparer.Ordinal);
        var businessMonth = YearMonth.FromDate(businessDate);

        var projected = new List<TransactionProjection>();
        var cardCache = new Dictionary<string, CardDocument?>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (!IsProjectableRule(rule) ||
                !TryParseDate(rule.StartsAt, out var startsAt))
            {
                continue;
            }

            var endsAt = TryParseDate(rule.EndsAt, out var parsedEndsAt)
                ? parsedEndsAt
                : (DateOnly?)null;

            var cursor = new DateOnly(candidateFrom.Year, candidateFrom.Month, 1);
            var endMonth = new DateOnly(throughDate.Year, throughDate.Month, 1);
            while (cursor <= endMonth)
            {
                var occurrence = ResolveOccurrenceDate(rule.Schedule, cursor.Year, cursor.Month);
                cursor = cursor.AddMonths(1);

                if (occurrence < startsAt || (endsAt is not null && occurrence > endsAt.Value))
                {
                    continue;
                }

                var cashDate = occurrence;
                string? cardId = null;
                if (rule.Category == AggregateCategories.CreditCard)
                {
                    if (string.IsNullOrWhiteSpace(rule.CardId))
                    {
                        continue;
                    }

                    if (!cardCache.TryGetValue(rule.CardId, out var card))
                    {
                        card = await cards.GetActiveAsync(userId, rule.CardId, cancellationToken);
                        cardCache[rule.CardId] = card;
                    }

                    if (card is null)
                    {
                        continue;
                    }

                    cardId = card.Id;
                    cashDate = DueDateForPurchase(card, occurrence);
                }

                if (cashDate <= businessDate || cashDate > throughDate)
                {
                    var cashYearMonth = YearMonth.FromDate(cashDate);
                    if (cashDate > throughDate || cashYearMonth != businessMonth)
                    {
                        continue;
                    }
                }

                var yearMonth = YearMonth.FromDate(cashDate);
                if (materialized.Contains(OccurrenceKey(rule.Id, occurrence)))
                {
                    continue;
                }

                projected.Add(new TransactionProjection
                {
                    Id = $"projected_{rule.Id}_{occurrence:yyyyMMdd}",
                    DocType = "transaction",
                    UserId = userId,
                    YearMonth = yearMonth.Value,
                    Date = cashDate,
                    PurchaseDate = rule.Category == AggregateCategories.CreditCard ? occurrence : null,
                    Category = rule.Category,
                    Description = rule.Description,
                    Kind = KindFor(rule.Category),
                    Amount = rule.Amount,
                    Currency = "BRL",
                    CardId = cardId,
                    FixedRuleId = rule.Id,
                    Projected = true
                });
            }
        }

        return projected;
    }

    public static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out date);

    private static bool IsProjectableRule(FixedRuleDocument rule) =>
        string.Equals(rule.DocType, "fixedRule", StringComparison.Ordinal) &&
        UserIdRules.IsValid(rule.UserId) &&
        rule.Active &&
        rule.DeletedAt is null &&
        rule.Amount > 0 &&
        AggregateCategories.All.Contains(rule.Category);

    private static DateOnly ResolveOccurrenceDate(FixedRuleScheduleDocument schedule, int year, int month)
    {
        var day = schedule.Type switch
        {
            "calendar_day" when schedule.Day is not null =>
                Math.Min(schedule.Day.Value, DateTime.DaysInMonth(year, month)),
            "business_day" when schedule.Ordinal is not null =>
                NthBusinessDay(year, month, schedule.Ordinal.Value).Day,
            "period" when schedule.Period == "start" => 1,
            "period" when schedule.Period == "middle" => Math.Min(15, DateTime.DaysInMonth(year, month)),
            "period" when schedule.Period == "end" => DateTime.DaysInMonth(year, month),
            _ => 1
        };

        return new DateOnly(year, month, day);
    }

    private static DateOnly NthBusinessDay(int year, int month, int ordinal)
    {
        var lastBusinessDay = new DateOnly(year, month, 1);
        var count = 0;
        for (var day = 1; day <= DateTime.DaysInMonth(year, month); day++)
        {
            var candidate = new DateOnly(year, month, day);
            if (candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            count++;
            lastBusinessDay = candidate;
            if (count == ordinal)
            {
                return candidate;
            }
        }

        return lastBusinessDay;
    }

    private static DateOnly DueDateForPurchase(CardDocument card, DateOnly purchaseDate)
    {
        var closingDate = DateWithMonthFallback(purchaseDate.Year, purchaseDate.Month, card.ClosingDay);
        var invoiceMonth = purchaseDate <= closingDate
            ? new DateOnly(purchaseDate.Year, purchaseDate.Month, 1)
            : new DateOnly(purchaseDate.Year, purchaseDate.Month, 1).AddMonths(1);

        var dueMonth = card.DueDay > card.ClosingDay ? invoiceMonth : invoiceMonth.AddMonths(1);
        return DateWithMonthFallback(dueMonth.Year, dueMonth.Month, card.DueDay);
    }

    private static DateOnly DateWithMonthFallback(int year, int month, int requestedDay) =>
        new(year, month, Math.Min(requestedDay, DateTime.DaysInMonth(year, month)));

    private static string OccurrenceKey(string fixedRuleId, DateOnly occurrenceDate) =>
        $"{fixedRuleId}|{occurrenceDate:yyyy-MM-dd}";

    private static string KindFor(string category) => category switch
    {
        AggregateCategories.Income => AggregateKinds.In,
        AggregateCategories.Investment => AggregateKinds.Invest,
        _ => AggregateKinds.Out
    };
}

public sealed class AggregateCalculator(AggregatesOptions options)
{
    public MonthlyAggregateDocument Compute(
        string userId,
        YearMonth yearMonth,
        IReadOnlyList<TransactionProjection> ownerTransactions,
        decimal saldo,
        decimal investido,
        decimal saldoHoje,
        decimal investidoHoje,
        DateOnly businessDate,
        bool includesProjected,
        int projectedCount,
        string? partnerUserId,
        IReadOnlyList<TransactionProjection> partnerTransactions,
        DateTimeOffset computedAt)
    {
        ValidateTransactions(userId, yearMonth, ownerTransactions);
        if (partnerUserId is not null)
        {
            ValidateTransactions(partnerUserId, yearMonth, partnerTransactions);
        }

        var ownerTotals = CalculateOwnerTotals(ownerTransactions);
        var totals = new MonthlyTotalsDocument
        {
            Entradas = ownerTotals.Entradas,
            Saidas = ownerTotals.Saidas,
            Aportes = ownerTotals.Aportes,
            Saldo = saldo,
            Investido = investido
        };
        var dailyBalances = BuildDailyBalances(yearMonth, ownerTransactions, ownerTotals, saldo);
        var dailyMovements = BuildDailyMovements(ownerTransactions);

        var byOwner = new Dictionary<string, OwnerTotalsDocument>(StringComparer.Ordinal);
        if (ownerTransactions.Count > 0)
        {
            byOwner[userId] = ownerTotals;
        }

        if (partnerUserId is not null && partnerTransactions.Count > 0)
        {
            byOwner[partnerUserId] = CalculateOwnerTotals(partnerTransactions);
        }

        return new MonthlyAggregateDocument
        {
            Id = AggregateDocumentId.For(userId, yearMonth),
            DocType = "monthlyAggregate",
            SchemaVersion = 1,
            UserId = userId,
            Year = yearMonth.Year,
            MonthIdx = yearMonth.MonthIdx,
            YearMonth = yearMonth.Value,
            Totals = totals,
            SnapshotToday = BuildSnapshotToday(yearMonth, saldoHoje, investidoHoje, businessDate),
            DailyBalances = dailyBalances,
            DailyMovements = dailyMovements,
            Projection = new ProjectionDocument
            {
                IncludesProjected = includesProjected,
                ProjectedCount = projectedCount,
                AsOfDate = businessDate
            },
            ByCategory = ownerTransactions
                .GroupBy(x => x.Category, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Sum(t => t.Amount), StringComparer.Ordinal),
            ByCard = ownerTransactions
                .Where(x => x.Category == AggregateCategories.CreditCard && !string.IsNullOrWhiteSpace(x.CardId))
                .GroupBy(x => x.CardId!, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Sum(t => t.Amount), StringComparer.Ordinal),
            ByOwner = byOwner,
            TransactionsCount = ownerTransactions.Count(x => !x.Projected),
            ComputedAt = computedAt,
            SourceVersion = options.SourceVersion
        };
    }

    private static List<DailyBalanceDocument> BuildDailyBalances(
        YearMonth yearMonth,
        IReadOnlyList<TransactionProjection> ownerTransactions,
        OwnerTotalsDocument ownerTotals,
        decimal monthEndSaldo)
    {
        var monthDelta = ownerTotals.Entradas - ownerTotals.Saidas - ownerTotals.Aportes;
        var runningSaldo = monthEndSaldo - monthDelta;
        var deltaByDay = ownerTransactions
            .GroupBy(x => x.Date.Day)
            .ToDictionary(
                x => x.Key,
                x => x.Sum(DailySaldoDelta),
                EqualityComparer<int>.Default);

        var balances = new List<DailyBalanceDocument>(yearMonth.LastDay.Day);
        for (var day = 1; day <= yearMonth.LastDay.Day; day++)
        {
            runningSaldo += deltaByDay.GetValueOrDefault(day);
            balances.Add(new DailyBalanceDocument
            {
                Day = day,
                Saldo = runningSaldo
            });
        }

        return balances;
    }

    private static List<DailyMovementDocument> BuildDailyMovements(
        IReadOnlyList<TransactionProjection> ownerTransactions) =>
        ownerTransactions
            .OrderBy(x => x.Date.Day)
            .ThenBy(x => x.Projected)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => new DailyMovementDocument
            {
                Day = x.Date.Day,
                Id = x.Id,
                UserId = x.UserId,
                Category = x.Category,
                Kind = x.Kind,
                Description = x.Description,
                Amount = x.Amount,
                CardId = x.CardId,
                FixedRuleId = x.FixedRuleId,
                Projected = x.Projected,
                PurchaseDate = x.PurchaseDate
            })
            .ToList();

    private static decimal DailySaldoDelta(TransactionProjection projection) => projection.Kind switch
    {
        AggregateKinds.In => projection.Amount,
        AggregateKinds.Invest => -projection.Amount,
        _ => -projection.Amount
    };

    private static SnapshotTodayDocument? BuildSnapshotToday(
        YearMonth yearMonth,
        decimal saldoHoje,
        decimal investidoHoje,
        DateOnly businessDate)
    {
        if (yearMonth != YearMonth.FromDate(businessDate))
        {
            return null;
        }

        return new SnapshotTodayDocument
        {
            SaldoHoje = saldoHoje,
            InvestidoHoje = investidoHoje,
            PatrimonioHoje = saldoHoje + investidoHoje,
            AsOfDate = businessDate
        };
    }

    public static OwnerTotalsDocument CalculateTotals(IEnumerable<TransactionProjection> projections) =>
        CalculateOwnerTotals(projections);

    private static OwnerTotalsDocument CalculateOwnerTotals(IEnumerable<TransactionProjection> projections)
    {
        var totals = new OwnerTotalsDocument();
        foreach (var projection in projections)
        {
            switch (projection.Kind)
            {
                case AggregateKinds.In:
                    totals.Entradas += projection.Amount;
                    break;
                case AggregateKinds.Out:
                    totals.Saidas += projection.Amount;
                    break;
                case AggregateKinds.Invest:
                    totals.Aportes += projection.Amount;
                    break;
            }
        }

        return totals;
    }

    private static void ValidateTransactions(string userId, YearMonth yearMonth, IEnumerable<TransactionProjection> projections)
    {
        foreach (var projection in projections)
        {
            if (!string.Equals(projection.DocType, "transaction", StringComparison.Ordinal) ||
                !string.Equals(projection.UserId, userId, StringComparison.Ordinal) ||
                !string.Equals(projection.YearMonth, yearMonth.Value, StringComparison.Ordinal) ||
                projection.DeletedAt is not null ||
                projection.Amount < 0 ||
                !AggregateCategories.All.Contains(projection.Category) ||
                !AggregateKinds.All.Contains(projection.Kind))
            {
                throw new InvalidTransactionProjectionException("Invalid transaction projection.");
            }

            if (projection.Category == AggregateCategories.CreditCard && string.IsNullOrWhiteSpace(projection.CardId))
            {
                throw new InvalidTransactionProjectionException("Credit card transaction without cardId.");
            }
        }
    }
}

public static class CarryForwardFactory
{
    public static async Task<MonthlyAggregateResponse> MonthAsync(
        string userId,
        YearMonth yearMonth,
        IMonthlyAggregatesRepository aggregates,
        IUsersReadRepository users,
        int sourceVersion,
        CancellationToken cancellationToken)
    {
        var previous = await aggregates.GetLatestBeforeAsync(userId, yearMonth, cancellationToken);
        var saldo = previous?.Totals.Saldo ?? await users.GetStartingBalanceAsync(userId, cancellationToken);
        var investido = previous?.Totals.Investido ?? 0m;
        return Month(
            userId,
            yearMonth,
            saldo,
            investido,
            sourceVersion,
            previous?.Projection.IncludesProjected ?? false,
            previous?.Projection.ProjectedCount ?? 0,
            previous?.Projection.AsOfDate);
    }

    public static MonthlyAggregateResponse Month(
        string userId,
        YearMonth yearMonth,
        decimal saldo,
        decimal investido,
        int sourceVersion,
        bool includesProjected = false,
        int projectedCount = 0,
        DateOnly? projectionAsOfDate = null) =>
        new(
            AggregateDocumentId.For(userId, yearMonth),
            userId,
            yearMonth.Year,
            yearMonth.Month,
            yearMonth.MonthIdx,
            yearMonth.Value,
            new MonthlyTotalsResponse(0, 0, 0, saldo, investido),
            null,
            Enumerable.Range(1, yearMonth.LastDay.Day)
                .Select(day => new DailyBalanceResponse(day, saldo))
                .ToArray(),
            Array.Empty<DailyMovementResponse>(),
            new ProjectionResponse(includesProjected, projectedCount, projectionAsOfDate ?? yearMonth.LastDay),
            new Dictionary<string, decimal>(),
            new Dictionary<string, decimal>(),
            new Dictionary<string, OwnerTotalsResponse>(),
            0,
            null,
            sourceVersion,
            true,
            "carried",
            new FreshnessResponse("stale", "carried"),
            new SourceWatermarkResponse(null, 0));
}

public static class AggregateMapping
{
    public static MonthlyAggregateResponse ToResponse(
        MonthlyAggregateDocument document,
        string source,
        bool isStale,
        string? staleReason = null)
    {
        var sourceWatermark = document.SourceWatermark ?? new SourceWatermarkDocument();
        return new(
            document.Id,
            document.UserId,
            document.Year,
            document.MonthIdx + 1,
            document.MonthIdx,
            document.YearMonth,
            new MonthlyTotalsResponse(
                document.Totals.Entradas,
                document.Totals.Saidas,
                document.Totals.Aportes,
                document.Totals.Saldo,
                document.Totals.Investido),
            document.SnapshotToday is null
                ? null
                : new SnapshotTodayResponse(
                    document.SnapshotToday.SaldoHoje,
                    document.SnapshotToday.InvestidoHoje,
                    document.SnapshotToday.PatrimonioHoje,
                    document.SnapshotToday.AsOfDate),
            document.DailyBalances
                .OrderBy(x => x.Day)
                .Select(x => new DailyBalanceResponse(x.Day, x.Saldo))
                .ToArray(),
            document.DailyMovements
                .OrderBy(x => x.Day)
                .ThenBy(x => x.Projected)
                .ThenBy(x => x.Id, StringComparer.Ordinal)
                .Select(x => new DailyMovementResponse(
                    x.Day,
                    x.Id,
                    x.UserId,
                    x.Category,
                    x.Kind,
                    x.Description,
                    x.Amount,
                    x.CardId,
                    x.FixedRuleId,
                    x.Projected,
                    x.PurchaseDate))
                .ToArray(),
            new ProjectionResponse(
                document.Projection.IncludesProjected,
                document.Projection.ProjectedCount,
                document.Projection.AsOfDate),
            document.ByCategory,
            document.ByCard,
            document.ByOwner.ToDictionary(
                x => x.Key,
                x => new OwnerTotalsResponse(x.Value.Entradas, x.Value.Saidas, x.Value.Aportes),
                StringComparer.Ordinal),
            document.TransactionsCount,
            document.ComputedAt,
            document.SourceVersion,
            isStale,
            source,
            new FreshnessResponse(isStale ? "stale" : "fresh", isStale ? staleReason ?? "stale" : null),
            new SourceWatermarkResponse(
                sourceWatermark.MaxTransactionUpdatedAt,
                sourceWatermark.ActiveTransactionsCount));
    }
}

public static class BusinessClock
{
    public static DateOnly Today(TimeProvider clock, string timeZoneId)
    {
        var timeZone = ResolveTimeZone(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException) when (timeZoneId == "America/Sao_Paulo")
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
        catch (InvalidTimeZoneException) when (timeZoneId == "America/Sao_Paulo")
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }
}
