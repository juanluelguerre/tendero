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
///
/// And, since phase 13b, **what the sentence is made of** (ADR 0059): the
/// condition that was not met, how far the cart is from it, the promotion that
/// won instead. A page cannot format money its reader's way or name a category
/// out of prose, and it adds no money of its own — so the distance is worked
/// out here.
/// </summary>
/// <param name="Requires">The condition that was not met, for <see cref="RuleReasonCode.ConditionNotMet"/>.</param>
/// <param name="ShortBy">How much the cart is below the condition's minimum, when that is what failed.</param>
/// <param name="UnitsShort">How many units the lines it reaches are below its quantity, when that is what failed.</param>
/// <param name="Winner">The promotion that took the place of this one, by name.</param>
public sealed record RuleReason(
    RuleReasonCode Code,
    LocalizedText Explanation,
    PromotionCondition? Requires = null,
    Money? ShortBy = null,
    int? UnitsShort = null,
    LocalizedText? Winner = null);

/// <summary>
/// Why a promotion did not apply, as a closed set (ADR 0043): what a test
/// asserts and what an interface phrases its own way.
/// </summary>
public enum RuleReasonCode
{
    /// <summary>Another of its exclusivity group already took the slot.</summary>
    NotCombinableWith,

    /// <summary>A globally exclusive promotion closed the evaluation.</summary>
    SupersededByExclusive,

    /// <summary>The cart does not meet its condition.</summary>
    ConditionNotMet,

    /// <summary>There was nothing left to discount.</summary>
    NothingToDiscount,
}

/// <summary>
/// The reasons the engine knows how to give, as a table. Same idiom as
/// <c>Order.AllowedTransitions</c>: a reason from outside this list is a bug,
/// not a new string.
/// </summary>
public static class RuleReasons
{
    public const RuleReasonCode NotCombinableWithCode = RuleReasonCode.NotCombinableWith;
    public const RuleReasonCode SupersededByExclusiveCode = RuleReasonCode.SupersededByExclusive;
    public const RuleReasonCode ConditionNotMetCode = RuleReasonCode.ConditionNotMet;
    public const RuleReasonCode NothingToDiscountCode = RuleReasonCode.NothingToDiscount;

    /// <summary>Falls to the exclusivity group: another of its group already
    /// took the slot.</summary>
    public static RuleReason NotCombinableWith(LocalizedText winner) => new(
        NotCombinableWithCode,
        Both($"No acumulable con {winner.In("es")}.",
             $"Not combinable with {winner.In("en")}."),
        Winner: winner);

    /// <summary>Falls because a globally exclusive promotion closed the
    /// evaluation.</summary>
    public static RuleReason SupersededByExclusive(LocalizedText winner) => new(
        SupersededByExclusiveCode,
        Both($"{winner.In("es")} es exclusiva y no admite otras promociones.",
             $"{winner.In("en")} is exclusive and admits no other promotions."),
        Winner: winner);

    /// <summary>
    /// Falls because the cart does not meet its condition: the sentence, the
    /// condition itself, and how far the cart is from it.
    /// </summary>
    public static RuleReason ConditionNotMet(
        LocalizedText requirement, PromotionCondition requires, Money? shortBy = null, int? unitsShort = null) => new(
        ConditionNotMetCode,
        Both($"No se cumple la condición: {requirement.In("es")}.",
             $"Condition not met: {requirement.In("en")}."),
        requires, shortBy, unitsShort);

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
