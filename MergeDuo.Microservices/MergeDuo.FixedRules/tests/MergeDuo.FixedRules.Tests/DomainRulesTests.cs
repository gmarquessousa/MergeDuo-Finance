using MergeDuo.FixedRules.Domain.Abstractions;
using MergeDuo.FixedRules.Domain.Documents;
using MergeDuo.FixedRules.Domain.Exceptions;
using MergeDuo.FixedRules.Domain.Options;
using MergeDuo.FixedRules.Domain.Services;

namespace MergeDuo.FixedRules.Tests;

public sealed class DomainRulesTests
{
    [Fact]
    public void Preview_uses_month_end_for_short_calendar_months()
    {
        var service = new FixedRulePreviewService(new WeekendOnlyBusinessCalendar(), new PreviewOptions { MaxMonths = 24 });
        var rule = Rule(schedule: new FixedRuleScheduleDocument { Type = "calendar_day", Day = 31 });

        var response = service.Preview(rule, "2026-02-01", "2026-02-28");

        Assert.Single(response.Items);
        Assert.Equal("2026-02-28", response.Items[0].OccurrenceDate);
        var warning = Assert.Single(response.Items[0].Warnings!);
        Assert.Equal("calendar_day_adjusted", warning.Code);
    }

    [Fact]
    public void Preview_returns_warning_when_range_has_no_occurrences()
    {
        var service = new FixedRulePreviewService(new WeekendOnlyBusinessCalendar(), new PreviewOptions { MaxMonths = 24 });
        var rule = Rule(schedule: new FixedRuleScheduleDocument { Type = "calendar_day", Day = 5 });
        rule.StartsAt = "2026-03-01";

        var response = service.Preview(rule, "2026-01-01", "2026-01-31");

        Assert.Empty(response.Items);
        var warning = Assert.Single(response.Warnings!);
        Assert.Equal("no_occurrences_in_range", warning.Code);
    }

    [Fact]
    public void Preview_uses_last_business_day_when_ordinal_exceeds_month()
    {
        var service = new FixedRulePreviewService(new WeekendOnlyBusinessCalendar(), new PreviewOptions { MaxMonths = 24 });
        var rule = Rule(schedule: new FixedRuleScheduleDocument { Type = "business_day", Ordinal = 23 });

        var response = service.Preview(rule, "2026-02-01", "2026-02-28");

        Assert.Single(response.Items);
        Assert.Equal("2026-02-27", response.Items[0].OccurrenceDate);
    }

    [Fact]
    public void Preview_rejects_interval_above_configured_limit()
    {
        var service = new FixedRulePreviewService(new WeekendOnlyBusinessCalendar(), new PreviewOptions { MaxMonths = 1 });
        var rule = Rule();

        var ex = Assert.Throws<FixedRulesBadRequestException>(() => service.Preview(rule, "2026-01-01", "2026-02-01"));

        Assert.Equal("invalid_date_range", ex.Code);
    }

    [Fact]
    public void Preview_respects_ends_at_inclusively()
    {
        var service = new FixedRulePreviewService(new WeekendOnlyBusinessCalendar(), new PreviewOptions { MaxMonths = 24 });
        var rule = Rule(schedule: new FixedRuleScheduleDocument { Type = "calendar_day", Day = 15 });
        rule.EndsAt = "2026-02-15";

        var response = service.Preview(rule, "2026-01-01", "2026-03-31");

        Assert.Equal(["2026-01-15", "2026-02-15"], response.Items.Select(x => x.OccurrenceDate));
    }

    [Fact]
    public void Weekend_calendar_identifies_business_days()
    {
        IBusinessCalendar calendar = new WeekendOnlyBusinessCalendar();

        Assert.True(calendar.IsBusinessDay(new DateOnly(2026, 4, 27)));
        Assert.False(calendar.IsBusinessDay(new DateOnly(2026, 4, 26)));
    }

    private static FixedRuleDocument Rule(FixedRuleScheduleDocument? schedule = null) =>
        new()
        {
            Id = "fxr_salary",
            DocType = "fixedRule",
            SchemaVersion = 1,
            UserId = "usr_gmarques",
            Category = "income",
            Description = "Salario",
            Amount = 8500m,
            CardId = null,
            Tags = [],
            Schedule = schedule ?? new FixedRuleScheduleDocument { Type = "calendar_day", Day = 5 },
            StartsAt = "2026-01-01",
            EndsAt = null,
            Active = true,
            CreatedAt = DateTimeOffset.Parse("2026-01-01T12:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-01-01T12:00:00Z")
        };
}
