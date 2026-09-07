using ElGuerre.Tendero.Pricing.Adapters;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Engine;
using ElGuerre.Tendero.Pricing.Features.QuoteCart;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>
/// The quoting slice end to end, over deterministic in-memory fakes rather than
/// mocks — the ports have behaviour, and a fake with behaviour reads better than
/// four Returns() calls.
/// </summary>
public sealed class QuoteCartHandlerTests
{
    private readonly TestClock clock = new();
    private readonly FakeCatalogue catalogue = new();
    private readonly FakePriceLists priceLists = new();
    private readonly FakePromotions promotions = new();
    private CommercePrincipal principal = CommercePrincipal.Anonymous;

    private QuoteCartHandler Handler() => new(
        this.catalogue, this.priceLists, this.promotions,
        new PriceListResolver(), new CombiningPromotionEngine(),
        new FakeTaxRegistry(), new FakePrincipal(() => this.principal),
        Options.Create(new PricingSeedOptions()), this.clock);

    private Task<QuoteCartResult> Quote(
        string? segment = null, decimal? shipping = null, string[]? coupons = null,
        params (string Sku, int Quantity)[] lines) =>
        Handler().HandleAsync(
            new QuoteCartQuery("es", segment,
                [.. lines.Select(line => new QuoteLineInput(line.Sku, line.Quantity))],
                shipping, coupons ?? []),
            CancellationToken.None);

    // ---------- What the shop refuses ----------

    /// <summary>
    /// A total with a line missing from it is worse than an error, so the whole
    /// quote is refused and the offending SKUs are named. An agent retrying with
    /// a typo needs to know which one it got wrong.
    /// </summary>
    [Fact]
    public async Task A_sku_the_catalogue_does_not_have_is_named_rather_than_skipped()
    {
        var result = await Quote(lines: [("SHIRT", 1), ("GHOST", 1)]);

        Assert.Equal(QuoteOutcome.UnknownSkus, result.Outcome);
        Assert.Equal(["GHOST"], result.Offending);
        Assert.Null(result.Quote);
    }

    /// <summary>
    /// One currency per cart, and enforced rather than assumed. Multi-currency
    /// is deferred on purpose; deferring it silently is how a total ends up
    /// adding dollars to euros.
    /// </summary>
    [Fact]
    public async Task Two_currencies_in_one_cart_are_refused()
    {
        this.catalogue.Add("DOLLARS", new Money(10m, "USD"));

        var result = await Quote(lines: [("SHIRT", 1), ("DOLLARS", 1)]);

        Assert.Equal(QuoteOutcome.MixedCurrencies, result.Outcome);
        Assert.Equal(["EUR", "USD"], result.Offending);
    }

    // ---------- Segments ----------

    /// <summary>
    /// The security-shaped detail of the slice: if the segment came from the
    /// request body, anybody would ask for the VIP tariff. A guest gets the
    /// default no matter what they send.
    /// </summary>
    [Fact]
    public async Task A_guest_asking_for_the_vip_tariff_is_quoted_the_retail_one()
    {
        this.priceLists.Add("vip", "vip", 10, ("SHIRT", 10m));

        var result = await Quote(segment: "vip", lines: [("SHIRT", 1)]);

        Assert.Equal("retail", result.Quote!.Segment);
        Assert.Equal(24.50m, result.Quote.Lines.Single().UnitPrice.Amount);
    }

    [Fact]
    public async Task An_authenticated_customer_gets_the_segment_they_ask_for()
    {
        this.priceLists.Add("vip", "vip", 10, ("SHIRT", 10m));
        this.principal = CommercePrincipal.Anonymous with { Subject = "ana" };

        var result = await Quote(segment: "vip", lines: [("SHIRT", 1)]);

        Assert.Equal("vip", result.Quote!.Segment);
        Assert.Equal(10m, result.Quote.Lines.Single().UnitPrice.Amount);
        Assert.Equal("vip", result.Quote.Lines.Single().PriceSource);
    }

    // ---------- The whole sum ----------

    [Fact]
    public async Task A_quote_adds_up_lines_discounts_shipping_and_tax()
    {
        this.promotions.Add(Build.Promotion("TEN", new PercentOffLine(10m)));

        var result = await Quote(shipping: 4.95m, lines: [("SHIRT", 2)]);
        var quote = result.Quote!;

        Assert.Equal(49.00m, quote.Subtotal.Amount);
        Assert.Equal(4.90m, quote.DiscountTotal.Amount);
        Assert.Equal(4.95m, quote.Shipping.Amount);

        // Shipping is taxed as a base like any other: (49,00 - 4,90 + 4,95) is
        // 49,05, and 21 % of that is 10,3005 — 10,30 once rounded, one single
        // time, at the end.
        Assert.Equal(10.30m, quote.TaxTotal.Amount);
        Assert.Equal(59.35m, quote.Total.Amount);
    }

    [Fact]
    public async Task A_suppressed_promotion_travels_with_the_quote_and_its_reason()
    {
        this.promotions.Add(
            Build.Promotion(
                "BIG", new PercentOffLine(25m),
                CombinationPolicy.ExclusiveInGroup, priority: 1, group: "seasonal"));
        this.promotions.Add(
            Build.Promotion(
                "SMALL", new PercentOffLine(10m),
                CombinationPolicy.ExclusiveInGroup, priority: 2, group: "seasonal"));

        var quote = (await Quote(lines: [("SHIRT", 1)])).Quote!;

        var suppressed = quote.Discounts.Single(
            discount => discount.Outcome == DiscountOutcome.Suppressed);

        Assert.Equal("SMALL", suppressed.PromotionCode);
        Assert.Equal(RuleReasons.NotCombinableWithCode, suppressed.Reason!.Code);
    }

