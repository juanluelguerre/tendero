using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>
/// Explicit builders, per the repository's testing rules: what a test does not
/// name is a documented default, never an anonymous value.
/// </summary>
internal static class Build
{
    public const string Eur = "EUR";

    public static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    public static Money Money(decimal amount) => new(amount, Eur);

    public static LocalizedText Text(string es, string? en = null) =>
        new(new Dictionary<string, string> { ["es"] = es, ["en"] = en ?? es });

    public static PricedLine Line(
        string sku, decimal unitPrice, int quantity = 1,
        string? categoryPath = null, string? taxClass = null) =>
        new(VariantId.New(), sku, quantity, Money(unitPrice), "catalog",
            categoryPath, taxClass, Money(0m));

    public static PricedCart Cart(decimal shipping = 0m, string segment = "retail", params PricedLine[] lines) =>
        new(Eur, segment, lines, Money(shipping));

    public static Promotion Promotion(
        string code,
        PromotionEffect effect,
        CombinationPolicy combination = CombinationPolicy.Stackable,
        int priority = 10,
        PromotionCondition? condition = null,
        string? group = null,
        string? segment = null,
        string? coupon = null,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validTo = null) =>
        new(code, Text(code), condition ?? PromotionCondition.Always, effect,
            combination, priority, group, segment, coupon, validFrom, validTo);

    public static PromotionContext At(DateTimeOffset? at = null, params string[] coupons) =>
        new(at ?? Now, coupons);
}
