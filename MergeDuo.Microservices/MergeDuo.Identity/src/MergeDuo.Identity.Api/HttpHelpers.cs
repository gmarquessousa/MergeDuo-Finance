using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MergeDuo.Identity.Domain.Options;

namespace MergeDuo.Identity.Api;

public static class HttpHelpers
{
    public const string ChallengeCookieName = "md_challenge";
    public const string CsrfCookieName = "md_csrf";

    public static string NewSecret(int bytes = 32)
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static CookieOptions RefreshCookieOptions(
        RefreshTokenOptions options,
        bool rememberMe,
        DateTimeOffset now)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = CookieSameSite(options),
            Path = "/auth",
            Domain = string.IsNullOrWhiteSpace(options.CookieDomain) ? null : options.CookieDomain,
            Expires = rememberMe ? now.AddDays(options.LifetimeDays) : null
        };
    }

    public static CookieOptions CsrfCookieOptions(RefreshTokenOptions options, DateTimeOffset now) =>
        new()
        {
            HttpOnly = false,
            Secure = true,
            SameSite = CookieSameSite(options),
            Path = "/auth",
            Domain = string.IsNullOrWhiteSpace(options.CookieDomain) ? null : options.CookieDomain,
            Expires = now.AddDays(options.LifetimeDays)
        };

    public static CookieOptions ExpiredCookieOptions(RefreshTokenOptions options, bool httpOnly) =>
        new()
        {
            HttpOnly = httpOnly,
            Secure = true,
            SameSite = CookieSameSite(options),
            Path = "/auth",
            Domain = string.IsNullOrWhiteSpace(options.CookieDomain) ? null : options.CookieDomain,
            Expires = DateTimeOffset.UnixEpoch
        };

    public static SameSiteMode CookieSameSite(RefreshTokenOptions options) =>
        options.CookieSameSite?.Trim().ToLowerInvariant() switch
        {
            "none" => SameSiteMode.None,
            "strict" => SameSiteMode.Strict,
            "lax" => SameSiteMode.Lax,
            _ => SameSiteMode.Lax
        };

    public static string CreateChallengeCookie(string nonce, string csrf, DateTimeOffset expiresAt, string pepper)
    {
        var expires = expiresAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var payload = $"v1.{nonce}.{csrf}.{expires}";
        var signature = Sign(payload, pepper);
        return $"{payload}.{signature}";
    }

    public static bool TryReadChallengeCookie(
        string cookie,
        string csrfHeader,
        string pepper,
        DateTimeOffset now,
        out string nonce)
    {
        nonce = "";
        var parts = cookie.Split('.', 5);
        if (parts.Length != 5 || parts[0] != "v1")
        {
            return false;
        }

        var payload = string.Join('.', parts[..4]);
        if (!FixedTimeEquals(Sign(payload, pepper), parts[4]))
        {
            return false;
        }

        if (!FixedTimeEquals(parts[2], csrfHeader))
        {
            return false;
        }

        if (!long.TryParse(parts[3], out var unixExpires))
        {
            return false;
        }

        if (DateTimeOffset.FromUnixTimeSeconds(unixExpires) <= now)
        {
            return false;
        }

        nonce = parts[1];
        return true;
    }

    public static string CreateRedirectState(
        string nonce,
        string csrf,
        bool rememberMe,
        string returnPath,
        DeviceRequest device,
        DateTimeOffset expiresAt,
        string pepper)
    {
        var payload = JsonSerializer.Serialize(new RedirectLoginState(
            nonce,
            csrf,
            rememberMe,
            returnPath,
            device,
            expiresAt.ToUnixTimeSeconds()));
        var encoded = Base64Url(Encoding.UTF8.GetBytes(payload));
        return $"v1.{encoded}.{Sign(encoded, pepper)}";
    }

    public static bool TryReadRedirectState(
        string state,
        string pepper,
        DateTimeOffset now,
        out RedirectLoginState redirectState)
    {
        redirectState = RedirectLoginState.Empty;
        var parts = state.Split('.', 3);
        if (parts.Length != 3 || parts[0] != "v1")
        {
            return false;
        }

        if (!FixedTimeEquals(Sign(parts[1], pepper), parts[2]))
        {
            return false;
        }

        RedirectLoginState? parsed;
        try
        {
            var json = Encoding.UTF8.GetString(FromBase64Url(parts[1]));
            parsed = JsonSerializer.Deserialize<RedirectLoginState>(json);
        }
        catch
        {
            return false;
        }

        if (parsed is null
            || string.IsNullOrWhiteSpace(parsed.Nonce)
            || string.IsNullOrWhiteSpace(parsed.Csrf)
            || string.IsNullOrWhiteSpace(parsed.Device.InstallId)
            || string.IsNullOrWhiteSpace(parsed.Device.Platform)
            || DateTimeOffset.FromUnixTimeSeconds(parsed.ExpiresAt) <= now)
        {
            return false;
        }

        redirectState = parsed;
        return true;
    }

    public static bool FixedTimeEquals(string actual, string expected)
    {
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return actualBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    private static string Sign(string payload, string pepper)
    {
        var bytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(pepper), Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Convert.FromBase64String(normalized);
    }
}

public sealed record RedirectLoginState(
    string Nonce,
    string Csrf,
    bool RememberMe,
    string ReturnPath,
    DeviceRequest Device,
    long ExpiresAt)
{
    public static RedirectLoginState Empty { get; } = new(
        "",
        "",
        false,
        "/",
        new DeviceRequest("", "", null, null, null, null),
        0);
}
