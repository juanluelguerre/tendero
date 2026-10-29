using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Shopify;

/// <summary>
/// The discounts port's Shopify adapter (phase 16, S2b, P16-19). It reads every
/// discount the store runs, a page at a time, and hands each over in the shop's
/// words:
///
/// - what it takes off: a percentage as a percentage (the store's is a fraction),
///   an amount with whether it comes off each item, a buy X get Y by its units,
///   free shipping; an app's discount is its program's, and named as such;
/// - what it reaches, as the catalogue names it: a collection as the category the
///   catalogue's import made it (<see cref="ISourceAddresses"/>), a product or a
///   variant as the SKU it sells under (<see cref="ISourceVariantSkus"/>);
///   anything else is said in the store's words;
/// - every restriction the store holds it to — a usage limit, once per customer,
///   some customers, some countries — so the proposal can say what it would lose.
///
/// Registered with the other Shopify adapters when the store's credentials are
/// configured.
/// </summary>
internal sealed class ShopifyDiscountConnector(
    ShopifyAdmin admin, ISourceVariantSkus catalogue, ISourceAddresses addresses) : IDiscountSourceConnector
{
    public const string Key = "shopify";

    public string Source => Key;

    public async IAsyncEnumerable<SourceDiscountRule> StreamDiscountsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var language = ShopifyLanguage.Of((await ShopifyShop.ReadAsync(admin, cancellationToken)).PrimaryLocale);

        string? after = null;
        do
        {
            var page = (await admin.QueryAsync(ShopifyQueries.Discounts(after), cancellationToken)).GetProperty("discountNodes");

            foreach (var node in page.GetProperty("nodes").EnumerateArray())
                yield return await DiscountAsync(node, language, cancellationToken);

            var info = page.GetProperty("pageInfo");
            after = info.GetProperty("hasNextPage").GetBoolean() ? info.GetProperty("endCursor").GetString() : null;
        }
        while (after is not null);
    }

    private async Task<SourceDiscountRule> DiscountAsync(JsonElement node, string language, CancellationToken ct)
    {
        var discount = node.GetProperty("discount");
        var gets = Present(discount, "customerGets");
        var buys = Present(discount, "customerBuys");
        var minimum = Present(discount, "minimumRequirement");

        var effect = EffectOf(discount, gets, buys);
        var target = effect is SourceFreeShipping or SourceOtherEffect || gets is null
            ? new AllItems()
            : await TargetAsync(gets.Value.GetProperty("items"), ct);

        return new SourceDiscountRule(
            ShopifyBulkProducts.IdOf(node.GetProperty("id").GetString()),
            Text(discount, "title") ?? "",
            language,
            Present(discount, "codes") is { } codes
                ? [.. codes.GetProperty("nodes").EnumerateArray().Select(code => Text(code, "code")).OfType<string>()]
                : [],
            effect,
            target,
            minimum is { } subtotal && Present(subtotal, "greaterThanOrEqualToSubtotal") is { } money ? MoneyOf(money) : null,
            minimum is { } quantity && Text(quantity, "greaterThanOrEqualToQuantity") is { } units ? int.Parse(units, CultureInfo.InvariantCulture) : null,
            At(discount, "startsAt") ?? DateTimeOffset.MinValue,
            At(discount, "endsAt"),
            Text(discount, "status") == "EXPIRED",
            CombinationOf(discount),
            RestrictionsOf(discount, gets));
    }

    private static SourceDiscountEffect EffectOf(JsonElement discount, JsonElement? gets, JsonElement? buys)
    {
        var kind = Text(discount, "__typename") ?? "";
        if (kind.EndsWith("FreeShipping", StringComparison.Ordinal))
            return new SourceFreeShipping();

        if (gets is not { } given || Present(given, "value") is not { } value)
            return new SourceOtherEffect(kind);

        if (Number(value, "percentage") is { } fraction)
            return new SourcePercentOff(fraction * 100m);

        if (Present(value, "amount") is { } amount)
            return new SourceAmountOff(MoneyOf(amount), value.TryGetProperty("appliesOnEachItem", out var each) && each.GetBoolean());

        // Buy X get Y: X units bought, Y units given at a percentage off. Bought by amount, or given at an amount off, is no case of the port's.
        if (buys is { } bought
            && Present(bought, "value") is { } paid && Text(paid, "quantity") is { } buy
            && Present(value, "quantity") is { } quantity && Text(quantity, "quantity") is { } get
            && Present(value, "effect") is { } off && Number(off, "percentage") is { } percent)
        {
            var same = bought.GetProperty("items").GetRawText() == given.GetProperty("items").GetRawText();
            return new SourceBuyXGetY(int.Parse(buy, CultureInfo.InvariantCulture), int.Parse(get, CultureInfo.InvariantCulture), percent * 100m, same);
        }

        return new SourceOtherEffect(kind);
    }

    /// <summary>What a discount reaches, as the catalogue names it; two of a kind are asked for, since a second is all it takes to say it cannot be one.</summary>
    private async Task<SourceDiscountTarget> TargetAsync(JsonElement items, CancellationToken ct)
    {
        if (items.TryGetProperty("allItems", out var all) && all.ValueKind == JsonValueKind.True)
            return new AllItems();

        if (Present(items, "collections") is { } collections)
        {
            var handles = collections.GetProperty("nodes").EnumerateArray().Select(collection => Text(collection, "handle")).OfType<string>().ToList();
            if (handles.Count != 1)
                return new UnplacedItems(string.Join(", ", handles));

            return await addresses.CategoryAsync(handles[0], ct) is { } category
                ? new CategoryItems(category)
                : new UnplacedItems(handles[0]);
        }

        var variants = new List<string>();
        if (Present(items, "productVariants") is { } chosen)
            variants.AddRange(chosen.GetProperty("nodes").EnumerateArray().Select(variant => ShopifyBulkProducts.IdOf(variant.GetProperty("id").GetString())));
        if (Present(items, "products") is { } products)
        {
            variants.AddRange(products.GetProperty("nodes").EnumerateArray()
                .SelectMany(product => product.GetProperty("variants").GetProperty("nodes").EnumerateArray())
                .Select(variant => ShopifyBulkProducts.IdOf(variant.GetProperty("id").GetString())));
        }

        if (variants.Count == 0)
            return new UnplacedItems("no items");

        var skus = await catalogue.SkusOfAsync(Key, variants, ct);
        var missing = variants.FirstOrDefault(variant => !skus.ContainsKey(variant));
        return missing is null
            ? new SkuItems([.. variants.Select(variant => skus[variant]).Distinct(StringComparer.Ordinal)])
            : new UnplacedItems($"variant {missing}");
    }

    /// <summary>With every kind, with none, or with some (Shopify's order, product and shipping classes).</summary>
    private static SourceCombination CombinationOf(JsonElement discount)
    {
        if (Present(discount, "combinesWith") is not { } combines)
            return SourceCombination.Nothing;

        var classes = new[] { "orderDiscounts", "productDiscounts", "shippingDiscounts" }
            .Count(kind => combines.TryGetProperty(kind, out var value) && value.ValueKind == JsonValueKind.True);

        return classes switch
        {
            0 => SourceCombination.Nothing,
            3 => SourceCombination.Everything,
            _ => SourceCombination.Some,
        };
    }

    /// <summary>Every restriction the store holds it to, in the order the store's fields name them.</summary>
    private static List<SourceDiscountRestriction> RestrictionsOf(JsonElement discount, JsonElement? gets)
    {
        var restrictions = new List<SourceDiscountRestriction>();

        if (discount.TryGetProperty("usageLimit", out var limit) && limit.ValueKind == JsonValueKind.Number)
            restrictions.Add(SourceDiscountRestriction.UsageLimit);
        if (discount.TryGetProperty("appliesOncePerCustomer", out var once) && once.ValueKind == JsonValueKind.True)
            restrictions.Add(SourceDiscountRestriction.OncePerCustomer);
        if (discount.TryGetProperty("usesPerOrderLimit", out var perOrder) && perOrder.ValueKind == JsonValueKind.Number)
            restrictions.Add(SourceDiscountRestriction.UsesPerOrder);
        // Anyone, or someone in particular; a context the API cannot name is not assumed to be anyone.
        if (Present(discount, "context") is { } context && Text(context, "__typename") != "DiscountBuyerSelectionAll")
            restrictions.Add(SourceDiscountRestriction.SomeCustomers);
        if (Present(discount, "destinationSelection") is { } destinations && Text(destinations, "__typename") == "DiscountCountries")
            restrictions.Add(SourceDiscountRestriction.SomeCountries);
        if (Present(discount, "maximumShippingPrice") is not null)
            restrictions.Add(SourceDiscountRestriction.MaximumShippingPrice);

        var oneTime = gets is { } given ? given : discount;
        if (oneTime.TryGetProperty("appliesOnOneTimePurchase", out var purchase) && purchase.ValueKind == JsonValueKind.False)
            restrictions.Add(SourceDiscountRestriction.SubscriptionsOnly);

        return restrictions;
    }

    private static Money MoneyOf(JsonElement money) =>
        new(decimal.Parse(money.GetProperty("amount").GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture),
            money.GetProperty("currencyCode").GetString()!);

    private static decimal? Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : null;

    private static DateTimeOffset? At(JsonElement element, string property) =>
        Text(element, property) is { } when ? DateTimeOffset.Parse(when, CultureInfo.InvariantCulture) : null;

    private static JsonElement? Present(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