    // ---------- The fingerprint ----------

    [Fact]
    public async Task The_same_cart_quoted_twice_carries_the_same_fingerprint()
    {
        var first = (await Quote(lines: [("SHIRT", 2)])).Quote!;
        var second = (await Quote(lines: [("SHIRT", 2)])).Quote!;

        Assert.Equal(first.InputHash, second.InputHash);

        // The id is not the fingerprint: two quotes of the same cart are two
        // quotes, each with its own expiry.
        Assert.NotEqual(first.QuoteId, second.QuoteId);
    }

    [Fact]
    public async Task Changing_a_quantity_changes_the_fingerprint()
    {
        var one = (await Quote(lines: [("SHIRT", 1)])).Quote!;
        var two = (await Quote(lines: [("SHIRT", 2)])).Quote!;

        Assert.NotEqual(one.InputHash, two.InputHash);
    }

    /// <summary>
    /// The half that is easy to forget: the fingerprint covers the DATA the shop
    /// answered with, not only what the shopper asked. Without it, dropping a
    /// price in the backoffice would leave live quotes promising the old one and
    /// checkout revalidation would have no way to notice.
    /// </summary>
    [Fact]
    public async Task Changing_a_tariff_changes_the_fingerprint_of_the_same_cart()
    {
        var before = (await Quote(lines: [("SHIRT", 1)])).Quote!;

        this.priceLists.Add("retail", "retail", 100, ("SHIRT", 19.90m));

        var after = (await Quote(lines: [("SHIRT", 1)])).Quote!;

        Assert.NotEqual(before.InputHash, after.InputHash);
    }

    [Fact]
    public async Task Changing_a_promotion_changes_the_fingerprint_of_the_same_cart()
    {
        var before = (await Quote(lines: [("SHIRT", 1)])).Quote!;

        this.promotions.Add(Build.Promotion("NEW", new PercentOffLine(5m)));

        var after = (await Quote(lines: [("SHIRT", 1)])).Quote!;

        Assert.NotEqual(before.InputHash, after.InputHash);
    }

    [Fact]
    public async Task A_quote_expires_and_says_which_of_the_two_things_went_wrong()
    {
        var quote = (await Quote(lines: [("SHIRT", 1)])).Quote!;

        Assert.Equal(QuoteValidity.Valid, quote.ValidateAt(this.clock.GetUtcNow(), quote.InputHash));
        Assert.Equal(QuoteValidity.InputsChanged, quote.ValidateAt(this.clock.GetUtcNow(), "something-else"));

        var later = this.clock.Advance(PriceQuote.Lifetime + TimeSpan.FromSeconds(1));
        Assert.Equal(QuoteValidity.Expired, quote.ValidateAt(later, quote.InputHash));
    }

    // ---------- Fakes ----------

    private sealed class FakeCatalogue : IPricedItemReader
    {
        private readonly Dictionary<string, PricedItem> items = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SHIRT"] = new(
                VariantId.New(), ProductId.New(), "SHIRT",
                new Money(24.50m, "EUR"), "APPAREL/SPORTSWEAR/SHIRT", null),
            ["PAN"] = new(
                VariantId.New(), ProductId.New(), "PAN",
                new Money(44.95m, "EUR"), "HOME/KITCHEN/COOKWARE", null)
        };

        public void Add(string sku, Money price) =>
            this.items[sku] = new PricedItem(VariantId.New(), ProductId.New(), sku, price, null, null);

        public Task<IReadOnlyList<PricedItem>> BySkusAsync(
            IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PricedItem>>(
                [.. skus.Where(this.items.ContainsKey).Select(sku => this.items[sku])]);
    }

    private sealed class FakePriceLists : IPriceListReader
    {
        private readonly List<PriceList> lists = [];

        public void Add(string code, string segment, int priority, params (string Sku, decimal Amount)[] entries) =>
            this.lists.Add(
                new PriceList(
                    code, segment, "EUR", priority,
                    [.. entries.Select(entry => new PriceListEntry(entry.Sku, new Money(entry.Amount, "EUR")))]));

        public Task<PriceBook> BookAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PriceBook([.. this.lists]));
    }

    private sealed class FakePromotions : IPromotionReader
    {
        private readonly List<Promotion> promotions = [];

        public void Add(Promotion promotion) => this.promotions.Add(promotion);

        public Task<IReadOnlyList<Promotion>> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Promotion>>([.. this.promotions]);
    }

    private sealed class FakeTaxRegistry : ITaxCalculatorRegistry
    {
        private readonly ITaxCalculator calculator = new FlatVatTaxCalculator(Options.Create(new FlatVatOptions()));

        public IReadOnlyCollection<string> Keys => [FlatVatTaxCalculator.Key];

        public ITaxCalculator Get(string key) => this.calculator;
    }

    private sealed class FakePrincipal(Func<CommercePrincipal> current) : IPrincipalAccessor
    {
        public CommercePrincipal Current => current();
    }
}
