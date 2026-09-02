using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Pricing.Domain;

/// <summary>
/// Why a promotion did not apply.
///
/// This is the difference between a promotions engine and magic. A cart showing
/// two discounts and staying quiet about the third makes the shopper guess; one
/// that says *"not combinable with Summer sale"* has turned an internal rule
/// into information. And the same value pays for itself twice more later: it is
/// the exact shape of *"this AP2 mandate does not cover this amount"*.
///
/// A reason carries a CODE and a text. The code is what a test asserts and what
/// an interface can phrase its own way; the text is what gets shown, and it
/// exists in both cultures because invariant 6 admits no bare strings.
/// </summary>
public sealed record RuleReason(string Code, LocalizedText Explanation);

/// <summary>
/// The reasons the engine knows how to give, as a table. Same idiom as
/// <c>Order.AllowedTransitions</c>: a reason from outside this list is a bug,
/// not a new string.
/// </summary>
public static class RuleReasons
{
    public const string NotCombinableWithCode = "not-combinable-with";
    public const string SupersededByExclusiveCode = "superseded-by-exclusive";
    public const string ConditionNotMetCode = "condition-not-met";
    public const string NothingToDiscountCode = "nothing-to-discount";

    /// <summary>Falls to the exclusivity group: another of its group already
    /// took the slot.</summary>
    public static RuleReason NotCombinableWith(LocalizedText winner) => new(
        NotCombinableWithCode,
        Both($"No acumulable con {winner.In("es")}.",
             $"Not combinable with {winner.In("en")}."));

    /// <summary>Falls because a globally exclusive promotion closed the
    /// evaluation.</summary>
    public static RuleReason SupersededByExclusive(LocalizedText winner) => new(
        SupersededByExclusiveCode,
        Both($"{winner.In("es")} es exclusiva y no admite otras promociones.",
             $"{winner.In("en")} is exclusive and admits no other promotions."));

    /// <summary>Falls because the cart does not meet its condition.</summary>
    public static RuleReason ConditionNotMet(LocalizedText requirement) => new(
        ConditionNotMetCode,
        Both($"No se cumple la condición: {requirement.In("es")}.",
             $"Condition not met: {requirement.In("en")}."));

    /// <summary>
    /// Falls because there was nothing left to discount. Said out loud rather
    /// than swallowed: a 0,00 € discount that applied and a suppressed one look
    /// identical in a total, and they are not the same thing.
    /// </summary>
    public static readonly RuleReason NothingToDiscount = new(
        NothingToDiscountCode,
        Both("No quedaba importe sobre el que aplicarla.",
             "There was nothing left to discount."));

    internal static LocalizedText Both(string es, string en) =>
        new(new Dictionary<string, string> { ["es"] = es, ["en"] = en });
}
