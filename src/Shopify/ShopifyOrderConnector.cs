using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Shopify;

/// <summary>
/// The orders port's Shopify adapter (phase 16, S2b, P16-18). It reads the
/// store's orders a page at a time, oldest first, and hands each over in the
/// shop's words:
///
/// - a line is the SKU the catalogue sells its variant under, asked of the
///   catalogue (<see cref="ISourceVariantSkus"/>) as the stock's are, with the
///   product's and the variant's names as the buyer saw them; a line whose
///   variant the store deleted, or the catalogue never imported, has none;
/// - the figures are an order's: the subtotal before discounts (the store's is
///   after them), the shipping before its own, and each discount what it took
///   off this order, added up from its allocations;
/// - an order is cancelled when the store cancelled it, fulfilled when all of it
///   left, and open otherwise; a fulfilled one shipped with its last fulfilment
///   (or when it was placed, for one the store marked fulfilled with none);
/// - a refund names the lines it took back by the store's ids for them.
///
/// Registered with the other Shopify adapters when the store's credentials are
/// configured.
/// </summary>
internal sealed class ShopifyOrderConnector(ShopifyAdmin admin, ISourceVariantSkus catalogue) : IOrderSourceConnector, IChannelOrderSource
{
    public const string Key = "shopify";

    public string Source => Key;

    public string Channel => Key;

    public IAsyncEnumerable<SourceOrder> StreamOrdersAsync(CancellationToken cancellationToken = default) =>
        PagesAsync(ShopifyQueries.Orders, StoreSkus.Distrusted, cancellationToken);

    /// <summary>The store as a sales channel (S3): its orders changed since the reconciliation's mark.</summary>
    public IAsyncEnumerable<SourceOrder> UpdatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
        PagesAsync(after => ShopifyQueries.OrdersUpdatedSince(since, after), StoreSkus.Trusted, cancellationToken);

    /// <summary>One order, by the id a webhook names (S3): read with the same fields and mapping as the rest.</summary>
    public async Task<SourceOrder?> FindAsync(string externalId, CancellationToken cancellationToken = default)
    {
        var id = externalId.StartsWith("gid://", StringComparison.Ordinal) ? externalId : $"gid://shopify/Order/{externalId}";
        var order = (await admin.QueryAsync(ShopifyQueries.Order(id), cancellationToken)).GetProperty("order");
        if (order.ValueKind != JsonValueKind.Object)
            return null;

        return Order(order, await SkusAsync([order], StoreSkus.Trusted, cancellationToken));
    }

    /// <summary>
    /// Whether a line the catalogue never imported is read by the SKU its variant has in
    /// the store. Leaving Shopify (S2) the store's SKU is not ours — it may be shared or
    /// missing — so only the imported variants count. As a channel (S3) the store holds
    /// the SKUs Tendero published to it, the identity the two share (ADR 0070): an order
    /// for a product Tendero published, never imported, was skipped on the lab store's
    /// round trip (2026-11-03) as not in the catalogue.
    /// </summary>
    private enum StoreSkus
    {
        Distrusted,
        Trusted,
    }

    private async IAsyncEnumerable<SourceOrder> PagesAsync(
        Func<string?, string> query, StoreSkus storeSkus, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? after = null;
        do
        {
            var page = (await admin.QueryAsync(query(after), cancellationToken)).GetProperty("orders");
            var orders = page.GetProperty("nodes").EnumerateArray().ToList();
            var skus = await SkusAsync(orders, storeSkus, cancellationToken);

            foreach (var order in orders)
                yield return Order(order, skus);

            var info = page.GetProperty("pageInfo");
            after = info.GetProperty("hasNextPage").GetBoolean() ? info.GetProperty("endCursor").GetString() : null;
        }
        while (after is not null);
    }

    private async Task<IReadOnlyDictionary<string, string>> SkusAsync(
        IReadOnlyList<JsonElement> orders, StoreSkus storeSkus, CancellationToken cancellationToken)
    {
        var lines = orders.SelectMany(order => order.GetProperty("lineItems").GetProperty("nodes").EnumerateArray()).ToList();
        var variants = lines.Select(VariantOf).OfType<string>().Distinct(StringComparer.Ordinal).ToList();

        var imported = variants.Count == 0
            ? new Dictionary<string, string>()
            : await catalogue.SkusOfAsync(Key, variants, cancellationToken);
        if (storeSkus == StoreSkus.Distrusted)
            return imported;

        var skus = new Dictionary<string, string>(imported, StringComparer.Ordinal);
        foreach (var line in lines)
            if (VariantOf(line) is { } variant && !skus.ContainsKey(variant)
                && Present(line, "variant") is { } held && Text(held, "sku") is { Length: > 0 } sku)
                skus[variant] = sku;
        return skus;
    }

