using System.Text.Json;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Ucp.Negotiation;
using ElGuerre.Tendero.Ucp.Signing;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Ucp.Ap2;

/// <summary>The error codes of UCP's AP2 mandate extension — each failure is exactly one of them.</summary>
public static class Ap2ErrorCodes
{
    public const string MandateRequired = "mandate_required";
    public const string AgentMissingKey = "agent_missing_key";
    public const string MandateInvalidSignature = "mandate_invalid_signature";
    public const string MandateExpired = "mandate_expired";
    public const string MandateScopeMismatch = "mandate_scope_mismatch";
    public const string MerchantAuthorizationInvalid = "merchant_authorization_invalid";
    public const string MerchantAuthorizationMissing = "merchant_authorization_missing";
}

/// <summary>
/// A checkout mandate that verified: who signed it, the checkout it authorises —
/// the shop's own terms, signed by the shop and then by the platform — and until when.
/// </summary>
public sealed record VerifiedCheckoutMandate(string Vct, string IssuerKeyId, JsonObject Checkout, string CheckoutHash, DateTimeOffset? ExpiresAt);

/// <summary>A verified mandate, or the UCP error code that says why not.</summary>
public sealed record MandateVerdict(VerifiedCheckoutMandate? Mandate, string? Error)
{
    public static MandateVerdict Refused(string code) => new(null, code);
}

/// <summary>
/// One kind of checkout mandate, keyed by its <c>vct</c> — the credential type AP2
/// names each mandate with. It receives claims whose signatures already verified
/// and decides what they authorise. A second kind is a class and a registration
/// (analysis §9: the open mandate is the next one).
/// </summary>
public interface ICheckoutMandateVerifier
{
    /// <param name="issuerKeyId">The platform key that signed it.</param>
    /// <param name="claims">The mandate's claims, disclosures put back.</param>
    MandateVerdict Verify(string issuerKeyId, JsonObject claims);
}

/// <summary>
/// AP2 v0.2's closed checkout mandate, <c>mandate.checkout.1</c>: the person
/// approved exactly this checkout. <c>checkout_jwt</c> is the shop's own signed
/// checkout and <c>checkout_hash</c> its hash; the first has to be the shop's
/// signature, the second has to be the first's hash.
/// </summary>
public sealed class ClosedCheckoutMandateVerifier(IMerchantSigner merchant) : ICheckoutMandateVerifier
{
    public const string Vct = "mandate.checkout.1";

    public MandateVerdict Verify(string issuerKeyId, JsonObject claims)
    {
        if (Text(claims, "checkout_jwt") is not { } checkoutJwt || Text(claims, "checkout_hash") is not { } hash || SdJwt.Digest(checkoutJwt) != hash)
            return MandateVerdict.Refused(Ap2ErrorCodes.MandateInvalidSignature);

        if (MerchantAuthorization.ReadCheckoutJwt(checkoutJwt, merchant.PublishedKeys) is not { } checkout)
            return MandateVerdict.Refused(Ap2ErrorCodes.MerchantAuthorizationInvalid);

        return new MandateVerdict(new VerifiedCheckoutMandate(Vct, issuerKeyId, checkout, hash, CheckoutMandates.ExpiryOf(claims)), null);
    }

    private static string? Text(JsonObject claims, string name) =>
        claims[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

/// <summary>
/// Verifies <c>ap2.checkout_mandate</c> the way UCP's Trusted Platform Provider
/// flow asks: an SD-JWT signed with a key from the platform's own profile, its
/// key-binding JWT signed by the key in <c>cnf</c> and covering exactly what
/// precedes it, not expired — and then whatever its kind requires.
/// </summary>
public sealed class CheckoutMandates(IServiceProvider services, TimeProvider clock)
{
    public MandateVerdict Verify(string? presentation, PlatformProfile platform)
    {
        if (string.IsNullOrWhiteSpace(presentation))
            return MandateVerdict.Refused(Ap2ErrorCodes.MandateRequired);

        if (SdJwt.Read(presentation) is not { } mandate || mandate.Issuer.KeyId is not { } keyId)
            return MandateVerdict.Refused(Ap2ErrorCodes.MandateInvalidSignature);

        using var issuerKey = platform.Keys.FirstOrDefault(key => key.KeyId == keyId)?.ToEcdsa();
        if (issuerKey is null)
            return MandateVerdict.Refused(Ap2ErrorCodes.AgentMissingKey);

        if (!Jws.Verify(mandate.Issuer, issuerKey) || !HolderSignedIt(mandate))
            return MandateVerdict.Refused(Ap2ErrorCodes.MandateInvalidSignature);

        var vct = mandate.Claims["vct"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        if (vct is null || services.GetKeyedService<ICheckoutMandateVerifier>(vct) is not { } kind)
            return MandateVerdict.Refused(Ap2ErrorCodes.MandateInvalidSignature);

        if (ExpiryOf(mandate.Claims) is { } expiry && expiry <= clock.GetUtcNow())
            return MandateVerdict.Refused(Ap2ErrorCodes.MandateExpired);

        return kind.Verify(keyId, mandate.Claims);
    }

    internal static DateTimeOffset? ExpiryOf(JsonObject claims) =>
        claims["exp"] is JsonValue value && value.TryGetValue<long>(out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    /// <summary>
    /// The key-binding JWT: required (UCP's schema says SD-JWT+kb), signed by the
    /// holder key the issuer put in <c>cnf</c>, and bound to these disclosures.
    /// </summary>
    private static bool HolderSignedIt(SdJwtPresentation mandate)
    {
        if (mandate.KeyBinding is not { } binding || mandate.Claims["cnf"]?["jwk"] is not JsonObject jwk)
            return false;

        // A cnf key names no kid as often as not: it is the only key in play.
        var named = (JsonObject)jwk.DeepClone();
        named["kid"] ??= "cnf";

        Jwk? holder;
        try
        {
            holder = named.Deserialize<Jwk>();
        }
        catch (JsonException)
        {
            return false;
        }

        using var key = holder?.ToEcdsa();
        return key is not null && Jws.Verify(binding, key) && mandate.KeyBindingCoversIt();
    }
}
