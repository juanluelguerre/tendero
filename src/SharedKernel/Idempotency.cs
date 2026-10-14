using System.Security.Cryptography;
using System.Text;

namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// What a caller's idempotency key turned out to be when a request arrived
/// with it.
/// </summary>
public abstract record IdempotencyClaim
{
    private IdempotencyClaim()
    {
    }

    /// <summary>Nobody had used the key: this request runs, and says how it ended.</summary>
    public sealed record Fresh : IdempotencyClaim;

    /// <summary>The same request already ran: this is what it was answered.</summary>
    public sealed record Replayed(int Status, string Body) : IdempotencyClaim;

    /// <summary>The key was used for another request. The caller's mistake, and never run.</summary>
    public sealed record Mismatched : IdempotencyClaim;

    /// <summary>The same request is running now: its answer is not known yet.</summary>
    public sealed record InFlight : IdempotencyClaim;
}

/// <summary>
/// One key a caller sent, the request it sent it with, and what that request was
/// answered. No context owns it — every writing surface may keep one — which is
/// the shape the audit log and the outbox already have.
/// </summary>
public sealed class IdempotencyRecord
{
    /// <summary>Whose key it is: two callers may choose the same one.</summary>
    public required string Scope { get; init; }

    public required string Key { get; init; }

    /// <summary>The request, hashed: what tells a retry from a key used twice.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Null while the request is running.</summary>
    public int? Status { get; set; }

    public string? Body { get; set; }

    /// <summary>When the key was claimed.</summary>
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// **A retry must not happen twice, and a key must not mean two things.** The
/// contract every specification that names an `Idempotency-Key` settles on
/// (Stripe's, the IETF draft, UCP's *the server MUST*): keep the key with the
/// answer, give that answer back to the same request, and refuse — 409 — the
/// same key on a different one.
///
/// Claim first, run, then say how it ended: two requests racing on one key
/// cannot both run, and the one that loses is told the other is in flight.
/// A claim nobody finished — the process died in between — is taken over after
/// <see cref="Idempotency.Lease"/>, so a key is never lost for good.
///
/// Kept without an end: the specification asks for at least a day, and pruning
/// is work that exists only at a scale this shop does not have (the scale rule).
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyClaim> ClaimAsync(string scope, string key, string fingerprint, CancellationToken cancellationToken = default);

    /// <summary>The claimed request changed something: this is its answer, for whoever retries.</summary>
    Task CompleteAsync(string scope, string key, int status, string body, CancellationToken cancellationToken = default);

    /// <summary>The claimed request changed nothing — refused, or failed: the key is free to try again.</summary>
    Task ReleaseAsync(string scope, string key, CancellationToken cancellationToken = default);
}

public static class Idempotency
{
    /// <summary>The header every REST surface reads it from.</summary>
    public const string Header = "Idempotency-Key";

    /// <summary>What a key may be: enough for a UUID with room, and not a column anybody can fill.</summary>
    public const int MaximumKeyLength = 200;

    /// <summary>How long a claim nobody finished holds its key.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A request as one string a store can compare: its parts, each prefixed by
    /// its length so that no two different lists of parts are the same text,
    /// hashed.
    /// </summary>
    public static string Fingerprint(params ReadOnlySpan<string> parts)
    {
        var text = new StringBuilder();
        foreach (var part in parts)
            text.Append(part.Length).Append(':').Append(part).Append('|');

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
