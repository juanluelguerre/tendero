using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace ElGuerre.Tendero.DevIssuer;

/// <summary>
/// One issued authorization code, and everything the token request has to agree
/// with before it becomes a token.
/// </summary>
/// <param name="Subject">Who pressed the button on the picker.</param>
/// <param name="RedirectUri">
/// Echoed back and compared. An authorization code that can be redeemed against
/// a different redirect URI than the one it was issued for is the classic way a
/// code ends up somewhere it was not meant to go.
/// </param>
/// <param name="CodeChallenge">The S256 challenge the client sent up front.</param>
/// <param name="Nonce">Travels into the id_token so the client can tie the two together.</param>
public sealed record DevAuthorizationCode(
    string Subject,
    string RedirectUri,
    string CodeChallenge,
    string? Nonce,
    DateTimeOffset ExpiresAt);

/// <summary>
/// The short-lived codes handed out by <c>/connect/authorize</c> and spent at
/// <c>/connect/token</c>.
///
/// **In memory, and single-use.** Both are the real rules rather than a
/// simplification: an authorization code is meant to live for seconds and to be
/// redeemable exactly once, and a dictionary with a removal on read is a more
/// honest implementation of that than a table would be. It dies with the process
/// for the same reason the signing key does.
///
/// PKCE is verified here rather than in the endpoint because this is the type
/// that owns the challenge. A code store that hands back the challenge and
/// trusts somebody else to check it is a code store with a hole in it.
/// </summary>
public sealed class DevAuthorizationCodes(TimeProvider clock)
{
    /// <summary>
    /// Seconds, not minutes. The client redeems it on the next request, and a
    /// code that outlives the redirect is a code somebody can find in a log.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, DevAuthorizationCode> codes = new(StringComparer.Ordinal);

    public string Issue(string subject, string redirectUri, string codeChallenge, string? nonce)
    {
        // 256 bits from the CSPRNG. The code is a bearer credential for as long
        // as it lives, so it is generated the way one is.
        var code = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

        this.codes[code] = new DevAuthorizationCode(
            subject, redirectUri, codeChallenge, nonce, clock.GetUtcNow().Add(Lifetime));

        Sweep();

        return code;
    }

    /// <summary>
    /// Spends a code, or explains why it cannot be spent.
    ///
    /// The failures are deliberately not distinguished to the caller beyond a
    /// message, because every one of them is the same answer to the client —
    /// <c>invalid_grant</c> — and telling a caller WHICH check failed is telling
    /// an attacker which half they got right.
    /// </summary>
    public bool TryRedeem(
        string code, string redirectUri, string codeVerifier, out DevAuthorizationCode? redeemed)
    {
        redeemed = null;

        // Removed on the first read, so a replay finds nothing. This is the
        // single-use rule, and it is one method call rather than a flag that
        // somebody has to remember to set.
        if (!this.codes.TryRemove(code, out var found))
            return false;

        if (found.ExpiresAt <= clock.GetUtcNow())
            return false;

        if (!string.Equals(found.RedirectUri, redirectUri, StringComparison.Ordinal))
            return false;

        if (!string.Equals(found.CodeChallenge, Challenge(codeVerifier), StringComparison.Ordinal))
            return false;

        redeemed = found;
        return true;
    }

    /// <summary>
    /// The S256 transformation from RFC 7636: base64url(SHA-256(ASCII(verifier))).
    ///
    /// Only S256 exists here. `plain` is in the specification and is a challenge
    /// that challenges nothing, and a development issuer that accepts it teaches
    /// a client it is acceptable.
    /// </summary>
    public static string Challenge(string codeVerifier) =>
        Base64UrlEncoder.Encode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(codeVerifier)));

    /// <summary>Codes nobody came back for. Cheap, and bounded by how many
    /// sign-ins a developer can start in a minute.</summary>
    private void Sweep()
    {
        var now = clock.GetUtcNow();

        foreach (var (code, issued) in this.codes)
        {
            if (issued.ExpiresAt <= now)
                this.codes.TryRemove(code, out _);
        }
    }
}
