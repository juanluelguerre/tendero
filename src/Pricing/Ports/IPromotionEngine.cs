using ElGuerre.Tendero.Pricing.Domain;

namespace ElGuerre.Tendero.Pricing.Ports;

/// <summary>
/// The context of one evaluation: the instant the validity windows are measured
/// against, and the coupons the shopper brought.
///
/// The instant travels as a VALUE rather than as a <c>TimeProvider</c> injected
/// into the engine. That is purer and more checkable: a property about "expired
/// promotions do not apply" can generate instants instead of wiring a clock.
/// </summary>
public sealed record PromotionContext(DateTimeOffset At, IReadOnlyCollection<string> Coupons);

/// <summary>
/// The discounted cart plus the COMPLETE list of what was evaluated — applied
/// and suppressed. Returning only what applied would throw away the information
/// this phase exists for.
/// </summary>
public sealed record PromotionOutcome(PricedCart Cart, IReadOnlyList<AppliedDiscount> Discounts);

/// <summary>
/// Applies the combination rules to an already-priced cart.
///
/// Pure: promotions and cart in, cart and explanations out. No database, no
/// clock, no HTTP — which is the only way the properties in P3-10 (the total is
/// never negative, two exclusives of one group never coexist, input order does
/// not change the result) can be written at all.
/// </summary>
public interface IPromotionEngine
{
    PromotionOutcome Apply(
        IReadOnlyList<Promotion> promotions, PricedCart cart, PromotionContext context);
}
