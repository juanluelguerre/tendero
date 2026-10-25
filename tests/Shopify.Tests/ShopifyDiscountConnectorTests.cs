using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.Pricing.Tests.Sources;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Shopify.Tests;

/// <summary>The Shopify adapter of the discounts port passes the contract every discount source does.</summary>
public sealed class ShopifyDiscountConnectorContractTests : DiscountSourceConnectorContractTests
{
    protected override IDiscountSourceConnector CreateConnector() => ShopifyDiscountConnectorTests.AConnector();
}

/// <summary>
/// A store's discounts in the shop's words (phase 16, S2b, P16-19): every kind
/// the store runs, its percentage made a percentage (the store's is a fraction),
/// what it reaches as the catalogue names it — a collection as the category the
/// import made it, a variant as its SKU — and every restriction the store holds
/// it to, so the proposal can say what it would lose.
/// </summary>
public sealed class ShopifyDiscountConnectorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Money Eur(decimal amount) => new(amount, "EUR");

    internal static ShopifyDiscountConnector AConnector(FakeShopify? store = null)
    {
        store ??= new FakeShopify();
        var clock = new TestClock();
        var options = Options.Create(new ShopifyOptions
        {
            Store = FakeShopify.Store,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            BulkPollInterval = TimeSpan.Zero,
            ThrottleBackoff = TimeSpan.Zero,
        });
        var http = new HttpClient(store);
        var admin = new ShopifyAdmin(http, new ShopifyAccessTokens(http, options, clock), options, clock);
        return new ShopifyDiscountConnector(admin, new CatalogueSkus(), new ImportedCollections());
    }

    private static async Task<Dictionary<string, SourceDiscountRule>> Discounts()
    {
        var discounts = new Dictionary<string, SourceDiscountRule>(StringComparer.Ordinal);
        await foreach (var discount in AConnector().StreamDiscountsAsync(Ct))
            discounts[discount.Title] = discount;
        return discounts;
    }

    [Fact]
    public async Task A_code_discount_on_a_collection_reaches_the_category_the_collection_became()
    {
        var spring = (await Discounts())["Menaje de primavera"];

        Assert.Equal(("901", "es", new SourcePercentOff(15m)), (spring.ExternalId, spring.Language, spring.Effect));
        Assert.Equal(new CategoryItems("KITCHEN"), spring.Target);
        Assert.Equal(["SPRING15"], spring.Codes);
        Assert.Equal((SourceCombination.Nothing, false), (spring.Combination, spring.Expired));
        Assert.Equal((new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)), (spring.StartsAt, spring.EndsAt));
        Assert.Empty(spring.Restrictions);
    }

    [Fact]
    public async Task Automatic_free_shipping_comes_with_its_minimum_and_combines_with_everything()
    {
        var shipping = (await Discounts())["Envío gratis desde 60 €"];

        Assert.Equal((new SourceFreeShipping(), Eur(60m)), (shipping.Effect, shipping.MinimumSubtotal!.Value));
        Assert.Equal((SourceCombination.Everything, null), (shipping.Combination, shipping.EndsAt));
        Assert.Empty(shipping.Codes);
        Assert.Empty(shipping.Restrictions);
    }

    /// <summary>Every limit the store holds it to comes along, and a combination with some kinds is "some".</summary>
    [Fact]
    public async Task A_discounts_limits_come_with_it()
    {
        var welcome = (await Discounts())["Bienvenida"];

        Assert.Equal([SourceDiscountRestriction.UsageLimit, SourceDiscountRestriction.OncePerCustomer], welcome.Restrictions);
        Assert.Equal((new SourceAmountOff(Eur(5m), OnEachItem: false), 2, SourceCombination.Some), (welcome.Effect, welcome.MinimumQuantity!.Value, welcome.Combination));
        Assert.Equal(["HOLA5", "HELLO5"], welcome.Codes);
        Assert.Equal(new AllItems(), welcome.Target);
    }

    // The tests below, and the second page of the fixture, were found by phase 16
    // S2b's Stryker close: each mutant they name survived the suite above.

    /// <summary>Every page is read, the second after the first's cursor; the first is asked with none.</summary>
    [Fact]
    public async Task Every_page_is_read_the_next_after_the_cursor()
    {
        var store = new FakeShopify();

        var discounts = new List<SourceDiscountRule>();
        await foreach (var discount in AConnector(store).StreamDiscountsAsync(Ct))
            discounts.Add(discount);

        Assert.Equal(10, discounts.Count);
        var pages = store.Queries.Where(query => query.Contains("{ discountNodes(", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, pages.Count);
        Assert.Contains("{ discountNodes(first: 25) {", pages[0], StringComparison.Ordinal);
        Assert.Contains("after: \"d6\")", pages[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// A free shipping to some countries, up to a shipping price, for subscriptions
    /// only, and a deal with a limit per order: each restriction comes with it.
    /// </summary>
    [Fact]
    public async Task Every_restriction_the_store_holds_comes_with_it()
    {
        var discounts = await Discounts();

        Assert.Equal(
            [SourceDiscountRestriction.SomeCountries, SourceDiscountRestriction.MaximumShippingPrice, SourceDiscountRestriction.SubscriptionsOnly],
            discounts["Envío gratis a Portugal"].Restrictions);
        Assert.Equal([SourceDiscountRestriction.UsesPerOrder], discounts["Compra 50 € y llévate una taza"].Restrictions);
    }

    /// <summary>A deal bought by an amount is no case of the port's: its program's, and reaching everything rather than what it gives.</summary>
    [Fact]
    public async Task A_deal_bought_by_an_amount_is_named_and_reaches_nothing_in_particular()
    {
        var deal = (await Discounts())["Compra 50 € y llévate una taza"];

        Assert.Equal((new SourceOtherEffect("DiscountCodeBxgy"), new AllItems()), (deal.Effect, deal.Target));
    }

    /// <summary>An amount off each item says so; a variant the catalogue lacks, or two collections, are said in the store's words.</summary>
    [Fact]
    public async Task What_the_catalogue_cannot_place_is_said_in_the_stores_words()
    {
        var discounts = await Discounts();

        var each = discounts["2 € por taza"];
        Assert.Equal((new SourceAmountOff(Eur(2m), OnEachItem: true), new UnplacedItems("variant 44009")), (each.Effect, each.Target));
        Assert.Equal(new UnplacedItems("cocina, ofertas"), discounts["Cocina y ofertas"].Target);
    }

    [Fact]
    public async Task A_buy_x_get_y_is_bought_and_given_by_units()
    {
        var deal = (await Discounts())["3x2 en cafeteras"];

        Assert.Equal(new SourceBuyXGetY(2, 1, 100m, SameItems: true), deal.Effect);
        // No limit per order: the store answers null, and null is no limit.
        Assert.Empty(deal.Restrictions);
        Assert.Equal(["MOKA-OLV"], Assert.IsType<SkuItems>(deal.Target).Skus);
    }

    /// <summary>An app's discount is its program's arithmetic; an ended one, for some customers, on a product with two variants, is all that and said so.</summary>
    [Fact]
    public async Task What_the_store_cannot_hand_over_is_named()
    {
        var discounts = await Discounts();

        Assert.Equal(new SourceOtherEffect("DiscountAutomaticApp"), discounts["Descuento de una app"].Effect);
        var ended = discounts["Rebajas pasadas"];
        Assert.True(ended.Expired);
        Assert.Equal([SourceDiscountRestriction.SomeCustomers], ended.Restrictions);
        Assert.Equal(["MOKA-OLV", "MOKA-ROJ"], Assert.IsType<SkuItems>(ended.Target).Skus);
    }

    /// <summary>The catalogue's answer, as the store's catalogue was imported.</summary>
    private sealed class CatalogueSkus : ISourceVariantSkus
    {
        private static readonly Dictionary<string, string> Imported = new() { ["44001"] = "MOKA-OLV", ["44002"] = "MOKA-ROJ" };

        public Task<IReadOnlyDictionary<string, string>> SkusOfAsync(string source, IReadOnlyCollection<string> externalVariantIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                externalVariantIds.Where(Imported.ContainsKey).ToDictionary(id => id, id => Imported[id]));
    }

    /// <summary>The import made "cocina" the KITCHEN category, and nothing else.</summary>
    private sealed class ImportedCollections : ISourceAddresses
    {
        public Task<ProductAddress?> ProductAsync(string handle, CancellationToken ct) => Task.FromResult<ProductAddress?>(null);

        public Task<string?> CategoryAsync(string collectionHandle, CancellationToken ct) =>
            Task.FromResult(collectionHandle == "cocina" ? "KITCHEN" : null);
    }
}
