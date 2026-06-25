using System.Security.Cryptography;
using System.Text;
using MergeDuo.Identity.Domain.Documents;

namespace MergeDuo.Identity.Domain.Rules;

public static class IdentityReservationRules
{
    public const string KindHandle = "handle";
    public const string KindEmail = "email";
    public const string KindGoogleSub = "googleSub";

    public const string StatusPending = "pending";
    public const string StatusActive = "active";
    public const string StatusReleased = "released";

    public static IReadOnlyList<IdentityReservationValue> ForUser(UserDocument user) =>
    [
        For(KindHandle, user.Handle),
        For(KindEmail, user.Email),
        For(KindGoogleSub, user.Auth.Google.Sub)
    ];

    public static IdentityReservationValue For(string kind, string value)
    {
        var normalized = Normalize(kind, value);
        var hash = Sha256Hex($"{kind}:{normalized}");
        return new IdentityReservationValue(kind, normalized, $"idx_{hash}", hash);
    }

    public static IdentityReservationDocument ToDocument(
        IdentityReservationValue value,
        string userId,
        string status,
        DateTimeOffset now) =>
        new()
        {
            Id = value.Id,
            Kind = value.Kind,
            ValueHash = value.ValueHash,
            UserId = userId,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static string Normalize(string kind, string value)
    {
        var normalized = value.Trim();
        return kind is KindHandle or KindEmail
            ? normalized.ToLowerInvariant()
            : normalized;
    }

    private static string Sha256Hex(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}

public sealed record IdentityReservationValue(
    string Kind,
    string NormalizedValue,
    string Id,
    string ValueHash);
