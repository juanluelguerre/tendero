using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ElGuerre.Tendero.Ucp.Signing;

/// <summary>Whatever signs a JWS: a key id and an algorithm to name in the header, and the signature over the signing input.</summary>
public interface IJwsSigner
{
    string KeyId { get; }

    /// <summary><c>ES256</c>, <c>ES384</c> or <c>ES512</c> — AP2 v0.2 accepts ECDSA only.</summary>
    string Algorithm { get; }

    byte[] Sign(ReadOnlySpan<byte> signingInput);
}

/// <summary>An in-memory ECDSA key as a signer.</summary>
public sealed class EcdsaJwsSigner(ECDsa key, string? keyId = null) : IJwsSigner
{
    public string KeyId { get; } = keyId ?? Jwk.FromEcdsa(key).KeyId;

    public string Algorithm => Jws.AlgorithmFor(key);

    public Jwk PublicKey => Jwk.FromEcdsa(key, KeyId);

    public byte[] Sign(ReadOnlySpan<byte> signingInput) =>
        key.SignData(signingInput, Jws.HashFor(Algorithm), DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
}

/// <summary>A JWS read apart: its header, the encoded header and payload it was signed over, and the signature.</summary>
public sealed record JwsParts(JsonElement Header, string EncodedHeader, string EncodedPayload, byte[] Signature)
{
    public string? KeyId => Header.TryGetProperty("kid", out var kid) && kid.ValueKind == JsonValueKind.String ? kid.GetString() : null;

    public string? Algorithm => Header.TryGetProperty("alg", out var alg) && alg.ValueKind == JsonValueKind.String ? alg.GetString() : null;

    public byte[] Payload => Base64Url.DecodeFromChars(EncodedPayload);
}

/// <summary>
/// JSON Web Signature (RFC 7515) in the compact serialisation, with ECDSA only —
/// what AP2 v0.2 accepts and all UCP's mandate extension needs. Detached content
/// (Appendix F) is the compact form with the payload left out between the dots.
///
/// Built on <c>System.Security.Cryptography</c> rather than a JOSE package: the
/// dependency budget is small, and the one subtlety — a JWS wants the signature as
/// r‖s, not DER — is one enum.
/// </summary>
public static class Jws
{
    public static string Sign(IJwsSigner signer, ReadOnlySpan<byte> payload, string? type = null)
    {
        var header = Header(signer, type);
        var input = $"{header}.{Base64Url.EncodeToString(payload)}";
        return $"{input}.{Base64Url.EncodeToString(signer.Sign(Encoding.ASCII.GetBytes(input)))}";
    }

    /// <summary><c>&lt;header&gt;..&lt;signature&gt;</c>: signed over the payload, which travels elsewhere.</summary>
    public static string SignDetached(IJwsSigner signer, ReadOnlySpan<byte> payload)
    {
        var compact = Sign(signer, payload);
        return $"{compact[..compact.IndexOf('.')]}..{compact[(compact.LastIndexOf('.') + 1)..]}";
    }

    /// <summary>A compact JWS split and decoded, or null if it is not one. Nothing is verified here.</summary>
    public static JwsParts? Read(string? compact)
    {
        var parts = compact?.Split('.');
        if (parts is not { Length: 3 } || parts[0].Length == 0 || parts[2].Length == 0)
            return null;

        try
        {
            using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]));
            if (header.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            return new JwsParts(header.RootElement.Clone(), parts[0], parts[1], Base64Url.DecodeFromChars(parts[2]));
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary>A detached JWS with its payload put back, read apart.</summary>
    public static JwsParts? ReadDetached(string? detached, ReadOnlySpan<byte> payload) =>
        detached?.Split('.') is [var header, "", var signature]
            ? Read($"{header}.{Base64Url.EncodeToString(payload)}.{signature}")
            : null;

    /// <summary>
    /// True when <paramref name="key"/> signed it with the algorithm its header
    /// names. The header's algorithm has to be the key's: a header is signed, but
    /// an algorithm chosen by the token instead of the key is the oldest JWT attack.
    /// </summary>
    public static bool Verify(JwsParts jws, ECDsa key)
    {
        var algorithm = AlgorithmFor(key);
        if (jws.Algorithm != algorithm)
            return false;

        var input = Encoding.ASCII.GetBytes($"{jws.EncodedHeader}.{jws.EncodedPayload}");
        return key.VerifyData(input, jws.Signature, HashFor(algorithm), DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    public static string AlgorithmFor(ECDsa key) => key.KeySize switch
    {
        256 => "ES256",
        384 => "ES384",
        521 => "ES512",
        _ => throw new NotSupportedException($"No JWS algorithm for a {key.KeySize}-bit ECDSA key."),
    };

    public static HashAlgorithmName HashFor(string algorithm) => algorithm switch
    {
        "ES256" => HashAlgorithmName.SHA256,
        "ES384" => HashAlgorithmName.SHA384,
        "ES512" => HashAlgorithmName.SHA512,
        _ => throw new NotSupportedException($"{algorithm} is not an ECDSA JWS algorithm."),
    };

    private static string Header(IJwsSigner signer, string? type)
    {
        var header = new JsonObject { ["alg"] = signer.Algorithm, ["kid"] = signer.KeyId };
        if (type is not null)
            header["typ"] = type;
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(header.ToJsonString()));
    }
}
