using System.Text.Json;
using MergeDuo.FixedRules.Domain.Abstractions;
using MergeDuo.FixedRules.Domain.Contracts;
using MergeDuo.FixedRules.Domain.Documents;
using MergeDuo.FixedRules.Domain.Exceptions;
using MergeDuo.FixedRules.Domain.Options;
using MergeDuo.FixedRules.Domain.Rules;

namespace MergeDuo.FixedRules.Domain.Services;

public interface IFixedRulesService
{
    Task<FixedRulesListResponse> ListAsync(
        string userId,
        string? category,
        string? active,
        CancellationToken cancellationToken);

    Task<FixedRuleResponse> GetAsync(string userId, string fixedRuleId, CancellationToken cancellationToken);

    Task<FixedRuleResponse> CreateAsync(
        string userId,
        CreateFixedRuleRequest request,
        CancellationToken cancellationToken);

    Task<FixedRuleResponse> PatchAsync(
        string userId,
        string fixedRuleId,
        UpdateFixedRuleRequest request,
        string? ifMatchEtag,
        CancellationToken cancellationToken);

    Task<FixedRuleResponse> PauseAsync(
        string userId,
        string fixedRuleId,
        string? ifMatchEtag,
        CancellationToken cancellationToken);

    Task<FixedRuleResponse> ResumeAsync(
        string userId,
        string fixedRuleId,
        string? ifMatchEtag,
        CancellationToken cancellationToken);

    Task SoftDeleteAsync(string userId, string fixedRuleId, string? ifMatchEtag, CancellationToken cancellationToken);

    Task<FixedRulePreviewResponse> PreviewAsync(
        string userId,
        string fixedRuleId,
        string? from,
        string? to,
        CancellationToken cancellationToken);
}

