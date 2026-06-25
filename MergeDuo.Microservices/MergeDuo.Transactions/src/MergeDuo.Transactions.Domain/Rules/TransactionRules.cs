using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MergeDuo.Transactions.Domain.Contracts;
using MergeDuo.Transactions.Domain.Documents;
using MergeDuo.Transactions.Domain.Exceptions;

namespace MergeDuo.Transactions.Domain.Rules;

public static partial class UserIdRules
{
    public static bool IsValid(string? userId) =>
        !string.IsNullOrWhiteSpace(userId) && UserIdRegex().IsMatch(userId);

    [GeneratedRegex("^usr_[A-Za-z0-9_-]{1,96}$", RegexOptions.CultureInvariant)]
    private static partial Regex UserIdRegex();
}

public static partial class TransactionIdRules
{
    public static bool IsValid(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TransactionIdRegex().IsMatch(id);

    [GeneratedRegex("^tx_[A-Za-z0-9_-]{1,160}$", RegexOptions.CultureInvariant)]
    private static partial Regex TransactionIdRegex();
}

public static partial class GroupIdRules
{
    public static bool IsValid(string? id) =>
        !string.IsNullOrWhiteSpace(id) && GroupIdRegex().IsMatch(id);

    [GeneratedRegex("^txg_[A-Za-z0-9_-]{1,160}$", RegexOptions.CultureInvariant)]
    private static partial Regex GroupIdRegex();
}

public static partial class CardIdRules
{
    public static bool IsValid(string? id) =>
        !string.IsNullOrWhiteSpace(id) && CardIdRegex().IsMatch(id);

    [GeneratedRegex("^card_[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex CardIdRegex();
}

public static partial class FixedRuleIdRules
{
    public static bool IsValid(string? id) =>
        !string.IsNullOrWhiteSpace(id) && FixedRuleIdRegex().IsMatch(id);

    [GeneratedRegex("^(fxr_|fr_|rule_)[A-Za-z0-9_-]{1,160}$", RegexOptions.CultureInvariant)]
    private static partial Regex FixedRuleIdRegex();
}

public static partial class YearMonthRules
{
    public static string EnsureValid(string? yearMonth)
    {
        if (string.IsNullOrWhiteSpace(yearMonth) || !YearMonthRegex().IsMatch(yearMonth))
        {
            throw new TransactionsBadRequestException("invalid_year_month", "Invalid year-month.");
        }

        return yearMonth;
    }

    public static string FromDate(DateOnly date) => $"{date.Year:0000}-{date.Month:00}";

    public static (int Year, int Month) Parse(string yearMonth)
    {
        var valid = EnsureValid(yearMonth);
        return (int.Parse(valid[..4]), int.Parse(valid[5..7]));
    }

    [GeneratedRegex("^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.CultureInvariant)]
    private static partial Regex YearMonthRegex();
}

public static class CategoryRules
{
    public const string Income = "income";
    public const string CreditCard = "credit_card";
    public const string Loan = "loan";
    public const string FixedExpense = "fixed_expense";
    public const string VariableExpense = "variable_expense";
    public const string Investment = "investment";

    private static readonly HashSet<string> ValidCategories =
    [
        Income,
        CreditCard,
        Loan,
        FixedExpense,
        VariableExpense,
        Investment
    ];

    public static string EnsureValid(string? category)
    {
        var normalized = category?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !ValidCategories.Contains(normalized))
        {
            throw new TransactionsBadRequestException("invalid_category", "Invalid transaction category.");
        }

        return normalized;
    }

    public static string KindFor(string category) => category switch
    {
        Income => "in",
        Investment => "invest",
        CreditCard or Loan or FixedExpense or VariableExpense => "out",
        _ => throw new TransactionsBadRequestException("invalid_category", "Invalid transaction category.")
    };
}

public static class MoneyRules
{
    public static decimal EnsureValid(decimal? amount)
    {
        if (amount is null || amount <= 0 || decimal.GetBits(amount.Value)[3] >> 16 > 2)
        {
            throw new TransactionsBadRequestException("invalid_amount", "Invalid transaction amount.");
        }

        return amount.Value;
    }
}

public static class CurrencyRules
{
    public static string Normalize(string? currency)
    {
        var normalized = string.IsNullOrWhiteSpace(currency)
            ? "BRL"
            : currency.Trim().ToUpperInvariant();

        if (normalized != "BRL")
        {
            throw new TransactionsBadRequestException("unsupported_currency", "Unsupported currency.");
        }

        return normalized;
    }
}

public static class TextRules
{
    public static string Description(string? value)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length is 0 or > 200)
        {
            throw new TransactionsBadRequestException("invalid_request", "Invalid description.");
        }

        return normalized;
    }

    public static string? Notes(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > 1000)
        {
            throw new TransactionsBadRequestException("invalid_request", "Invalid notes.");
        }

        return normalized.Length == 0 ? null : normalized;
    }

