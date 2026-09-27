using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Ucp.Signing;

namespace ElGuerre.Tendero.Ucp.Ap2;

/// <summary>
/// An SD-JWT presentation (RFC 9901) read apart and its disclosures checked, not
/// yet trusted: <see cref="Issuer"/> and <see cref="KeyBinding"/> are verified by
/// whoever knows the keys. <see cref="Claims"/> is the payload with every
/// disclosure put back where its digest was.
/// </summary>
public sealed record SdJwtPresentation(JwsParts Issuer, JsonObject Claims, JwsParts? KeyBinding, string KeyBoundPortion)
{
    /// <summary>
    /// A key-binding JWT belongs to this presentation when its <c>sd_hash</c> is the
    /// hash of everything before it — the issuer JWT and exactly these disclosures.
    /// </summary>
    public bool KeyBindingCoversIt()
    {
        if (KeyBinding is null)
            return false;

        using var payload = JsonDocument.Parse(KeyBinding.Payload);
        return payload.RootElement.TryGetProperty("sd_hash", out var hash)
               && hash.ValueKind == JsonValueKind.String
               && hash.GetString() == SdJwt.Digest(KeyBoundPortion);
    }
}

/// <summary>
/// Selective-disclosure JWTs, the format AP2 v0.2 writes its mandates in: an
/// issuer-signed JWT whose payload carries digests (<c>_sd</c>) in place of the
/// claims the holder may choose to reveal, each revealed by a disclosure — a
/// salted <c>[salt, name, value]</c> — appended after a <c>~</c>, and a key-binding
/// JWT last, signed by the holder's key named in <c>cnf</c>.
///
/// Only <c>sha-256</c>, the RFC's default and the only algorithm AP2's examples use.
/// </summary>
public static class SdJwt
{
    public const string HashAlgorithm = "sha-256";

    public const string IssuerType = "dc+sd-jwt";

    public const string KeyBindingType = "kb+sd-jwt";