public sealed class FixedRulesService(
    IFixedRulesRepository fixedRules,
    ICardsReadRepository cards,
    IFixedRuleIdGenerator idGenerator,
    IFixedRulePreviewService previewService,
    TimeProvider clock) : IFixedRulesService
{
    public async Task<FixedRulesListResponse> ListAsync(
        string userId,
        string? category,
        string? active,
        CancellationToken cancellationToken)
    {
        EnsureUserId(userId);
        var filter = new FixedRuleListFilter(
            string.IsNullOrWhiteSpace(category) ? null : CategoryRules.Normalize(category),
            ParseActiveFilter(active));

        var documents = await fixedRules.ListAsync(userId, filter, cancellationToken);
        return new FixedRulesListResponse(documents.Select(rule => FixedRuleMapping.ToResponse(rule)).ToArray());
    }

    public async Task<FixedRuleResponse> GetAsync(string userId, string fixedRuleId, CancellationToken cancellationToken)
    {
        var rule = await GetExistingRuleAsync(userId, fixedRuleId, cancellationToken);
        return FixedRuleMapping.ToResponse(rule);
    }

    public async Task<FixedRuleResponse> CreateAsync(
        string userId,
        CreateFixedRuleRequest request,
        CancellationToken cancellationToken)
    {
        EnsureUserId(userId);
        RequestRules.EnsureNoExtraFields(request.ExtraFields);

        var category = CategoryRules.Normalize(request.Category);
        var description = DescriptionRules.Normalize(request.Description);
        var amount = AmountRules.EnsureValid(request.Amount);
        var startsAt = DateRules.EnsureDate(request.StartsAt);
        var endsAt = DateRules.EnsureOptionalDate(request.EndsAt);
        DateRules.EnsureRange(startsAt, endsAt);
        var schedule = ScheduleRules.Normalize(request.Schedule);
        var cardId = await NormalizeCardIdAsync(userId, category, request.CardId, cancellationToken);
        var tags = TagRules.Normalize(request.Tags);
        var now = clock.GetUtcNow();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var rule = new FixedRuleDocument
            {
                Id = idGenerator.NewId(),
                DocType = "fixedRule",
                SchemaVersion = 1,
                UserId = userId,
                Category = category,
                Description = description,
                Amount = amount,
                CardId = cardId,
                Tags = tags,
                Schedule = schedule,
                StartsAt = startsAt,
                EndsAt = endsAt,
                Active = request.Active ?? true,
                CreatedAt = now,
                UpdatedAt = now,
                DeletedAt = null
            };

            try
            {
                await fixedRules.CreateAsync(rule, cancellationToken);
                return FixedRuleMapping.ToResponse(rule, BuildRuleWarnings(rule, Today()));
            }
            catch (FixedRulesConflictException) when (attempt < 2)
            {
            }
        }

        throw new FixedRulesConflictException("fixed_rule_conflict", "Fixed rule id conflict.");
    }

    public async Task<FixedRuleResponse> PatchAsync(
        string userId,
        string fixedRuleId,
        UpdateFixedRuleRequest request,
        string? ifMatchEtag,
        CancellationToken cancellationToken)
    {
        RequestRules.EnsureNoExtraFields(request.ExtraFields);
        if (!request.HasAnyEditableField)
        {
            throw new FixedRulesBadRequestException("empty_patch", "Patch must include at least one editable field.");
        }

        var rule = await GetExistingRuleAsync(userId, fixedRuleId, cancellationToken);
        var updated = Clone(rule);

        var patch = new FixedRulePatch { UpdatedAt = clock.GetUtcNow() };
        var patchBuilder = new FixedRulePatchBuilder(patch);

        if (HasPatchField(request.Category))
        {
            updated.Category = CategoryRules.Normalize(RequestRules.ReadNullableString(request.Category, "invalid_category"));
            patchBuilder.Category(updated.Category);
        }

        if (HasPatchField(request.Description))
        {
            updated.Description = DescriptionRules.Normalize(RequestRules.ReadNullableString(request.Description, "invalid_description"));
            patchBuilder.Description(updated.Description);
        }

        if (HasPatchField(request.Amount))
        {
            updated.Amount = AmountRules.EnsureValid(RequestRules.ReadDecimal(request.Amount));
            patchBuilder.Amount(updated.Amount);
        }

        if (HasPatchField(request.Schedule))
        {
            updated.Schedule = ScheduleRules.Normalize(request.Schedule);
            patchBuilder.Schedule(updated.Schedule);
        }

        if (HasPatchField(request.StartsAt))
        {
            updated.StartsAt = DateRules.EnsureDate(RequestRules.ReadNullableString(request.StartsAt, "invalid_date_range"));
            patchBuilder.StartsAt(updated.StartsAt);
        }

        if (HasPatchField(request.EndsAt))
        {
            updated.EndsAt = DateRules.EnsureOptionalDate(RequestRules.ReadNullableString(request.EndsAt, "invalid_date_range"));
            patchBuilder.EndsAt(updated.EndsAt);
        }

        if (HasPatchField(request.Active))
        {
            updated.Active = RequestRules.ReadBoolean(request.Active);
            patchBuilder.Active(updated.Active);
        }

        if (HasPatchField(request.CardId))
        {
            var requestedCardId = RequestRules.ReadNullableString(request.CardId, "invalid_card_id");
            if (updated.Category != "credit_card" && !string.IsNullOrWhiteSpace(requestedCardId))
            {
                throw new FixedRulesBadRequestException("invalid_card_id", "Invalid card id.");
            }

            updated.CardId = requestedCardId;
            patchBuilder.CardId(updated.CardId);
        }

        if (HasPatchField(request.Tags))
        {
            updated.Tags = TagRules.Normalize(RequestRules.ReadStringArray(request.Tags, "invalid_tags"));
            patchBuilder.Tags(updated.Tags);
        }

        if (updated.Category != "credit_card" && updated.CardId is not null)
        {
            updated.CardId = null;
            patchBuilder.CardId(null);
        }

        DateRules.EnsureRange(updated.StartsAt, updated.EndsAt);
        updated.CardId = await NormalizeCardIdAsync(userId, updated.Category, updated.CardId, cancellationToken);
        if (updated.CardId != rule.CardId || patchBuilder.HasExplicitCardId)
        {
            patchBuilder.CardId(updated.CardId);
        }

        var selectedPatch = patchBuilder.Build();
        var etag = SelectEtag(rule, ifMatchEtag);
        var stored = await fixedRules.PatchAsync(
            rule,
            selectedPatch,
            etag,
            !string.IsNullOrWhiteSpace(ifMatchEtag),
            cancellationToken);
        return FixedRuleMapping.ToResponse(stored, BuildRuleWarnings(stored, Today()));
    }

    public async Task<FixedRuleResponse> PauseAsync(
        string userId,
        string fixedRuleId,
        string? ifMatchEtag,
        CancellationToken cancellationToken)
    {
        var rule = await GetExistingRuleAsync(userId, fixedRuleId, cancellationToken);
        var patch = new FixedRulePatch
        {
            Active = false,
            HasActive = true,
            UpdatedAt = clock.GetUtcNow()
        };
        var stored = await fixedRules.PatchAsync(
            rule,
            patch,
            SelectEtag(rule, ifMatchEtag),
            !string.IsNullOrWhiteSpace(ifMatchEtag),
            cancellationToken);
        return FixedRuleMapping.ToResponse(stored);
    }

    public async Task<FixedRuleResponse> ResumeAsync(
        string userId,
        string fixedRuleId,
        string? ifMatchEtag,
        CancellationToken cancellationToken)
    {
        var rule = await GetExistingRuleAsync(userId, fixedRuleId, cancellationToken);
        if (rule.EndsAt is not null && DateRules.Parse(rule.EndsAt) < DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
        {
            throw new FixedRulesConflictException("fixed_rule_expired", "Fixed rule expired.");
        }

        var patch = new FixedRulePatch
        {
            Active = true,
            HasActive = true,
            UpdatedAt = clock.GetUtcNow()
        };
        var stored = await fixedRules.PatchAsync(
            rule,
            patch,
            SelectEtag(rule, ifMatchEtag),
            !string.IsNullOrWhiteSpace(ifMatchEtag),
            cancellationToken);
        return FixedRuleMapping.ToResponse(stored);
    }

    public async Task SoftDeleteAsync(
        string userId,
        string fixedRuleId,
        string? ifMatchEtag,
        CancellationToken cancellationToken)
    {
        var rule = await GetExistingRuleAsync(userId, fixedRuleId, cancellationToken);
        await fixedRules.SoftDeleteAsync(
            rule,
            clock.GetUtcNow(),
            SelectEtag(rule, ifMatchEtag),
            !string.IsNullOrWhiteSpace(ifMatchEtag),
            cancellationToken);
    }

    public async Task<FixedRulePreviewResponse> PreviewAsync(
        string userId,
        string fixedRuleId,
        string? from,
        string? to,
        CancellationToken cancellationToken)
    {
        var rule = await GetExistingRuleAsync(userId, fixedRuleId, cancellationToken);
        return previewService.Preview(rule, from, to);
    }

    private async Task<FixedRuleDocument> GetExistingRuleAsync(
        string userId,
        string fixedRuleId,
        CancellationToken cancellationToken)
    {
        EnsureUserId(userId);
        FixedRuleIdRules.EnsureValid(fixedRuleId);
        var rule = await fixedRules.GetByIdAsync(userId, fixedRuleId, includeDeleted: false, cancellationToken);
        return rule ?? throw new FixedRulesNotFoundException("fixed_rule_not_found", "Fixed rule not found.");
    }

    private async Task<string?> NormalizeCardIdAsync(
        string userId,
        string category,
        string? cardId,
        CancellationToken cancellationToken)
    {
        if (category != "credit_card")
        {
            if (!string.IsNullOrWhiteSpace(cardId))
            {
                throw new FixedRulesBadRequestException("invalid_card_id", "Invalid card id.");
            }

            return null;
        }

        if (!CardIdRules.IsValid(cardId))
        {
            throw new FixedRulesBadRequestException("invalid_card_id", "Invalid card id.");
        }

        var card = await cards.GetActiveCardAsync(userId, cardId!, cancellationToken);
        if (card is null)
        {
            throw new FixedRulesNotFoundException("card_not_found", "Card not found.");
        }

        return card.Id;
    }

    private static FixedRuleActiveFilter ParseActiveFilter(string? active)
    {
        if (string.IsNullOrWhiteSpace(active) || string.Equals(active, "true", StringComparison.OrdinalIgnoreCase))
        {
            return FixedRuleActiveFilter.Active;
        }

        if (string.Equals(active, "false", StringComparison.OrdinalIgnoreCase))
        {
            return FixedRuleActiveFilter.Inactive;
        }

        if (string.Equals(active, "all", StringComparison.OrdinalIgnoreCase))
        {
            return FixedRuleActiveFilter.All;
        }

        throw new FixedRulesBadRequestException("invalid_active_filter", "Invalid active filter.");
    }

    private static void EnsureUserId(string userId)
    {
        if (!UserIdRules.IsValid(userId))
        {
            throw new FixedRulesBadRequestException("unauthorized", "JWT is missing, invalid or expired.");
        }
    }

    private static string SelectEtag(FixedRuleDocument rule, string? ifMatchEtag)
    {
        if (string.IsNullOrWhiteSpace(ifMatchEtag))
        {
            throw new FixedRulesPreconditionFailedException("if_match_required", "If-Match header is required for fixed rule updates.");
        }

        return ifMatchEtag.Trim();
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private static IReadOnlyList<FixedRuleWarningResponse>? BuildRuleWarnings(FixedRuleDocument rule, DateOnly today)
    {
        var warnings = new List<FixedRuleWarningResponse>();
        if (!rule.Active)
        {
            warnings.Add(new FixedRuleWarningResponse(
                "fixed_rule_inactive",
                "Rule is paused and will not materialize until resumed.",
                "info"));
        }

        if (rule.EndsAt is not null && DateRules.Parse(rule.EndsAt) < today)
        {
            warnings.Add(new FixedRuleWarningResponse(
                "fixed_rule_expired",
                "Rule end date is in the past.",
                "warning"));
        }

        return warnings.Count == 0 ? null : warnings;
    }

    private static bool HasPatchField(JsonElement element) => element.ValueKind != JsonValueKind.Undefined;

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
            LastRunAt = rule.LastRunAt,
            NextRunAt = rule.NextRunAt,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt,
            DeletedAt = rule.DeletedAt,
            ETag = rule.ETag
        };

    private sealed class FixedRulePatchBuilder(FixedRulePatch initial)
    {
        private FixedRulePatch _patch = initial;

        public bool HasExplicitCardId { get; private set; }

        public void Category(string value) => _patch = _patch with { Category = value, HasCategory = true };
        public void Description(string value) => _patch = _patch with { Description = value, HasDescription = true };
        public void Amount(decimal value) => _patch = _patch with { Amount = value, HasAmount = true };
        public void CardId(string? value)
        {
            HasExplicitCardId = true;
            _patch = _patch with { CardId = value, HasCardId = true };
        }

        public void Tags(string[] value) => _patch = _patch with { Tags = value, HasTags = true };
        public void Schedule(FixedRuleScheduleDocument value) => _patch = _patch with { Schedule = value, HasSchedule = true };
        public void StartsAt(string value) => _patch = _patch with { StartsAt = value, HasStartsAt = true };
        public void EndsAt(string? value) => _patch = _patch with { EndsAt = value, HasEndsAt = true };
        public void Active(bool value) => _patch = _patch with { Active = value, HasActive = true };
        public FixedRulePatch Build() => _patch;
    }
}

