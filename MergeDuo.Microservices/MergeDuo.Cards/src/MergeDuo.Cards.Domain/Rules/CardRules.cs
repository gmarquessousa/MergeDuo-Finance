using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MergeDuo.Cards.Domain.Abstractions;
using MergeDuo.Cards.Domain.Contracts;
using MergeDuo.Cards.Domain.Documents;
using MergeDuo.Cards.Domain.Exceptions;

namespace MergeDuo.Cards.Domain.Rules;

public static partial class UserIdRules
{
    public static bool IsValid(string? userId) =>
        !string.IsNullOrWhiteSpace(userId) && UserIdRegex().IsMatch(userId);

    [GeneratedRegex("^usr_[A-Za-z0-9_-]{1,96}$", RegexOptions.CultureInvariant)]
    private static partial Regex UserIdRegex();
}

public static partial class CardIdRules
{
    public static bool IsValid(string? cardId) =>
        !string.IsNullOrWhiteSpace(cardId) && CardIdRegex().IsMatch(cardId);

    [GeneratedRegex("^card_[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex CardIdRegex();
}

public static class TitleRules
{
    public const int MaximumLength = 80;

    public static string Normalize(string? title)
    {
        var normalized = (title ?? "").Trim();
        if (normalized.Length is 0 or > MaximumLength)
        {
            throw new CardsBadRequestException("invalid_title", "Invalid card title.");
        }

        return normalized;
    }
}

public static class BillingDayRules
{
    public static int EnsureValid(int? day)
    {
        if (day is < 1 or > 31 or null)
        {
            throw new CardsBadRequestException("invalid_billing_day", "Invalid billing day.");
        }

        return day.Value;
    }
}

public static class CurrencyCodeRules
{
    public static string Normalize(string? currency)
    {
        var normalized = string.IsNullOrWhiteSpace(currency)
            ? "BRL"
            : currency.Trim().ToUpperInvariant();

        if (normalized != "BRL")
        {
            throw new CardsBadRequestException("unsupported_currency", "Unsupported currency.");
        }

        return normalized;
    }
}

public static partial class YearMonthRules
{
    public static string EnsureValid(string? yearMonth)
    {
        if (string.IsNullOrWhiteSpace(yearMonth) || !YearMonthRegex().IsMatch(yearMonth))
        {
            throw new CardsBadRequestException("invalid_year_month", "Invalid year-month.");
        }

        return yearMonth;
    }

    public static (int Year, int Month) Parse(string yearMonth)
    {
        var valid = EnsureValid(yearMonth);
        return (int.Parse(valid[..4]), int.Parse(valid[5..7]));
    }

    [GeneratedRegex("^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.CultureInvariant)]
    private static partial Regex YearMonthRegex();
}

public static class BillingCycleRules
{
    public static BillingCycle Calculate(int closingDay, int dueDay, string yearMonth)
    {
        var (dueYear, dueMonth) = YearMonthRules.Parse(yearMonth);
        var dueDate = DateWithMonthFallback(dueYear, dueMonth, dueDay);
        var closingMonth = dueDate.AddMonths(-1);
        var closingDate = DateWithMonthFallback(closingMonth.Year, closingMonth.Month, closingDay);
        var previousClosingMonth = closingDate.AddMonths(-1);
        var previousClosingDate = DateWithMonthFallback(previousClosingMonth.Year, previousClosingMonth.Month, closingDay);

        return new BillingCycle(
            ClosingDate: closingDate,
            DueDate: dueDate,
            CycleStart: previousClosingDate.AddDays(1),
            CycleEnd: closingDate);
    }

    private static DateOnly DateWithMonthFallback(int year, int month, int requestedDay)
    {
        var day = Math.Min(requestedDay, DateTime.DaysInMonth(year, month));
        return new DateOnly(year, month, day);
    }
}

public sealed record BillingCycle(DateOnly ClosingDate, DateOnly DueDate, DateOnly CycleStart, DateOnly CycleEnd);

public static class CardMapping
{
    public static CardResponse ToResponse(CardDocument card) =>
        new(card.Id, card.Title, card.ClosingDay, card.DueDay, card.Currency, card.CreatedAt, card.UpdatedAt, card.ETag);

    public static BillingCycleResponse ToResponse(BillingCycle cycle) =>
        new(cycle.ClosingDate, cycle.DueDate);
}

public sealed class UlidCardIdGenerator(TimeProvider clock) : ICardIdGenerator
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

        return $"card_{new string(chars).ToLowerInvariant()}";
    }
}
