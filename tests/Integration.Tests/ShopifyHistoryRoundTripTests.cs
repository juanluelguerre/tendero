using System.Text.Json;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Features.ProposePromotions;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ElGuerre.Tendero.Integration.Tests.ShopifyRoundTripTests;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The round trip of the lab store's people (phase 16, S2b, P16-16; spec
/// *Testing*): <c>tools/shopify-seed/history.mjs</c> wrote
/// <c>tools/shopify-seed/history.json</c> into the development store — five
/// customers, a dozen orders, two discounts, two gift cards — and each adapter
/// must hand back what that file says. Like the catalogue's, it runs only where
/// the store's credentials are, and is skipped everywhere else, CI included.
///
/// What the store holds differently from what was written is said here, by name:
/// <list type="bullet">
/// <item>an address an order was sent or billed to joins its customer's address
/// book, and is the default of a customer who had none;</item>
/// <item>an order written by <c>orderCreate</c> has no buyer's language, and its
/// fulfilment is dated the day it was written: neither can be set;</item>
/// <item>cancelling an order writes a refund of nothing for its lines.</item>
/// </list>
/// </summary>
public sealed class ShopifyHistoryRoundTripTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static History Seeded() =>
        JsonSerializer.Deserialize<History>(File.ReadAllText(Seed(Path.Combine("tools", "shopify-seed", "history.json"))), Json)!;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_lab_stores_customers_read_back_are_the_historys()
    {
        var secrets = StoreSecrets();
        Assert.SkipWhen(secrets is null, "needs a Shopify development store's credentials in the AppHost's user secrets");
        var ct = TestContext.Current.CancellationToken;
        var history = Seeded();

        await using var provider = Services(secrets!);
        await using var scope = provider.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredKeyedService<ICustomerSourceConnector>("shopify");
        var store = await Customers(source, ct);

        var differences = new List<string>();
        foreach (var expected in history.Customers)
        {
            if (!store.TryGetValue(expected.Email, out var actual))
            {
                differences.Add($"{expected.Email}: not in the store");
                continue;
            }

            void Same(string field, object? want, object? got)
            {
                if (!Equals(want, got))
                    differences.Add($"{expected.Email} {field}: history «{want}», store «{got}»");
            }

            Same("name", $"{expected.FirstName} {expected.LastName}", $"{actual.FirstName} {actual.LastName}");
            Same("language", expected.Locale, actual.Culture);
            Same("consent", expected.Marketing == "SUBSCRIBED", actual.Marketing?.Given ?? false);

            // The book as written, and every address an order of theirs went to.
            var recipient = $"{expected.FirstName} {expected.LastName}";
            var book = expected.Addresses
                .Concat(history.Orders.Where(order => order.Customer == expected.Key).SelectMany(order => new[] { order.ShippingAddress, order.BillingAddress }).OfType<HistoryAddress>())
                .Select(address => Line(recipient, address))
                .Distinct()
                .Order(StringComparer.Ordinal);
            Same("addresses", string.Join(" | ", book), string.Join(" | ", actual.Addresses.Select(Line).Order(StringComparer.Ordinal)));

            var first = expected.Addresses.FirstOrDefault()
                ?? history.Orders.First(order => order.Customer == expected.Key).ShippingAddress;
            Same("default address", first is null ? null : Line(recipient, first), actual.Addresses.SingleOrDefault(address => address.IsDefault) is { } chosen ? Line(chosen) : null);
        }

        Assert.True(differences.Count == 0, $"{differences.Count} differences:\n{string.Join('\n', differences)}");
        Assert.Equal(history.Customers.Count, store.Count);
        Assert.Empty(source.SourceNotes);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_lab_stores_orders_read_back_are_the_historys()
    {
        var secrets = StoreSecrets();
        Assert.SkipWhen(secrets is null, "needs a Shopify development store's credentials in the AppHost's user secrets");
        var ct = TestContext.Current.CancellationToken;
        var history = Seeded();

        await using var provider = Services(secrets!);
        await using var scope = provider.CreateAsyncScope();
        var prices = await CatalogueAsync(provider, scope, ct);
        var buyers = await Customers(scope.ServiceProvider.GetRequiredKeyedService<ICustomerSourceConnector>("shopify"), ct);

        var store = new List<SourceOrder>();
        await foreach (var order in scope.ServiceProvider.GetRequiredKeyedService<IOrderSourceConnector>("shopify").StreamOrdersAsync(ct))
            store.Add(order);

        var differences = new List<string>();
        foreach (var expected in history.Orders)
        {
            var actual = store.SingleOrDefault(order => order.PlacedAt == expected.PlacedAt);
            if (actual is null)
            {
                differences.Add($"{expected.Key}: no order placed at {expected.PlacedAt:O}");
                continue;
            }

            void Same(string field, object? want, object? got)
            {
                if (!Equals(want, got))
                    differences.Add($"{expected.Key} ({actual.Number}) {field}: history «{want}», store «{got}»");
            }

            var buyer = history.Customers.Single(customer => customer.Key == expected.Customer);
            var lines = expected.Lines.Select(line => (Sku: line.Sku.ToUpperInvariant(), line.Quantity, Price: prices[line.Sku.ToUpperInvariant()])).ToList();
            var subtotal = lines.Sum(line => line.Price * line.Quantity);
            var discount = expected.Discount is { } code
                ? lines.Sum(line => decimal.Round(line.Price * line.Quantity * code.Percentage / 100m, 2, MidpointRounding.AwayFromZero))
                : 0m;
            var shipping = decimal.Parse(expected.Shipping.Amount, System.Globalization.CultureInfo.InvariantCulture);
            var total = subtotal - discount + shipping;
            var tax = decimal.Round(total * 0.21m / 1.21m, 2, MidpointRounding.AwayFromZero);
            var recipient = $"{buyer.FirstName} {buyer.LastName}";
            var sentTo = expected.ShippingAddress ?? buyer.Addresses[0];

            Same("state", expected.State switch { "fulfilled" => SourceOrderState.Fulfilled, "cancelled" => SourceOrderState.Cancelled, _ => SourceOrderState.Open }, actual.State);
            Same("buyer", buyers[buyer.Email].ExternalId, actual.CustomerExternalId);
            Same("email", buyer.Email, actual.ContactEmail);
            Same("lines", string.Join(" | ", lines.Select(line => $"{line.Sku} x{line.Quantity} {line.Price:0.00}")),
                string.Join(" | ", actual.Lines.Select(line => $"{line.Sku?.ToUpperInvariant()} x{line.Quantity} {line.UnitPrice.Amount:0.00}")));
            Same("shipping", $"{expected.Shipping.Title} {shipping:0.00}", $"{actual.Shipping.Label} {actual.Shipping.Amount.Amount:0.00}");
            Same("totals", $"{subtotal:0.00} − {discount:0.00} + {shipping:0.00} = {total:0.00}, tax {tax:0.00}",
                $"{actual.Totals.Subtotal.Amount:0.00} − {actual.Totals.Discount.Amount:0.00} + {actual.Totals.Shipping.Amount:0.00} = {actual.Totals.Total.Amount:0.00}, tax {actual.Totals.Tax.Amount:0.00}");
            Same("discounts", expected.Discount is { } applied ? $"{applied.Code} {discount:0.00}" : "",
                string.Join(", ", actual.Discounts.Select(each => $"{each.Code} {each.Amount.Amount:0.00}")));
            Same("taxes", $"IVA 0.21 {tax:0.00}", string.Join(", ", actual.Taxes.Select(each => $"{each.Title} {each.Rate:0.00} {each.Amount.Amount:0.00}")));
            Same("sent to", Line(recipient, sentTo), actual.ShippingAddress is { } shipped ? Line(shipped) : null);
            Same("billed to", Line(recipient, expected.BillingAddress ?? sentTo), actual.BillingAddress is { } billed ? Line(billed) : null);
            Same("language", null, actual.Culture);
            Same("shipped", expected.State == "fulfilled", actual.ShippedAt is not null);

            var skuOf = actual.Lines.ToDictionary(line => line.ExternalId, line => line.Sku?.ToUpperInvariant());
            var refunds = expected.State == "cancelled"
                ? "0.00"
                : expected.Refund is { } refund
                    ? $"{refund.ProcessedAt:O} {refund.Amount} {string.Join(",", refund.Lines.Select(line => $"{line.Sku.ToUpperInvariant()} x{line.Quantity}"))}"
                    : "";
            Same("refunds", refunds, string.Join(" | ", actual.Refunds.Select(each => expected.State == "cancelled"
                ? $"{each.Amount.Amount:0.00}"
                : $"{each.At:O} {each.Amount.Amount:0.00} {string.Join(",", each.Lines.Select(line => $"{skuOf[line.LineExternalId]} x{line.Quantity}"))}")));
        }

        Assert.True(differences.Count == 0, $"{differences.Count} differences over {history.Orders.Count} orders:\n{string.Join('\n', differences)}");
        Assert.Equal(history.Orders.Count, store.Count);
    }

    /// <summary>
    /// The discounts read back, and the proposal they make (spec, *Testing*: the
    /// proposed promotions against the seeded discounts): the one the engine can
    /// express becomes its promotion, on the category its collection is; the other
    /// is left, said, for its limits.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_lab_stores_discounts_make_the_proposal_the_history_expects()
    {
        var secrets = StoreSecrets();
        Assert.SkipWhen(secrets is null, "needs a Shopify development store's credentials in the AppHost's user secrets");
        var ct = TestContext.Current.CancellationToken;
        var history = Seeded();

        await using var provider = Services(secrets!, services => services.AddSingleton<ISourceAddresses, CollectionsAsCategories>());
        await using var scope = provider.CreateAsyncScope();
        await CatalogueAsync(provider, scope, ct);
        var tree = await scope.ServiceProvider.GetRequiredService<ICategoryReader>().AllAsync(ct);
        var source = scope.ServiceProvider.GetRequiredKeyedService<IDiscountSourceConnector>("shopify");

        var rules = new List<SourceDiscountRule>();
        await foreach (var rule in source.StreamDiscountsAsync(ct))
            rules.Add(rule);

        var plants = history.Discounts.Single(discount => discount.Collection is not null);
        var category = tree.All.Single(each => Slugs.Of(each.Name.In("es")) == plants.Collection).Code;
        var proposable = Assert.Single(rules, rule => rule.Codes.SequenceEqual([plants.Code]));
        Assert.Equal(
            (plants.Title, "es", (SourceDiscountEffect)new SourcePercentOff(plants.Percentage), (SourceDiscountTarget)new CategoryItems(category), plants.StartsAt, SourceCombination.Everything),
            (proposable.Title, proposable.Language, proposable.Effect, proposable.Target, proposable.StartsAt, proposable.Combination));
        Assert.Empty(proposable.Restrictions);

        var vip = history.Discounts.Single(discount => discount.UsageLimit is not null);
        var limited = Assert.Single(rules, rule => rule.Codes.SequenceEqual([vip.Code]));
        Assert.Equal((new SourcePercentOff(vip.Percentage), new AllItems()), ((SourcePercentOff)limited.Effect, (AllItems)limited.Target));
        Assert.Equal([SourceDiscountRestriction.UsageLimit, SourceDiscountRestriction.OncePerCustomer], limited.Restrictions);
        Assert.Equal(history.Discounts.Count, rules.Count);

        var handler = new ProposePromotionsHandler(new OneSource(source), new NoPromotions(), new NoProposals());
        var proposal = await handler.HandleAsync(new ProposePromotionsCommand("shopify", "en", Rehearse: true), ct);

        var promotion = Assert.Single(proposal.Proposed);
        Assert.Equal(
            (plants.Code, "percent-off-line", CombinationPolicy.Stackable, plants.Code, category, (DateTimeOffset?)plants.StartsAt),
            (promotion.Code, promotion.Effect, promotion.Combination, promotion.CouponCode, promotion.CategoryCode, promotion.ValidFrom));
        Assert.Equal(1, proposal.Left);
        Assert.Equal((DiscountNoteCode.LimitNotExpressible, vip.Title), (Assert.Single(proposal.Notes).Code, proposal.Notes[0].About));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_lab_stores_gift_cards_read_back_are_the_historys()
    {
        var secrets = StoreSecrets();
        Assert.SkipWhen(secrets is null, "needs a Shopify development store's credentials in the AppHost's user secrets");
        var ct = TestContext.Current.CancellationToken;
        var history = Seeded();

        await using var provider = Services(secrets!);
        await using var scope = provider.CreateAsyncScope();
        var holders = (await Customers(scope.ServiceProvider.GetRequiredKeyedService<ICustomerSourceConnector>("shopify"), ct))
            .ToDictionary(pair => pair.Value.ExternalId, pair => history.Customers.Single(customer => customer.Email == pair.Key).Key);

        var cards = new List<SourceGiftCard>();
        await foreach (var card in scope.ServiceProvider.GetRequiredKeyedService<IGiftCardSourceConnector>("shopify").StreamGiftCardsAsync(ct))
            cards.Add(card);

        Assert.Equal(
            history.GiftCards.Select(card => $"{card.Amount} EUR {card.ExpiresOn?.ToString("yyyy-MM-dd") ?? "-"} {card.Customer ?? "-"}").Order(StringComparer.Ordinal),
            cards.Select(card => $"{card.Balance.Amount:0.00} {card.Balance.Currency} {card.ExpiresOn?.ToString("yyyy-MM-dd") ?? "-"} {(card.CustomerExternalId is { } id ? holders[id] : "-")}").Order(StringComparer.Ordinal));
        Assert.All(cards, card => Assert.Equal(4, card.LastCharacters.Length));
    }

    /// <summary>The store's customers by email.</summary>
    private static async Task<Dictionary<string, SourceCustomer>> Customers(ICustomerSourceConnector source, CancellationToken ct)
    {
        var customers = new Dictionary<string, SourceCustomer>(StringComparer.OrdinalIgnoreCase);
        await foreach (var customer in source.StreamCustomersAsync(ct))
            customers[customer.Email!] = customer;
        return customers;
    }

    /// <summary>The store's catalogue: its variants' SKUs given to the catalogue's answer, and each SKU's price.</summary>
    private static async Task<Dictionary<string, decimal>> CatalogueAsync(ServiceProvider provider, AsyncServiceScope scope, CancellationToken ct)
    {
        var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var product in await All(scope.ServiceProvider.GetRequiredKeyedService<ICatalogSourceConnector>("shopify"), ct))
            foreach (var variant in product.Variants ?? [])
            {
                var sku = variant.Sku ?? ExternalProductMapper.SkuOf(product, variant);
                provider.GetRequiredService<StoreSkus>().Known[variant.ExternalId!] = sku;
                prices[sku] = variant.PriceAmount ?? product.PriceAmount;
            }
        return prices;
    }

    private static string Line(string recipient, HistoryAddress address) =>
        $"{recipient}, {address.Address1}{(address.Address2 is { } more ? $" {more}" : "")}, {address.Zip} {address.City} ({address.ProvinceCode}) {address.CountryCode}{(address.Phone is { } phone ? $" {phone}" : "")}";

    private static string Line(SourceAddress address) =>
        $"{address.Recipient}, {address.Line1}{(address.Line2 is { } more ? $" {more}" : "")}, {address.PostalCode} {address.City} ({address.Region}) {address.CountryCode}{(address.Phone is { } phone ? $" {phone}" : "")}";

    private static string Line(Address address) =>
        $"{address.RecipientName}, {address.Line1}{(address.Line2 is { } more ? $" {more}" : "")}, {address.PostalCode} {address.City} ({address.Region}) {address.CountryCode}{(address.Phone is { } phone ? $" {phone}" : "")}";

    /// <summary>What the catalogue's import would keep each seeded collection as: the category it is named for.</summary>
    private sealed class CollectionsAsCategories(ICategoryReader categories) : ISourceAddresses
    {
        public Task<ProductAddress?> ProductAsync(string handle, CancellationToken ct) => Task.FromResult<ProductAddress?>(null);

        public async Task<string?> CategoryAsync(string collectionHandle, CancellationToken ct) =>
            (await categories.AllAsync(ct)).All.FirstOrDefault(category => Slugs.Of(category.Name.In("es")) == collectionHandle)?.Code;
    }

    private sealed class OneSource(IDiscountSourceConnector source) : IDiscountSourceRegistry
    {
        public IReadOnlyCollection<string> Sources => [source.Source];

        public IDiscountSourceConnector Get(string name) => source;
    }

    /// <summary>A shop with no promotions committed: the proposal starts at the first priority.</summary>
    private sealed class NoPromotions : IPromotionReader
    {
        public Task<IReadOnlyList<Promotion>> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Promotion>>([]);
    }

    /// <summary>A rehearsal writes nothing.</summary>
    private sealed class NoProposals : IPromotionProposals
    {
        public Task<string> WriteAsync(IReadOnlyList<Promotion> promotions, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A rehearsal writes no proposal.");
    }

    private sealed record History(
        List<HistoryCustomer> Customers, List<HistoryOrder> Orders, List<HistoryDiscount> Discounts, List<HistoryGiftCard> GiftCards);

    private sealed record HistoryCustomer(string Key, string FirstName, string LastName, string Email, string Locale, string Marketing, List<HistoryAddress> Addresses);

    private sealed record HistoryAddress(string Address1, string? Address2, string City, string ProvinceCode, string Zip, string CountryCode, string? Phone);

    private sealed record HistoryOrder(
        string Key, string Customer, DateTimeOffset PlacedAt, string State, List<HistoryLine> Lines, HistoryShipping Shipping,
        HistoryAddress? ShippingAddress, HistoryAddress? BillingAddress, HistoryRefund? Refund, HistoryOrderDiscount? Discount);

    private sealed record HistoryLine(string Sku, int Quantity);

    private sealed record HistoryShipping(string Title, string Amount);

    private sealed record HistoryRefund(DateTimeOffset ProcessedAt, List<HistoryLine> Lines, string Amount);

    private sealed record HistoryOrderDiscount(string Code, decimal Percentage);

    private sealed record HistoryDiscount(
        string Code, string Title, decimal Percentage, string? Collection, DateTimeOffset StartsAt, bool CombinesWithEverything, int? UsageLimit, bool AppliesOncePerCustomer);

    private sealed record HistoryGiftCard(string Key, string? Customer, string Amount, DateOnly? ExpiresOn);
}