public interface IFixedRulePreviewService
{
    FixedRulePreviewResponse Preview(FixedRuleDocument rule, string? from, string? to);
}

public sealed class FixedRulePreviewService(IBusinessCalendar calendar, PreviewOptions options) : IFixedRulePreviewService
{
    public FixedRulePreviewResponse Preview(FixedRuleDocument rule, string? from, string? to)
    {
        var fromDate = DateRules.Parse(DateRules.EnsureDate(from));
        var toDate = DateRules.Parse(DateRules.EnsureDate(to));
        if (fromDate > toDate)
        {
            throw new FixedRulesBadRequestException("invalid_date_range", "Invalid date range.");
        }

        var monthCount = ((toDate.Year - fromDate.Year) * 12) + toDate.Month - fromDate.Month + 1;
        if (monthCount > Math.Max(1, options.MaxMonths))
        {
            throw new FixedRulesBadRequestException("invalid_date_range", "Invalid date range.");
        }

        var startsAt = DateRules.Parse(rule.StartsAt);
        var endsAt = rule.EndsAt is null ? (DateOnly?)null : DateRules.Parse(rule.EndsAt);
        var items = new List<FixedRuleOccurrenceResponse>();
        var previewWarnings = new List<FixedRuleWarningResponse>();
        if (!rule.Active)
        {
            previewWarnings.Add(new FixedRuleWarningResponse(
                "fixed_rule_inactive",
                "Rule is paused; preview is informational only.",
                "info"));
        }

        var cursor = new DateOnly(fromDate.Year, fromDate.Month, 1);
        var endMonth = new DateOnly(toDate.Year, toDate.Month, 1);
        while (cursor <= endMonth)
        {
            var occurrence = ResolveOccurrenceDate(rule.Schedule, cursor.Year, cursor.Month);
            var occurrenceWarnings = OccurrenceWarnings(rule.Schedule, occurrence, cursor.Year, cursor.Month);
            if (occurrence >= fromDate &&
                occurrence <= toDate &&
                occurrence >= startsAt &&
                (endsAt is null || occurrence <= endsAt.Value))
            {
                items.Add(new FixedRuleOccurrenceResponse(
                    DateRules.Format(occurrence),
                    $"{occurrence.Year:D4}-{occurrence.Month:D2}",
                    rule.Category,
                    rule.Description,
                    rule.Amount,
                    rule.CardId,
                    rule.Tags ?? [],
                    occurrenceWarnings));
            }

            cursor = cursor.AddMonths(1);
        }

        if (items.Count == 0)
        {
            previewWarnings.Add(new FixedRuleWarningResponse(
                "no_occurrences_in_range",
                "Rule does not generate occurrences in the requested range.",
                "info"));
        }

        return new FixedRulePreviewResponse(
            rule.Id,
            rule.Active,
            DateRules.Format(fromDate),
            DateRules.Format(toDate),
            items,
            previewWarnings.Count == 0 ? null : previewWarnings);
    }