    private static SourceOrder Order(JsonElement order, IReadOnlyDictionary<string, string> skus)
    {
        var total = MoneyOf(order.GetProperty("totalPriceSet"));
        var currency = total.Currency;
        var lines = order.GetProperty("lineItems").GetProperty("nodes").EnumerateArray().ToList();
        var shippingLine = Present(order, "shippingLine");
        var state = StateOf(order);

        var ordered = lines.Select(line => new SourceOrderLine(
            ShopifyBulkProducts.IdOf(line.GetProperty("id").GetString()),
            VariantOf(line) is { } variant ? skus.GetValueOrDefault(variant) : null,
            Text(line, "title") ?? "",
            Text(line, "variantTitle"),
            MoneyOf(line.GetProperty("originalUnitPriceSet")),
            line.GetProperty("quantity").GetInt32())).ToList();

        var shipping = shippingLine is { } shipped
            ? new SourceShipping(Text(shipped, "title") ?? "", MoneyOf(shipped.GetProperty("originalPriceSet")))
            : new SourceShipping("", new Money(0m, currency));

        return new SourceOrder(
            ShopifyBulkProducts.IdOf(order.GetProperty("id").GetString()),
            Text(order, "name") ?? "",
            Present(order, "customer") is { } customer ? ShopifyBulkProducts.IdOf(customer.GetProperty("id").GetString()) : null,
            Text(order, "email"),
            ShopifyLanguage.SpokenOf(Text(order, "customerLocale")),
            At(order, "processedAt")!.Value,
            state == SourceOrderState.Fulfilled ? LastFulfilment(order) ?? At(order, "processedAt") : null,
            state,
            ordered,
            AddressOf(Present(order, "shippingAddress")),
            AddressOf(Present(order, "billingAddress")),
            shipping,
            new SourceOrderTotals(
                new Money(ordered.Sum(line => line.UnitPrice.Amount * line.Quantity), currency),
                MoneyOf(order.GetProperty("totalDiscountsSet")),
                shipping.Amount,
                MoneyOf(order.GetProperty("totalTaxSet")),
                total),
            DiscountsOf(order, lines, shippingLine, currency),
            [
                .. order.GetProperty("taxLines").EnumerateArray().Select(tax => new SourceTax(
                    Text(tax, "title") ?? "", tax.GetProperty("rate").GetDecimal(), MoneyOf(tax.GetProperty("priceSet")))),
            ],
            [.. order.GetProperty("refunds").EnumerateArray().Select(Refund)],
            At(order, "updatedAt"));
    }

    /// <summary>Cancelled when the store cancelled it; fulfilled only when all of it left; anything else is still on its way.</summary>
    private static SourceOrderState StateOf(JsonElement order) =>
        At(order, "cancelledAt") is not null ? SourceOrderState.Cancelled
        : Text(order, "displayFulfillmentStatus") == "FULFILLED" ? SourceOrderState.Fulfilled
        : SourceOrderState.Open;

    private static DateTimeOffset? LastFulfilment(JsonElement order) =>
        order.GetProperty("fulfillments").EnumerateArray().Select(fulfilment => At(fulfilment, "createdAt")).Max();

    /// <summary>
    /// Each discount applied, with what it took off: its allocations on the lines
    /// and on the shipping, added up by the application's index. Named by its
    /// code, or by its title when it had none (automatic, manual).
    /// </summary>
    private static List<SourceDiscount> DiscountsOf(JsonElement order, List<JsonElement> lines, JsonElement? shippingLine, string currency)
    {
        var taken = lines.Append(shippingLine ?? default)
            .Where(owner => owner.ValueKind == JsonValueKind.Object)
            .SelectMany(owner => owner.GetProperty("discountAllocations").EnumerateArray())
            .GroupBy(allocation => allocation.GetProperty("discountApplication").GetProperty("index").GetInt32())
            .ToDictionary(group => group.Key, group => group.Sum(allocation => MoneyOf(allocation.GetProperty("allocatedAmountSet")).Amount));

        return
        [
            .. order.GetProperty("discountApplications").GetProperty("nodes").EnumerateArray()
                .Select(application => (Index: application.GetProperty("index").GetInt32(), Name: Text(application, "code") ?? Text(application, "title") ?? ""))
                .Where(application => taken.ContainsKey(application.Index))
                .Select(application => new SourceDiscount(application.Name, application.Name, new Money(taken[application.Index], currency))),
        ];
    }

    private static SourceRefund Refund(JsonElement refund) => new(
        ShopifyBulkProducts.IdOf(refund.GetProperty("id").GetString()),
        At(refund, "processedAt")!.Value,
        MoneyOf(refund.GetProperty("totalRefundedSet")),
        [
            .. refund.GetProperty("refundLineItems").GetProperty("nodes").EnumerateArray().Select(line => (
                ShopifyBulkProducts.IdOf(line.GetProperty("lineItem").GetProperty("id").GetString()),
                line.GetProperty("quantity").GetInt32())),
        ]);

    /// <summary>An address the shop can post to, or none: one missing a street or a city is not guessed at.</summary>
    private static Address? AddressOf(JsonElement? address)
    {
        if (address is not { } known
            || Text(known, "name") is not { Length: > 0 } name
            || Text(known, "address1") is not { Length: > 0 } line1
            || Text(known, "city") is not { Length: > 0 } city
            || Text(known, "zip") is not { Length: > 0 } zip
            || Text(known, "countryCodeV2") is not { Length: 2 } country)
            return null;

        return Address.Create(name, line1, Text(known, "address2"), city, Text(known, "provinceCode"), zip, country, Text(known, "phone"));
    }

    private static string? VariantOf(JsonElement line) =>
        Present(line, "variant") is { } variant ? ShopifyBulkProducts.IdOf(variant.GetProperty("id").GetString()) : null;

    /// <summary>The figure in the store's currency; the buyer's presentment currency is not the shop's books.</summary>
    private static Money MoneyOf(JsonElement set)
    {
        var money = set.GetProperty("shopMoney");
        return new Money(
            decimal.Parse(money.GetProperty("amount").GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture),
            money.GetProperty("currencyCode").GetString()!);
    }

    private static DateTimeOffset? At(JsonElement element, string property) =>
        Text(element, property) is { } when ? DateTimeOffset.Parse(when, CultureInfo.InvariantCulture) : null;

    private static JsonElement? Present(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
