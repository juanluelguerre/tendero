using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace ElGuerre.Tendero.Ucp.Signing;

/// <summary>
/// A public JSON Web Key (RFC 7517), as UCP's profile publishes it under
/// <c>keys</c>: <c>kid</c> and <c>kty</c> required, and for an elliptic-curve key
/// its curve and its two coordinates. Never a private member — a profile is public.
///
/// UCP's vocabulary is open: a key of a type this shop cannot use is kept and
/// skipped, not an error, so <see cref="ToEcdsa"/> answers null rather than throwing.
/// </summary>
public sealed record Jwk
{
    [JsonPropertyName("kid")]
    public required string KeyId { get; init; }

    [JsonPropertyName("kty")]
    public required string KeyType { get; init; }

    [JsonPropertyName("crv")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Curve { get; init; }

    [JsonPropertyName("x")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? X { get; init; }

    [JsonPropertyName("y")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Y { get; init; }

    [JsonPropertyName("alg")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Algorithm { get; init; }

    [JsonPropertyName("use")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Use { get; init; }

    /// <summary>
    /// The public half of <paramref name="key"/>. With no <paramref name="keyId"/>
    /// the kid is the key's RFC 7638 thumbprint — what UCP asks of a key that may
    /// also sign for Web Bot Auth, and a stable name whoever computes it.
    /// </summary>
    public static Jwk FromEcdsa(ECDsa key, string? keyId = null)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        var curve = CurveName(key.KeySize);
        var x = Base64Url.EncodeToString(parameters.Q.X);
        var y = Base64Url.EncodeToString(parameters.Q.Y);

        return new Jwk
        {
            KeyId = keyId ?? Thumbprint(curve, x, y),
            KeyType = "EC",
            Curve = curve,
            X = x,
            Y = y,
            Algorithm = Jws.AlgorithmFor(key),
            Use = "sig",
        };
    }

    /// <summary>An ECDSA key to verify with, or null for a key this shop does not use (another type, another curve, a malformed coordinate).</summary>
    public ECDsa? ToEcdsa()
    {
        if (KeyType != "EC" || X is null || Y is null)
            return null;

        var curve = Curve switch
        {
            "P-256" => ECCurve.NamedCurves.nistP256,
            "P-384" => ECCurve.NamedCurves.nistP384,
            "P-521" => ECCurve.NamedCurves.nistP521,
            _ => (ECCurve?)null,
        };
        if (curve is null)
            return null;

        try
        {
            return ECDsa.Create(new ECParameters
            {
                Curve = curve.Value,
                Q = new ECPoint { X = Base64Url.DecodeFromChars(X), Y = Base64Url.DecodeFromChars(Y) },
            });
        }
        // A point that is not on the curve surfaces as PlatformNotSupportedException on
        // Windows, not as a CryptographicException — found by phase 11's Stryker close,
        // when a malformed key in a profile would have crashed the verifier instead of
        // being skipped, as UCP asks.
        catch (Exception exception) when (exception is FormatException or CryptographicException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    /// <summary>RFC 7638: SHA-256 of the required members, in lexical order, with no whitespace.</summary>
    public static string Thumbprint(string curve, string x, string y) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $$"""{"crv":"{{curve}}","kty":"EC","x":"{{x}}","y":"{{y}}"}""")));

    private static string CurveName(int keySize) => keySize switch
    {
        256 => "P-256",
        384 => "P-384",
        521 => "P-521",
        _ => throw new NotSupportedException($"No JWK curve name for a {keySize}-bit ECDSA key."),
    };
}
