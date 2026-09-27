using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Ucp.Signing;

namespace ElGuerre.Tendero.Ucp.Ap2;

/// <summary>
/// **Development only**: the platforms this process pretends to be for its seeded
/// agents, so AP2 can be exercised end to end without a real one — the dev-issuer
/// pattern applied to UCP's Trusted Platform Provider flow. Each platform has a
/// signing key, published in its profile, and a holder key for the key-binding JWT.
///
/// A real platform signs a mandate only after the person consented in a channel it
/// trusts; this one signs whatever checkout it is handed, which is exactly why it
/// exists only in development. Keys live as long as the process.
/// </summary>
public sealed class DevelopmentPlatforms(TimeProvider clock) : IDisposable
{
    private readonly ConcurrentDictionary<string, Platform> platforms = new(StringComparer.Ordinal);

    /// <summary>How long a mandate lasts unless asked otherwise: the length of a person's attention, not of a session.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    public Jwk KeyOf(string agent) => PlatformOf(agent).Issuer.PublicKey;

    /// <summary>
    /// A closed checkout mandate over <paramref name="checkout"/> — first checking,
    /// as UCP says a platform MUST, that the shop signed it. Null when it did not.
    /// </summary>
    public string? SignCheckoutMandate(string agent, JsonObject checkout, IReadOnlyList<Jwk> shopKeys, string audience, TimeSpan? lifetime = null)
    {
        if (!MerchantAuthorization.Verify(checkout, shopKeys))
            return null;

        var now = clock.GetUtcNow();
        var checkoutJwt = MerchantAuthorization.CheckoutJwtOf(checkout);
        var claims = new JsonObject
        {
            ["vct"] = ClosedCheckoutMandateVerifier.Vct,
            ["checkout_jwt"] = checkoutJwt,
            ["checkout_hash"] = SdJwt.Digest(checkoutJwt),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = (now + (lifetime ?? DefaultLifetime)).ToUnixTimeSeconds(),
        };

        var platform = PlatformOf(agent);
        return SdJwt.Issue(platform.Issuer, claims, ["checkout_jwt"], platform.Holder, audience,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)), now);
    }

    public void Dispose()
    {
        foreach (var platform in platforms.Values)
            platform.Dispose();
    }

    private Platform PlatformOf(string agent) => platforms.GetOrAdd(agent, name => new Platform(name));

    private sealed class Platform : IDisposable
    {
        private readonly ECDsa issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public Platform(string agent)
        {
            // A readable kid, so a failing test names the platform it expected.
            Issuer = new EcdsaJwsSigner(issuerKey, $"dev-platform-{agent}-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Jwk.FromEcdsa(issuerKey).KeyId)))[..8]}");
            Holder = new EcdsaJwsSigner(holderKey);
        }

        public EcdsaJwsSigner Issuer { get; }

        public EcdsaJwsSigner Holder { get; }

        public void Dispose()
        {
            issuerKey.Dispose();
            holderKey.Dispose();
        }
    }
}
