namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// One context's share of what the shop holds about a person (P15-9,
/// docs/specs/shopper-account.md, *The shopper's own data*). The shopper's
/// download and the removal of their account gather every holder through this
/// port, so no context references another's data to say what it keeps: each
/// says it for itself (invariant 1).
///
/// **Erasing is the holder's decision about its own data**, within the law: a
/// holder forgets what it may forget, and keeps — and says it keeps — what the law
/// makes the shop keep. A holder changes tracked state and never saves; the one
/// asking saves once, so the account is removed whole or not at all.
/// </summary>
public interface IPersonalDataHolder
{
    /// <summary>The section's name in the download, camelCase: <c>addresses</c>, <c>orders</c>.</summary>
    string Section { get; }

    /// <summary>What this holder keeps about the customer, as it would be shown to them; null when it keeps nothing.</summary>
    Task<object?> ExportAsync(CustomerId customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// The customer's data forgotten, or kept where the law says so. Answers what
    /// happened, for the page and the audit row: whether anything was forgotten,
    /// and whether anything was kept.
    /// </summary>
    Task<Erasure> EraseAsync(CustomerId customer, CancellationToken cancellationToken = default);
}

/// <param name="Forgotten">Something of the customer's was removed.</param>
/// <param name="Kept">Something was kept because the law makes the shop keep it, and is blocked: used for nothing else.</param>
public sealed record Erasure(string Section, bool Forgotten, bool Kept);
