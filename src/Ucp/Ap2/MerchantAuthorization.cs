using System.Text.Json;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Ucp.Signing;

namespace ElGuerre.Tendero.Ucp.Ap2;

/// <summary>
/// <c>ap2.merchant_authorization</c>: the shop's signature over a checkout it
/// answers, so the platform can prove to the person — and later to anyone — that
/// these were the shop's terms.
///
/// A JWS with detached content, <c>&lt;header&gt;..&lt;signature&gt;</c>, over the
/// JCS bytes of the checkout **without** its <c>ap2</c> member — excluded whole, so
/// whatever AP2 adds there later is excluded too. Put the payload back between the
/// dots and it is a compact JWS whose payload is the checkout: AP2's
/// <c>checkout_jwt</c> (analysis §9).
/// </summary>
public static class MerchantAuthorization
{
    public const string Member = "ap2";

    public const string AuthorizationMember = "merchant_authorization";

    /// <summary>Signs the checkout and writes the signature into its <c>ap2</c> member.</summary>
    public static JsonObject Sign(JsonObject checkout, IJwsSigner signer)
    {
        checkout.Remove(Member);
        checkout[Member] = new JsonObject { [AuthorizationMember] = Jws.SignDetached(signer, SignedBytes(checkout)) };
        return checkout;
    }

    /// <summary>
    /// What a platform does before showing the checkout (UCP: it MUST): the
    /// signature in <c>ap2</c> verifies over the rest, with a key the shop published.
    /// </summary>
    public static bool Verify(JsonObject checkout, IReadOnlyList<Jwk> keys) =>
        checkout[Member]?[AuthorizationMember] is JsonValue value
        && value.TryGetValue<string>(out var authorization)
        && Jws.ReadDetached(authorization, SignedBytes(checkout)) is { } jws
        && VerifiedBy(jws, keys);

    /// <summary>
    /// AP2's <c>checkout_jwt</c> read back: the checkout inside it, if one of the
    /// shop's keys signed it; null if not — <c>merchant_authorization_invalid</c>.
    /// </summary>
    public static JsonObject? ReadCheckoutJwt(string? checkoutJwt, IReadOnlyList<Jwk> keys)
    {
        if (Jws.Read(checkoutJwt) is not { } jws || !VerifiedBy(jws, keys))
            return null;

        try
        {
            return JsonNode.Parse(jws.Payload) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The checkout_jwt a platform builds from a signed checkout: the detached
    /// signature with the JCS payload put back. What the development platform does,
    /// and what the tests do.
    /// </summary>
    public static string CheckoutJwtOf(JsonObject checkout)
    {
        var authorization = checkout[Member]![AuthorizationMember]!.GetValue<string>();
        var jws = Jws.ReadDetached(authorization, SignedBytes(checkout))!;
        return $"{jws.EncodedHeader}.{jws.EncodedPayload}.{authorization[(authorization.LastIndexOf('.') + 1)..]}";
    }

    private static byte[] SignedBytes(JsonObject checkout)
    {
        var payload = (JsonObject)checkout.DeepClone();
        payload.Remove(Member);
        return Jcs.Canonicalize(JsonSerializer.SerializeToElement(payload));
    }

    private static bool VerifiedBy(JwsParts jws, IReadOnlyList<Jwk> keys)
    {
        using var key = keys.FirstOrDefault(candidate => candidate.KeyId == jws.KeyId)?.ToEcdsa();
        return key is not null && Jws.Verify(jws, key);
    }
}
