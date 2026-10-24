using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Pricing.Ports;

/// <summary>
/// The discounts' inbound port (phase 16, S2b, P16-19): a store a merchant is
/// leaving hands over the discounts it runs, in the shop's words — what each
/// takes off, what it reaches as the catalogue names it (a category, a SKU), and
/// every restriction the store holds it to, so that whoever translates it knows
/// what it would lose. Each source registers keyed by name, as the other ports
/// do, and the ProposePromotions slice knows only this interface.
/// </summary>
public interface IDiscountSourceConnector
{
    /// <summary>A stable lowercase identifier: "shopify"…</summary>
    string Source { get; }

    IAsyncEnumerable<SourceDiscountRule> StreamDiscountsAsync(CancellationToken cancellationToken = default);
}

/// <param name="ExternalId">The source's id for the discount.</param>
/// <param name="Title">What the merchant called it, in <paramref name="Language"/>.</param>
/// <param name="Codes">The codes a buyer types; none for an automatic discount.</param>
/// <param name="Target">What it reaches: what it takes off from, or for a buy X get Y what is given.</param>
/// <param name="Expired">Whether it has ended at the source: nothing to propose.</param>
/// <param name="Restrictions">What the source holds it to beyond the rest: the engine has no field for any of them.</param>
public sealed record SourceDiscountRule(
    string ExternalId,
    string Title,
    string Language,
    IReadOnlyList<string> Codes,
    SourceDiscountEffect Effect,
    SourceDiscountTarget Target,
    Money? MinimumSubtotal,
    int? MinimumQuantity,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    bool Expired,
    SourceCombination Combination,
    IReadOnlyList<SourceDiscountRestriction> Restrictions);

/// <summary>What a discount takes off. Closed: a switch names every case.</summary>
public closed record SourceDiscountEffect;

/// <param name="Percent">From 0 to 100.</param>
public sealed record SourcePercentOff(decimal Percent) : SourceDiscountEffect;

/// <param name="OnEachItem">Whether the amount comes off every item reached, rather than once, spread across them.</param>
public sealed record SourceAmountOff(Money Amount, bool OnEachItem) : SourceDiscountEffect;

/// <param name="PercentOffGiven">What comes off the items given, from 0 to 100: 100 is free.</param>
/// <param name="SameItems">Whether what is bought and what is given are the same items.</param>
public sealed record SourceBuyXGetY(int Buy, int Get, decimal PercentOffGiven, bool SameItems) : SourceDiscountEffect;

public sealed record SourceFreeShipping : SourceDiscountEffect;

/// <summary>An effect the port has no case for: one another program computes (a Shopify app's function), or a buy X get Y bought by amount rather than by units.</summary>
public sealed record SourceOtherEffect(string Kind) : SourceDiscountEffect;

/// <summary>What a discount reaches, as the catalogue names it. Closed.</summary>
public closed record SourceDiscountTarget;

public sealed record AllItems : SourceDiscountTarget;

/// <summary>A category of the catalogue: what the store's collection became when it was imported.</summary>
public sealed record CategoryItems(string CategoryCode) : SourceDiscountTarget;

/// <summary>The SKUs the catalogue sells the store's products or variants under.</summary>
public sealed record SkuItems(IReadOnlyList<string> Skus) : SourceDiscountTarget;

/// <summary>Something the catalogue cannot name: a collection no category is, a product never imported, several collections.</summary>
public sealed record UnplacedItems(string What) : SourceDiscountTarget;

/// <summary>Which other discounts it may apply with, at the source.</summary>
public enum SourceCombination
{
    /// <summary>With none: it applies alone.</summary>
    Nothing,

    /// <summary>With some kinds and not others (Shopify's order, product and shipping classes).</summary>
    Some,

    /// <summary>With every kind.</summary>
    Everything,
}

/// <summary>A restriction the store holds a discount to that the promotions engine has no field for.</summary>
public enum SourceDiscountRestriction
{
    /// <summary>It may be used so many times in all.</summary>
    UsageLimit,

    /// <summary>Each customer may use it once.</summary>
    OncePerCustomer,

    /// <summary>It applies so many times in one order at most.</summary>
    UsesPerOrder,

    /// <summary>Only some customers, segments or markets get it.</summary>
    SomeCustomers,

    /// <summary>Only some shipping countries get it.</summary>
    SomeCountries,

    /// <summary>Only shipping up to some price is made free.</summary>
    MaximumShippingPrice,

    /// <summary>Only subscriptions get it.</summary>
    SubscriptionsOnly,
}

/// <summary>Why a discount was proposed differently from the store's, or not at all.</summary>
public enum DiscountNoteCode
{
    /// <summary>It has ended at the source: not proposed.</summary>
    Expired,

    /// <summary>A promotion with its code is already committed: not proposed.</summary>
    AlreadyHere,

    /// <summary>A limit on its use (in all, per customer, per order) the engine cannot hold: not proposed, since without it the discount would give away more.</summary>
    LimitNotExpressible,

    /// <summary>Only some customers get it, and the store's customers are not the shop's segments: not proposed.</summary>
    CustomersNotExpressible,

    /// <summary>A condition the engine has no field for (shipping countries, a ceiling on the shipping, subscriptions): not proposed.</summary>
    ConditionNotExpressible,

    /// <summary>What it reaches cannot be named as one category or one SKU: not proposed.</summary>
    TargetNotExpressible,

    /// <summary>What it takes off is not one of the engine's effects: not proposed.</summary>
    EffectNotExpressible,

    /// <summary>It combined with some kinds of discount; the proposal applies it alone. Proposed.</summary>
    CombinationNarrowed,

    /// <summary>It had several codes; the proposal keeps the first. Proposed.</summary>
    CodesNarrowed,
}

/// <param name="About">The discount's title at the source.</param>
public sealed record DiscountNote(DiscountNoteCode Code, string About, string? Detail);

/// <summary>The discount sources registered, by name; the only place that talks to the container.</summary>
public interface IDiscountSourceRegistry
{
    IReadOnlyCollection<string> Sources { get; }

    IDiscountSourceConnector Get(string source);
}

/// <summary>
/// Where a proposal of promotions is written for a person to read (phase 16,
/// P16-19): beside the committed promotions, in their shape, never over them.
/// Pricing still has no writer of promotions; this writes a file nobody reads
/// until a person commits it.
/// </summary>
public interface IPromotionProposals
{
    /// <returns>The name of the file written.</returns>
    Task<string> WriteAsync(IReadOnlyList<Domain.Promotion> promotions, CancellationToken cancellationToken = default);
}
