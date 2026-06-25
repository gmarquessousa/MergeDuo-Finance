using System.Security.Cryptography;
using System.Text;
using MergeDuo.Identity.Domain.Documents;

namespace MergeDuo.Identity.Domain.Rules;

public static class IdentityRules
{
    public const int DeviceTtlSeconds = 7_776_000;

    public static string NewUserId() => "usr_" + Base64Url(RandomNumberGenerator.GetBytes(16));

    public static string NewSessionId() => "ses_" + Base64Url(RandomNumberGenerator.GetBytes(16));

    public static string DeviceId(string userId, string platform, string installId)
    {
        var source = $"{userId}|{platform}|{installId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return "dev_" + Base64Url(hash)[..16].ToLowerInvariant();
    }

    public static string Initials(string? name, string email)
    {
        var source = string.IsNullOrWhiteSpace(name) ? email.Split('@', 2)[0] : name;
        var parts = source.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return "U";
        }

        var chars = parts.Length == 1
            ? parts[0].Take(2)
            : new[] { parts[0][0], parts[^1][0] };

        return string.Concat(chars).ToUpperInvariant();
    }

    public static UserDocument CreateUser(
        string id,
        string handle,
        string name,
        string email,
        string googleSub,
        string? googlePictureUrl,
        string? hostedDomain,
        DateTimeOffset now)
    {
        return new UserDocument
        {
            Id = id,
            Name = name,
            Handle = handle,
            Email = email,
            AvatarInitials = Initials(name, email),
            AvatarUrl = null,
            MemberSince = now.ToString("MMMM 'de' yyyy", new System.Globalization.CultureInfo("pt-BR")),
            RegisteredAt = now.Date.ToString("yyyy-MM-dd"),
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null,
            Auth = new UserAuth
            {
                LastLoginAt = now,
                Google = new GoogleAuthState
                {
                    Sub = googleSub,
                    Email = email,
                    EmailVerified = true,
                    HostedDomain = hostedDomain,
                    PictureUrl = googlePictureUrl,
                    LinkedAt = now
                }
            }
        };
    }

    public static DeviceDocument UpsertDeviceSession(
        DeviceDocument? existing,
        string userId,
        string deviceId,
        DeviceProfile profile,
        string sessionId,
        bool rememberMe,
        string refreshTokenHash,
        string ipAddress,
        int lifetimeDays,
        DateTimeOffset now)
    {
        var device = existing ?? new DeviceDocument
        {
            Id = deviceId,
            UserId = userId,
            CreatedAt = now
        };

        device.Platform = profile.Platform;
        device.UserAgent = profile.UserAgent ?? "";
        device.Model = profile.Model ?? "";
        device.OsVersion = profile.OsVersion ?? "";
        device.AppVersion = profile.AppVersion ?? "";
        device.LastSeenAt = now;
        device.RevokedAt = null;
        device.Ttl = DeviceTtlSeconds;
        device.Session = new DeviceSession
        {
            RememberMe = rememberMe,
            SessionId = sessionId,
            RefreshTokenHash = refreshTokenHash,
            RefreshTokenRotatedAt = now,
            RefreshTokenExpiresAt = now.AddDays(lifetimeDays),
            LastIp = ipAddress,
            LastLocation = ""
        };
        return device;
    }

    public static bool IsRefreshSessionActive(DeviceDocument device, DateTimeOffset now)
    {
        return device.RevokedAt is null
            && !string.IsNullOrWhiteSpace(device.Session.SessionId)
            && !string.IsNullOrWhiteSpace(device.Session.RefreshTokenHash)
            && device.Session.RefreshTokenExpiresAt is { } expiresAt
            && expiresAt > now;
    }

    public static void RevokeSession(DeviceDocument device, DateTimeOffset now)
    {
        device.RevokedAt = now;
        device.LastSeenAt = now;
        device.Session.SessionId = null;
        device.Session.RefreshTokenHash = null;
        device.Session.RefreshTokenRotatedAt = null;
        device.Session.RefreshTokenExpiresAt = null;
    }

    public static void ApplySoftDelete(UserDocument user, DateTimeOffset now)
    {
        var suffix = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(user.Id)))[..12].ToLowerInvariant();
        user.DeletedAt = now;
        user.UpdatedAt = now;
        user.Handle = "@deleted_" + suffix[..Math.Min(12, suffix.Length)];
        user.Email = $"deleted+{user.Id}@deleted.mergeduo.local";
        user.Auth.Google.Email = user.Email;
        user.Auth.Google.Sub = $"deleted:{user.Id}:{suffix}";
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

public sealed record DeviceProfile(
    string InstallId,
    string Platform,
    string? UserAgent,
    string? Model,
    string? OsVersion,
    string? AppVersion);
