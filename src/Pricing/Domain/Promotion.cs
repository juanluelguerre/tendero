using System.Globalization;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Pricing.Domain;

/// <summary>
/// How a promotion lives alongside the others. This is the heart of the phase:
/// a promotions engine without combination rules is a list of discounts that add
/// up, and no real shop works that way.
/// </summary>
public enum CombinationPolicy
{
    /// <summary>Wins over everything below it and stops evaluation. What came
    /// above — higher priority — already applied and is respected.</summary>
    ExclusiveGlobal,

    /// <summary>Applies and silences the later ones sharing its exclusivity
    /// group. Outside the group it bothers nobody.</summary>
    ExclusiveInGroup,

    /// <summary>Applies and carries on. The only one that stacks.</summary>
    Stackable
}

/// <summary>What a promotion does. It is DATA: the arithmetic lives in the
/// engine, in four branches visible at once, rather than spread over four
/// classes.</summary>
public abstract record PromotionEffect
{
    /// <summary>A stable name, for the backoffice and for the UCP manifest.</summary>
    public abstract string Kind { get; }

    /// <summary>The effect as stable text, for the quote's fingerprint.</summary>
    public abstract string Canonical { get; }
}

/// <summary>A percentage off the lines the condition matches.</summary>
public sealed record PercentOffLine(decimal Percent) : PromotionEffect
{
    public override string Kind => "percent-off-line";
    public override string Canonical => $"{Kind}:{Percent.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// A flat amount off the order, spread across the lines. This is the effect
/// <c>Money.Allocate</c> had to exist for: splitting 10,00 € across three equal
/// lines loses a cent if it is improvised.
/// </summary>
public sealed record AmountOffOrder(Money Amount) : PromotionEffect
{
    public override string Kind => "amount-off-order";
    public override string Canonical =>
        $"{Kind}:{Amount.Amount.ToString(CultureInfo.InvariantCulture)}{Amount.Currency}";
}

/// <summary>Take <c>Buy</c> plus <c>Free</c>, pay for <c>Buy</c>.</summary>
public sealed record BuyXGetY(int Buy, int Free) : PromotionEffect
{
    public override string Kind => "buy-x-get-y";
    public override string Canonical => $"{Kind}:{Buy}/{Free}";
}

/// <summary>Free shipping. The only effect that does not touch a line price,
/// which is why shipping enters the engine as a value rather than being worked
/// out afterwards.</summary>
public sealed record FreeShipping : PromotionEffect
{
    public override string Kind => "free-shipping";
    public override string Canonical => Kind;
}

/// <summary>
/// When a promotion applies. Anything null does not restrict: an empty
/// condition means "always", which is what a welcome promotion wants.
/// </summary>
public sealed record PromotionCondition(
    Money? MinimumSubtotal = null,
    string? CategoryCode = null,
    string? Sku = null,
    int? MinimumQuantity = null)
{
    public static readonly PromotionCondition Always = new();

    /// <summary>Whether this particular line is in the effect's scope. A
    /// condition with neither category nor SKU reaches all of them.</summary>
    public bool Reaches(string sku, string? categoryPath)
    {
        if (Sku is not null && !string.Equals(Sku, sku, StringComparison.OrdinalIgnoreCase))
            return false;

        // The whole branch, not the leaf: a promotion on KITCHEN has to reach a
        // pan filed under COOKWARE. Same call that made the index store the full
        // path rather than the last segment, and the one that took the Spanish
        // NDCG from 0.860 to 0.943.
        if (CategoryCode is not null &&
            (categoryPath is null || !PathContains(categoryPath, CategoryCode)))
            return false;

        return true;
    }

    /// <summary>The condition as stable text, for the fingerprint.</summary>
    public string Canonical => string.Join(',',
        MinimumSubtotal is { } minimum
            ? $"min={minimum.Amount.ToString(CultureInfo.InvariantCulture)}{minimum.Currency}"
            : "min=-",
        $"cat={CategoryCode ?? "-"}",
        $"sku={Sku ?? "-"}",
        $"qty={MinimumQuantity?.ToString(CultureInfo.InvariantCulture) ?? "-"}");

    private static bool PathContains(string categoryPath, string code) =>
        categoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, code, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A promotion. A record and not an aggregate because in Pricing **nothing is
/// written**: the engine reads one and produces values. The day the backoffice
/// lets you create them, the aggregate is born here and this becomes its
/// snapshot.
/// </summary>
public sealed record Promotion(
    string Code,
    LocalizedText Name,
    PromotionCondition Condition,
    PromotionEffect Effect,
    CombinationPolicy Combination,
    int Priority,
    string? ExclusivityGroup = null,
    string? Segment = null,
    string? CouponCode = null,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidTo = null)
{
    public bool IsActiveAt(DateTimeOffset at) =>
        (ValidFrom is null || at >= ValidFrom) && (ValidTo is null || at < ValidTo);

    /// <summary>With no segment it serves anyone.</summary>
    public bool Serves(string segment) =>
        Segment is null || string.Equals(Segment, segment, StringComparison.OrdinalIgnoreCase);

    /// <summary>With no coupon it is automatic; with one, you have to bring
    /// it.</summary>
    public bool CouponSatisfiedBy(IReadOnlyCollection<string> coupons) =>
        CouponCode is null || coupons.Contains(CouponCode, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Everything that can change the amount, as stable text. It goes into the
    /// quote's fingerprint so that editing a promotion invalidates the prices it
    /// promised — the name stays out, because retranslating a label does not
    /// change what anyone pays.
    ///
    /// Built by hand with invariant formatting rather than through
    /// <c>ToString()</c>: a different decimal separator would produce a
    /// different fingerprint for the same data.
    /// </summary>
    public string Canonical() => string.Join('|',
        Code,
        Priority.ToString(CultureInfo.InvariantCulture),
        Combination,
        ExclusivityGroup ?? "-",
        Segment ?? "-",
        CouponCode ?? "-",
        ValidFrom?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) ?? "-",
        ValidTo?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) ?? "-",
        Effect.Canonical,
        Condition.Canonical);
}
