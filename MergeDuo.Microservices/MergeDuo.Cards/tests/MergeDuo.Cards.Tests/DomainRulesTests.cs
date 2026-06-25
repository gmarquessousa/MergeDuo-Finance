using MergeDuo.Cards.Domain.Exceptions;
using MergeDuo.Cards.Domain.Rules;

namespace MergeDuo.Cards.Tests;

public sealed class DomainRulesTests
{
    [Fact]
    public void Validates_title_day_currency_and_year_month()
    {
        Assert.Equal("Nubank Roxinho", TitleRules.Normalize("  Nubank Roxinho  "));
        Assert.Equal(31, BillingDayRules.EnsureValid(31));
        Assert.Equal("BRL", CurrencyCodeRules.Normalize("brl"));
        Assert.Equal("2026-05", YearMonthRules.EnsureValid("2026-05"));

        Assert.Throws<CardsBadRequestException>(() => TitleRules.Normalize(""));
        Assert.Throws<CardsBadRequestException>(() => BillingDayRules.EnsureValid(32));
        Assert.Throws<CardsBadRequestException>(() => CurrencyCodeRules.Normalize("USD"));
        Assert.Throws<CardsBadRequestException>(() => YearMonthRules.EnsureValid("2026-13"));
    }

    [Fact]
    public void Calculates_nubank_cycle_like_front_rule()
    {
        var cycle = BillingCycleRules.Calculate(closingDay: 28, dueDay: 5, yearMonth: "2026-05");

        Assert.Equal(new DateOnly(2026, 4, 28), cycle.ClosingDate);
        Assert.Equal(new DateOnly(2026, 5, 5), cycle.DueDate);
        Assert.Equal(new DateOnly(2026, 3, 29), cycle.CycleStart);
        Assert.Equal(cycle.ClosingDate, cycle.CycleEnd);
    }

    [Fact]
    public void Calculates_itau_cycle_with_previous_month_closing()
    {
        var cycle = BillingCycleRules.Calculate(closingDay: 15, dueDay: 22, yearMonth: "2026-06");

        Assert.Equal(new DateOnly(2026, 5, 15), cycle.ClosingDate);
        Assert.Equal(new DateOnly(2026, 6, 22), cycle.DueDate);
    }

    [Fact]
    public void Uses_last_day_when_month_is_short()
    {
        var cycle = BillingCycleRules.Calculate(closingDay: 31, dueDay: 31, yearMonth: "2024-03");

        Assert.Equal(new DateOnly(2024, 2, 29), cycle.ClosingDate);
        Assert.Equal(new DateOnly(2024, 3, 31), cycle.DueDate);
    }

    [Fact]
    public void Generates_valid_card_id()
    {
        var generator = new UlidCardIdGenerator(TimeProvider.System);
        var id = generator.NewId();

        Assert.StartsWith("card_", id);
        Assert.True(CardIdRules.IsValid(id));
    }
}