    public static string[] Tags(string[]? values)
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
            throw new TransactionsBadRequestException("invalid_request", "Invalid tags.");
        }

        return tags;
    }

    public static string? OwnerLabel(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized.Length <= 80
            ? normalized
            : throw new TransactionsBadRequestException("invalid_request", "Invalid owner label.");
    }
}

public static class InstallmentRules
{
    public static int EnsureTotal(int? total, int maxInstallments)
    {
        var value = total ?? 1;
        if (value < 1 || value > maxInstallments)
        {
            throw new TransactionsBadRequestException("invalid_installments", "Invalid installments.");
        }

        return value;
    }

    public static decimal[] SplitAmount(decimal amount, int total)
    {
        var totalCents = checked((long)(amount * 100m));
        var baseCents = totalCents / total;
        var values = new decimal[total];

        for (var i = 0; i < total - 1; i++)
        {
            values[i] = baseCents / 100m;
        }

        values[^1] = (totalCents - (baseCents * (total - 1))) / 100m;
        return values;
    }
}

public static class CardInvoiceRules
{
    public static DateOnly DueDateForPurchase(CardDocument card, DateOnly purchaseDate, int installmentIndex = 1)
    {
        var closingDate = DateWithMonthFallback(purchaseDate.Year, purchaseDate.Month, card.ClosingDay);
        var invoiceMonth = purchaseDate <= closingDate
            ? new DateOnly(purchaseDate.Year, purchaseDate.Month, 1)
            : new DateOnly(purchaseDate.Year, purchaseDate.Month, 1).AddMonths(1);

        var dueMonth = card.DueDay > card.ClosingDay ? invoiceMonth : invoiceMonth.AddMonths(1);
        dueMonth = dueMonth.AddMonths(installmentIndex - 1);
        return DateWithMonthFallback(dueMonth.Year, dueMonth.Month, card.DueDay);
    }

    private static DateOnly DateWithMonthFallback(int year, int month, int requestedDay)
    {
        var day = Math.Min(requestedDay, DateTime.DaysInMonth(year, month));
        return new DateOnly(year, month, day);
    }
}

public static class TransactionMapping
{
    public static TransactionResponse ToResponse(TransactionDocument transaction, string? cardTitle = null) =>
        new(
            transaction.Id,
            transaction.UserId,
            transaction.YearMonth,
            transaction.Date,
            transaction.PurchaseDate,
            transaction.Category,
            transaction.Kind,
            transaction.Description,
            transaction.Amount,
            transaction.Currency,
            transaction.OwnerLabel,
            transaction.CardId,
            cardTitle,
            transaction.FixedRuleId,
            transaction.Installments is null
                ? null
                : new InstallmentResponse(transaction.Installments.Index, transaction.Installments.Total, transaction.Installments.GroupId),
            transaction.Tags ?? [],
            transaction.Notes,
            new TransactionSourceResponse(transaction.Source.Channel),
            transaction.CreatedAt,
            transaction.UpdatedAt,
            transaction.ETag);
}

public sealed class UlidTransactionIdGenerator(TimeProvider clock) : MergeDuo.Transactions.Domain.Abstractions.ITransactionIdGenerator
{
    private const string CrockfordEncoding = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public string NewTransactionId() => $"tx_{NewUlid()}";

    public string NewGroupId() => $"txg_{NewUlid()}";

    public string FromIdempotencyKey(string prefix, string userId, string idempotencyKey, string payloadHash, int index)
    {
        var input = $"{prefix}|{userId}|{idempotencyKey.Trim()}|{index}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"{prefix}_{Base64Url(hash)[..32].ToLowerInvariant()}";
    }

    private string NewUlid()
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

                chars[i] = CrockfordEncoding[value];
        }

        return new string(chars).ToLowerInvariant();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class PayloadHash
{
    public static string For(CreateTransactionRequest request)
    {
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = false });
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
