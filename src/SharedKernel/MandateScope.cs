namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// What a verified mandate authorises, as values (P11-5): the kind of mandate, who
/// signed it, the approval it refers to, the most it lets the shop charge, and
/// until when.
///
/// **A value that crosses, never a reference.** The protocol layer verifies the
/// credential (ADR 0040) and hands this on; Ordering freezes it onto the order it
/// placed, and nothing ever resolves it again — a mandate revoked tomorrow does not
/// change what was authorised today, the same rule an order's prices follow
/// (ADR 0016). The shop's own rules (P11-6) read it too.
/// </summary>
/// <param name="Kind">The credential type — AP2's <c>vct</c>, <c>mandate.checkout.1</c> for a closed checkout mandate.</param>
/// <param name="Issuer">The key that signed it, as the platform's profile names it.</param>
/// <param name="Reference">What identifies the approval itself: AP2's <c>checkout_hash</c>.</param>
/// <param name="ApprovedTotal">The total the person approved; the shop charges this or nothing.</param>
/// <param name="ExpiresAt">Null when the mandate says no expiry.</param>
public sealed record MandateScope(string Kind, string Issuer, string Reference, Money ApprovedTotal, DateTimeOffset? ExpiresAt)
{
    /// <summary>
    /// Whether charging <paramref name="total"/> at <paramref name="at"/> is within
    /// what was approved: the same currency, not a cent more, and not after it ends.
    /// A closed mandate approves one total, so a smaller one is not approved either —
    /// it is a different purchase.
    /// </summary>
    public bool Covers(Money total, DateTimeOffset at) =>
        string.Equals(total.Currency, ApprovedTotal.Currency, StringComparison.OrdinalIgnoreCase)
        && total.Amount == ApprovedTotal.Amount
        && (ExpiresAt is null || at < ExpiresAt);
}
