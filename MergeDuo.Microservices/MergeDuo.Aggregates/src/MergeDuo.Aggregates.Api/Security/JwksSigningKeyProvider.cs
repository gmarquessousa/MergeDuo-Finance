using MergeDuo.Aggregates.Domain.Options;
using Microsoft.IdentityModel.Tokens;

namespace MergeDuo.Aggregates.Api.Security;

public sealed class JwksSigningKeyProvider(HttpClient httpClient, JwtOptions options, TimeProvider clock)
{
    private readonly object _gate = new();
    private IReadOnlyCollection<SecurityKey> _keys = [];
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public IEnumerable<SecurityKey> ResolveSigningKeys(
        string token,
        SecurityToken securityToken,
        string kid,
        TokenValidationParameters validationParameters)
    {
        var keys = GetKeys();
        return string.IsNullOrWhiteSpace(kid)
            ? keys
            : keys.Where(x => string.Equals(x.KeyId, kid, StringComparison.Ordinal));
    }

    private IReadOnlyCollection<SecurityKey> GetKeys()
    {
        var now = clock.GetUtcNow();
        if (_keys.Count > 0 && _expiresAt > now)
        {
            return _keys;
        }

        lock (_gate)
        {
            now = clock.GetUtcNow();
            if (_keys.Count > 0 && _expiresAt > now)
            {
                return _keys;
            }

            var json = httpClient.GetStringAsync(options.JwksUrl).GetAwaiter().GetResult();
            var jwks = new JsonWebKeySet(json);
            _keys = jwks.Keys.Cast<SecurityKey>().ToArray();
            _expiresAt = now.AddHours(1);
            return _keys;
        }
    }
}