    /// <summary>
    /// Splits and processes a presentation: every disclosure must be referenced
    /// exactly once by a digest, or this answers null — an unreferenced disclosure is
    /// a claim the issuer never signed.
    /// </summary>
    public static SdJwtPresentation? Read(string? presentation)
    {
        var parts = presentation?.Split('~');
        if (parts is null || Jws.Read(parts[0]) is not { } issuer)
            return null;

        // The last part is a key-binding JWT when it looks like one; a
        // presentation without one ends in an empty part.
        var last = parts[^1];
        var keyBinding = parts.Length > 1 && last.Contains('.', StringComparison.Ordinal) ? Jws.Read(last) : null;
        if (parts.Length > 1 && last.Contains('.', StringComparison.Ordinal) && keyBinding is null)
            return null;

        var disclosures = parts[1..(keyBinding is null ? parts.Length : parts.Length - 1)].Where(part => part.Length > 0).ToList();
        var boundPortion = keyBinding is null ? presentation! : presentation![..(presentation!.Length - last.Length)];

        try
        {
            if (JsonNode.Parse(issuer.Payload) is not JsonObject payload
                || payload["_sd_alg"] is { } alg && alg.GetValue<string>() != HashAlgorithm)
                return null;

            var byDigest = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
            foreach (var disclosure in disclosures)
            {
                if (JsonNode.Parse(Base64Url.DecodeFromChars(disclosure)) is not JsonArray { Count: 2 or 3 } decoded
                    || !byDigest.TryAdd(Digest(disclosure), decoded))
                    return null;
            }

            var used = new HashSet<string>(StringComparer.Ordinal);
            if (Process(payload, byDigest, used) is not JsonObject claims || used.Count != byDigest.Count)
                return null;

            return new SdJwtPresentation(issuer, claims, keyBinding, boundPortion);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Issues a presentation — what a platform does, and what this shop does only in
    /// development and tests, as the platforms it publishes profiles for. The named
    /// top-level claims become disclosures; the holder's key goes in <c>cnf</c> and
    /// signs a key-binding JWT for <paramref name="audience"/>.
    /// </summary>
    public static string Issue(
        IJwsSigner issuer, JsonObject claims, IReadOnlyCollection<string> disclosed,
        EcdsaJwsSigner holder, string audience, string nonce, DateTimeOffset now)
    {
        var payload = new JsonObject();
        var digests = new JsonArray();
        var disclosures = new List<string>();

        foreach (var (name, value) in claims)
        {
            if (!disclosed.Contains(name))
            {
                payload[name] = value?.DeepClone();
                continue;
            }

            var salt = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
            var disclosure = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(new JsonArray(salt, name, value?.DeepClone()).ToJsonString()));
            disclosures.Add(disclosure);
            digests.Add(Digest(disclosure));
        }

        payload["_sd"] = digests;
        payload["_sd_alg"] = HashAlgorithm;
        payload["cnf"] = new JsonObject { ["jwk"] = JsonSerializer.SerializeToNode(holder.PublicKey) };

        var bound = Jws.Sign(issuer, Encoding.UTF8.GetBytes(payload.ToJsonString()), IssuerType)
                    + "~" + string.Concat(disclosures.Select(disclosure => disclosure + "~"));
        var keyBinding = new JsonObject
        {
            ["iat"] = now.ToUnixTimeSeconds(),
            ["aud"] = audience,
            ["nonce"] = nonce,
            ["sd_hash"] = Digest(bound),
        };

        return bound + Jws.Sign(holder, Encoding.UTF8.GetBytes(keyBinding.ToJsonString()), KeyBindingType);
    }

    /// <summary>base64url(SHA-256(the ASCII of <paramref name="value"/>)) — a disclosure's digest, <c>sd_hash</c>, AP2's <c>checkout_hash</c>.</summary>
    public static string Digest(string value) => Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(value)));

    /// <summary>
    /// Puts disclosures back: an object's <c>_sd</c> digests become members, an
    /// array's <c>{"...": digest}</c> elements become values, and a digest nothing
    /// discloses is a decoy and is dropped.
    /// </summary>
    private static JsonNode? Process(JsonNode? node, Dictionary<string, JsonArray> byDigest, HashSet<string> used)
    {
        switch (node)
        {
            case JsonObject source:
                var target = new JsonObject();
                foreach (var (name, value) in source)
                {
                    if (name is not ("_sd" or "_sd_alg"))
                        target[name] = Process(value, byDigest, used);
                }

                foreach (var digest in source["_sd"]?.AsArray().Select(item => item!.GetValue<string>()) ?? [])
                {
                    if (!byDigest.TryGetValue(digest, out var disclosure))
                        continue;
                    if (disclosure.Count != 3 || !used.Add(digest))
                        throw new InvalidOperationException("A disclosure is used twice, or in the wrong place.");

                    var name = disclosure[1]!.GetValue<string>();
                    if (name is "_sd" or "..." || !target.TryAdd(name, Process(disclosure[2]?.DeepClone(), byDigest, used)))
                        throw new InvalidOperationException($"The disclosure of '{name}' collides with a claim.");
                }

                return target;

            case JsonArray items:
                var array = new JsonArray();
                foreach (var item in items)
                {
                    if (item is JsonObject { Count: 1 } reference && reference["..."] is JsonValue digestNode)
                    {
                        var digest = digestNode.GetValue<string>();
                        if (!byDigest.TryGetValue(digest, out var disclosure))
                            continue;
                        if (disclosure.Count != 2 || !used.Add(digest))
                            throw new InvalidOperationException("A disclosure is used twice, or in the wrong place.");
                        array.Add(Process(disclosure[1]?.DeepClone(), byDigest, used));
                    }
                    else
                    {
                        array.Add(Process(item, byDigest, used));
                    }
                }

                return array;

            default:
                return node?.DeepClone();
        }
    }
}