    private DateOnly ResolveOccurrenceDate(FixedRuleScheduleDocument schedule, int year, int month)
    {
        var day = schedule.Type switch
        {
            "calendar_day" => Math.Min(schedule.Day!.Value, DateTime.DaysInMonth(year, month)),
            "business_day" => calendar.NthBusinessDay(year, month, schedule.Ordinal!.Value).Day,
            "period" when schedule.Period == "start" => 1,
            "period" when schedule.Period == "middle" => Math.Min(15, DateTime.DaysInMonth(year, month)),
            "period" when schedule.Period == "end" => DateTime.DaysInMonth(year, month),
            _ => throw new FixedRulesBadRequestException("invalid_schedule", "Invalid schedule.")
        };

        return new DateOnly(year, month, day);
    }

    private IReadOnlyList<FixedRuleWarningResponse>? OccurrenceWarnings(
        FixedRuleScheduleDocument schedule,
        DateOnly occurrence,
        int year,
        int month)
    {
        if (schedule.Type == "calendar_day" && schedule.Day is not null && occurrence.Day != schedule.Day.Value)
        {
            return [new FixedRuleWarningResponse(
                "calendar_day_adjusted",
                "Configured day does not exist in this month; occurrence was moved to month end.",
                "info")];
        }

        if (schedule.Type == "business_day" && schedule.Ordinal is not null && schedule.Ordinal.Value > BusinessDaysInMonth(year, month))
        {
            return [new FixedRuleWarningResponse(
                "business_day_adjusted",
                "Configured business day exceeds this month; occurrence was moved to the last business day.",
                "info")];
        }

        return null;
    }

    private int BusinessDaysInMonth(int year, int month)
    {
        var count = 0;
        var totalDays = DateTime.DaysInMonth(year, month);
        for (var day = 1; day <= totalDays; day++)
        {
            if (calendar.IsBusinessDay(new DateOnly(year, month, day)))
            {
                count++;
            }
        }

        return count;
    }
}

public sealed class WeekendOnlyBusinessCalendar : IBusinessCalendar
{
    public bool IsBusinessDay(DateOnly date) =>
        date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

    public DateOnly NthBusinessDay(int year, int month, int ordinal)
    {
        var lastBusinessDay = new DateOnly(year, month, 1);
        var count = 0;
        var totalDays = DateTime.DaysInMonth(year, month);

        for (var day = 1; day <= totalDays; day++)
        {
            var candidate = new DateOnly(year, month, day);
            if (!IsBusinessDay(candidate))
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
}
