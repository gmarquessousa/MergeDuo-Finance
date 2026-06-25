using MergeDuo.Aggregates.Domain.Documents;
using MergeDuo.Aggregates.Domain.Exceptions;
using MergeDuo.Aggregates.Domain.Options;
using MergeDuo.Aggregates.Domain.Rules;
using MergeDuo.Aggregates.Domain.Services;

namespace MergeDuo.Aggregates.Tests;

public sealed class DomainRulesTests
{
    [Fact]
    public void YearMonth_validates_route_and_generates_document_id()
    {
        var yearMonth = YearMonth.FromRoute(2026, 4);

        Assert.Equal(3, yearMonth.MonthIdx);
        Assert.Equal("2026-04", yearMonth.Value);
        Assert.Equal("agg_usr_gmarques_2026-04", AggregateDocumentId.For("usr_gmarques", yearMonth));
        Assert.Throws<AggregatesBadRequestException>(() => YearMonth.FromRoute(1999, 4));
        Assert.Throws<AggregatesBadRequestException>(() => YearMonth.FromRoute(2026, 13));
    }

    [Fact]
    public void Aggregate_calculator_computes_totals_breakdowns_and_sao_paulo_snapshot()
    {
        var calculator = new AggregateCalculator(new AggregatesOptions { SourceVersion = 1 });
        var yearMonth = new YearMonth(2026, 4);

        var aggregate = calculator.Compute(
            "usr_gmarques",
            yearMonth,
            [
                Tx("tx_income", "usr_gmarques", "2026-04", "2026-04-05", "income", "in", 1000),
                Tx("tx_card", "usr_gmarques", "2026-04", "2026-04-20", "credit_card", "out", 200, "card_nubank"),
                Tx("tx_invest", "usr_gmarques", "2026-04", "2026-04-25", "investment", "invest", 300)
            ],
            saldo: 1500,
            investido: 1300,
            saldoHoje: 1500,
            investidoHoje: 1300,
            businessDate: new DateOnly(2026, 4, 25),
            includesProjected: false,
            projectedCount: 0,
            partnerUserId: "usr_bmarques",
            partnerTransactions:
            [
                Tx("tx_partner", "usr_bmarques", "2026-04", "2026-04-15", "variable_expense", "out", 50)
            ],
            computedAt: DateTimeOffset.Parse("2026-04-25T18:00:00Z"));

        Assert.Equal(1000, aggregate.Totals.Entradas);
        Assert.Equal(200, aggregate.Totals.Saidas);
        Assert.Equal(300, aggregate.Totals.Aportes);
        Assert.Equal(1500, aggregate.Totals.Saldo);
        Assert.Equal(1300, aggregate.Totals.Investido);
        Assert.Equal(3, aggregate.TransactionsCount);
        Assert.Equal(200, aggregate.ByCategory["credit_card"]);
        Assert.Equal(200, aggregate.ByCard["card_nubank"]);
        Assert.Equal(300, aggregate.ByOwner["usr_gmarques"].Aportes);
        Assert.Equal(50, aggregate.ByOwner["usr_bmarques"].Saidas);
        Assert.NotNull(aggregate.SnapshotToday);
        Assert.Equal(new DateOnly(2026, 4, 25), aggregate.SnapshotToday!.AsOfDate);
        Assert.Equal(2800, aggregate.SnapshotToday.PatrimonioHoje);
    }

    [Fact]
    public void Aggregate_calculator_rejects_invalid_projection()
    {
        var calculator = new AggregateCalculator(new AggregatesOptions { SourceVersion = 1 });

        Assert.Throws<InvalidTransactionProjectionException>(() => calculator.Compute(
            "usr_gmarques",
            new YearMonth(2026, 4),
            [Tx("tx_bad", "usr_gmarques", "2026-04", "2026-04-20", "credit_card", "out", 200)],
            0,
            0,
            0,
            0,
            new DateOnly(2026, 4, 25),
            false,
            0,
            null,
            [],
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Carry_forward_returns_synthetic_stale_month()
    {
        var response = CarryForwardFactory.Month("usr_gmarques", new YearMonth(2026, 1), 1200, 300, 2);

        Assert.Equal("carried", response.Source);
        Assert.True(response.IsStale);
        Assert.Null(response.ComputedAt);
        Assert.Empty(response.ByOwner);
        Assert.Equal(1200, response.Totals.Saldo);
        Assert.Equal(300, response.Totals.Investido);
    }

    [Fact]
    public void Carry_forward_preserves_projection_metadata_when_balance_is_projected()
    {
        var response = CarryForwardFactory.Month(
            "usr_gmarques",
            new YearMonth(2026, 6),
            1200,
            300,
            2,
            includesProjected: true,
            projectedCount: 1,
            projectionAsOfDate: new DateOnly(2026, 4, 25));

        Assert.True(response.Projection.IncludesProjected);
        Assert.Equal(1, response.Projection.ProjectedCount);
        Assert.Equal(new DateOnly(2026, 4, 25), response.Projection.AsOfDate);
    }

    private static TransactionProjection Tx(
        string id,
        string userId,
        string yearMonth,
        string date,
        string category,
        string kind,
        decimal amount,
        string? cardId = null) =>
        new()
        {
            Id = id,
            DocType = "transaction",
            UserId = userId,
            YearMonth = yearMonth,
            Date = DateOnly.Parse(date),
            Category = category,
            Kind = kind,
            Amount = amount,
            Currency = "BRL",
            CardId = cardId
        };
}
