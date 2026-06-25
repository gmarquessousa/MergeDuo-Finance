using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using MergeDuo.FixedRules.Domain.Abstractions;
using MergeDuo.FixedRules.Domain.Contracts;
using MergeDuo.FixedRules.Domain.Documents;
using MergeDuo.FixedRules.Domain.Exceptions;

namespace MergeDuo.FixedRules.Domain.Rules;

public static partial class UserIdRules
{
    public static bool IsValid(string? userId) =>
        !string.IsNullOrWhiteSpace(userId) && UserIdRegex().IsMatch(userId);

    [GeneratedRegex("^usr_[A-Za-z0-9_-]{1,96}$", RegexOptions.CultureInvariant)]
    private static partial Regex UserIdRegex();
}

public static partial class FixedRuleIdRules
{
    public static bool IsValid(string? fixedRuleId) =>
        !string.IsNullOrWhiteSpace(fixedRuleId) && FixedRuleIdRegex().IsMatch(fixedRuleId);

    public static void EnsureValid(string? fixedRuleId)
    {
        if (!IsValid(fixedRuleId))
        {
            throw new FixedRulesBadRequestException("invalid_fixed_rule_id", "Invalid fixed rule id.");
        }
    }

    [GeneratedRegex("^fxr_[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex FixedRuleIdRegex();
}

public static partial class CardIdRules
{
    public static bool IsValid(string? cardId) =>
        !string.IsNullOrWhiteSpace(cardId) && CardIdRegex().IsMatch(cardId);

    [GeneratedRegex("^card_[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex CardIdRegex();
}

public static class CategoryRules
{
    private static readonly HashSet<string> ValidCategories =
    [
        "income",
        "credit_card",
        "loan",
        "fixed_expense",
        "variable_expense",
        "investment"
    ];

    public static string Normalize(string? category)
    {
        var normalized = (category ?? "").Trim();
        if (!ValidCategories.Contains(normalized))
        {
            throw new FixedRulesBadRequestException("invalid_category", "Invalid category.");
        }

        return normalized;
    }
}

public static class DescriptionRules
{
    public const int MaximumLength = 120;

    public static string Normalize(string? description)
    {
        var normalized = (description ?? "").Trim();
        if (normalized.Length is 0 or > MaximumLength)
        {
            throw new FixedRulesBadRequestException("invalid_description", "Invalid description.");
        }

        return normalized;
    }
}

public static class TagRules
{
    public static string[] Normalize(string[]? values)
    {
        if (values is null)
        {
            return [];
        }

        var tags = values
            .Select(x => (x ?? "").Trim().ToLowerInvariant())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (tags.Length > 20 || tags.Any(x => x.Length > 40))
        {
            throw new FixedRulesBadRequestException("invalid_tags", "Invalid tags.");
        }

        return tags;
    }
}

public static class AmountRules
{
    public static decimal EnsureValid(decimal? amount)
    {
        if (amount is null || amount <= 0)
        {
            throw new FixedRulesBadRequestException("invalid_amount", "Invalid amount.");
        }

        return amount.Value;
    }
}

public static class DateRules
{
    public static string EnsureDate(string? value, string code = "invalid_date_range")
    {
        if (!TryParse(value, out var date))
        {
            throw new FixedRulesBadRequestException(code, "Invalid date range.");
        }

        return Format(date);
    }

    public static string? EnsureOptionalDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return EnsureDate(value);
    }

    public static DateOnly Parse(string value)
    {
        if (!TryParse(value, out var date))
        {
            throw new FixedRulesBadRequestException("invalid_date_range", "Invalid date range.");
        }

        return date;
    }

    public static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static void EnsureRange(string startsAt, string? endsAt)
    {
        if (endsAt is null)
        {
            return;
        }

        if (Parse(endsAt) < Parse(startsAt))
        {
            throw new FixedRulesBadRequestException("invalid_date_range", "Invalid date range.");
        }
    }

    private static bool TryParse(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out date);
}

public static class ScheduleRules
{
    public static FixedRuleScheduleDocument Normalize(FixedRuleScheduleRequest? request)
    {
        if (request is null)
        {
            throw new FixedRulesBadRequestException("invalid_schedule", "Invalid schedule.");
        }

        RequestRules.EnsureNoExtraFields(request.ExtraFields, "invalid_schedule");
        return Normalize(request.Type, request.Day, request.Ordinal, request.Period);
    }

    public static FixedRuleScheduleDocument Normalize(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            throw new FixedRulesBadRequestException("invalid_schedule", "Invalid schedule.");
        }

        var request = element.Deserialize<FixedRuleScheduleRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Normalize(request);
    }

    public static FixedRuleScheduleDocument Normalize(string? type, int? day, int? ordinal, string? period)
    {
        return type switch
        {
            "calendar_day" when day is >= 1 and <= 31 && ordinal is null && period is null =>
                new FixedRuleScheduleDocument { Type = "calendar_day", Day = day.Value },
            "business_day" when ordinal is >= 1 and <= 23 && day is null && period is null =>
                new FixedRuleScheduleDocument { Type = "business_day", Ordinal = ordinal.Value },
            "period" when day is null && ordinal is null && period is "start" or "middle" or "end" =>
                new FixedRuleScheduleDocument { Type = "period", Period = period },
            _ => throw new FixedRulesBadRequestException("invalid_schedule", "Invalid schedule.")
        };
    }

    public static FixedRuleScheduleDocument Clone(FixedRuleScheduleDocument schedule) =>
        new()
        {
            Type = schedule.Type,
            Day = schedule.Day,
            Ordinal = schedule.Ordinal,
            Period = schedule.Period
        };
}

public static class FixedRuleMapping
{
    public static FixedRuleResponse ToResponse(
        FixedRuleDocument rule,
        IReadOnlyList<FixedRuleWarningResponse>? warnings = null) =>
        new(
            rule.Id,
            rule.Category,
            rule.Description,
            rule.Amount,
            rule.CardId,
            rule.Tags ?? [],
            ScheduleRules.Clone(rule.Schedule),
            rule.StartsAt,
            rule.EndsAt,
            rule.Active,
            rule.LastRunAt,
            rule.NextRunAt,
            rule.CreatedAt,
            rule.UpdatedAt,
            rule.ETag,
            warnings);
}

public sealed class UlidFixedRuleIdGenerator(TimeProvider clock) : IFixedRuleIdGenerator
{
    private const string Encoding = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public string NewId()
    {
        Span<byte> bytes = stackalloc byte[16];
        var timestamp = clock.GetUtcNow().ToUnixTimeMilliseconds();

        bytes[0] = (byte)(timestamp >> 40);
        bytes[1] = (byte)(timestamp >> 32);
        bytes[2] = (byte)(timestamp >> 24);
        bytes[3] = (byte)(timestamp >> 16);
        bytes[4] = (byte)(timestamp >> 8);
        bytes[5] = (byte)timestamp;
        RandomNumberGenerator.Fill(bytes[6..]);

        Span<char> chars = stackalloc char[26];
        for (var i = 0; i < chars.Length; i++)
        {
            var value = 0;
            for (var bit = 0; bit < 5; bit++)
            {
                value <<= 1;
                var dataBit = (i * 5) + bit - 2;
                if (dataBit < 0)
                {
                    continue;
                }

                var byteIndex = dataBit / 8;
                var bitInByte = 7 - (dataBit % 8);
                value |= (bytes[byteIndex] >> bitInByte) & 1;
            }

            chars[i] = Encoding[value];
        }

        return $"fxr_{new string(chars).ToLowerInvariant()}";
    }
}

public static class RequestRules
{
    public static void EnsureNoExtraFields(Dictionary<string, JsonElement>? extraFields, string code = "invalid_request")
    {
        if (extraFields is { Count: > 0 })
        {
            throw new FixedRulesBadRequestException(code, "Request contains unsupported fields.");
        }
    }

    public static string? ReadNullableString(JsonElement element, string code)
    {
        if (element.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind is not JsonValueKind.String)
        {
            throw new FixedRulesBadRequestException(code, "Invalid request.");
        }

        return element.GetString();
    }

    public static decimal ReadDecimal(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Number || !element.TryGetDecimal(out var value))
        {
            throw new FixedRulesBadRequestException("invalid_amount", "Invalid amount.");
        }

        return value;
    }

    public static bool ReadBoolean(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new FixedRulesBadRequestException("invalid_request", "Invalid request.");
        }

        return element.GetBoolean();
    }

    public static string[] ReadStringArray(JsonElement element, string code)
    {
        if (element.ValueKind is JsonValueKind.Null)
        {
            return [];
        }

        if (element.ValueKind is not JsonValueKind.Array)
        {
            throw new FixedRulesBadRequestException(code, "Invalid request.");
        }

        try
        {
            return element.Deserialize<string[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        }
        catch (JsonException ex)
        {
            _ = ex;
            throw new FixedRulesBadRequestException(code, "Invalid request.");
        }
    }
}
