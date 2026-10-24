using System.Runtime.CompilerServices;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Features.ProposePromotions;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.Pricing.Tests.Sources;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Pricing.Tests;

/// <summary>A discount source written in memory: the slice's control.</summary>
internal sealed class FakeDiscountSource : IDiscountSourceConnector
{
    public const string Key = "fake";

    public static readonly DateTimeOffset Spring = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private static Money Eur(decimal amount) => new(amount, "EUR");

    public static SourceDiscountRule Rule(
        string id, string title, SourceDiscountEffect? effect = null, SourceDiscountTarget? target = null,
        IReadOnlyList<string>? codes = null, decimal? minimumSubtotal = null, int? minimumQuantity = null,
        DateTimeOffset? endsAt = null, bool expired = false, SourceCombination combination = SourceCombination.Nothing,
        IReadOnlyList<SourceDiscountRestriction>? restrictions = null) =>
        new(id, title, "es", codes ?? [], effect ?? new SourcePercentOff(10m), target ?? new AllItems(),
            minimumSubtotal is { } minimum ? Eur(minimum) : null, minimumQuantity, Spring, endsAt, expired, combination, restrictions ?? []);

    public List<SourceDiscountRule> Discounts { get; } =
    [
        Rule("1", "Menaje de primavera", new SourcePercentOff(15m), new CategoryItems("KITCHEN"), codes: ["SPRING15"],
            endsAt: Spring.AddMonths(3)),
        Rule("2", "Envío gratis desde 60 €", new SourceFreeShipping(), minimumSubtotal: 60m, combination: SourceCombination.Everything),
        Rule("3", "Bienvenida", new SourceAmountOff(Eur(5m), OnEachItem: false), codes: ["HOLA5"],
            restrictions: [SourceDiscountRestriction.OncePerCustomer]),
    ];

    public string Source => Key;

    public async IAsyncEnumerable<SourceDiscountRule> StreamDiscountsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        foreach (var discount in Discounts)
            yield return discount;
    }
}

/// <summary>The fake discount source passes the contract every discount source does.</summary>
public sealed class FakeDiscountSourceContractTests : DiscountSourceConnectorContractTests
{
    protected override IDiscountSourceConnector CreateConnector() => new FakeDiscountSource();
}

/// <summary>
/// A store's discounts as a proposal (phase 16, S2b, P16-19; spec decision 5):
/// each one the engine can express becomes an entry of a promotions file written
/// beside the committed one, for a person to read and commit — Pricing still has
/// no writer of promotions. What it cannot express is said, and a proposal never
/// gives away more than the store did: a discount whose restriction the engine
/// cannot hold is not proposed at all.
/// </summary>
public sealed class ProposePromotionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Money Eur(decimal amount) => new(amount, "EUR");

    private sealed class Shop
    {
        public FakeDiscountSource Source { get; } = new();

        public Proposals Proposals { get; } = new();

        public List<Promotion> Committed { get; } =
        [
            new("KITCHEN5", LocalizedText.From("es", "5 € en menaje"), PromotionCondition.Always,
                new AmountOffOrder(new Money(5m, "EUR")), CombinationPolicy.Stackable, 30),
            new("VIPWELCOME", LocalizedText.From("es", "Bienvenida VIP"), PromotionCondition.Always,
                new AmountOffOrder(new Money(10m, "EUR")), CombinationPolicy.Stackable, 50),
        ];

        public Task<ProposePromotionsResult> ProposeAsync(bool rehearse = false, string culture = "es") =>
            new ProposePromotionsHandler(new OneSource(Source), new Committed(this.Committed), Proposals)
                .HandleAsync(new ProposePromotionsCommand(FakeDiscountSource.Key, culture, rehearse), Ct);

        public Promotion Proposed(string code) => Assert.Single(Proposals.Written!, promotion => promotion.Code == code);
    }

    [Fact]
    public async Task A_discount_the_engine_can_express_is_proposed_as_the_store_ran_it()
    {
        var shop = new Shop();

        var result = await shop.ProposeAsync();

        Assert.Equal(("promotions.proposed.json", 2, 1), (result.File, result.Proposed.Count, result.Left));
        var spring = shop.Proposed("SPRING15");
        Assert.Equal(new PercentOffLine(15m), spring.Effect);
        Assert.Equal(("KITCHEN", "SPRING15"), (spring.Condition.CategoryCode, spring.CouponCode));
        Assert.Equal((FakeDiscountSource.Spring, FakeDiscountSource.Spring.AddMonths(3)), (spring.ValidFrom!.Value, spring.ValidTo!.Value));
        Assert.Equal("Menaje de primavera", spring.Name.In("es"));
    }

    /// <summary>After everything committed, in the store's order: a proposal never jumps ahead of a promotion the shop chose.</summary>
    [Fact]
    public async Task A_proposal_comes_after_every_committed_promotion()
    {
        var shop = new Shop();

        await shop.ProposeAsync();

        Assert.Equal([60, 70], shop.Proposals.Written!.Select(promotion => promotion.Priority));
    }

    /// <summary>An automatic discount has no coupon, and is named for its title; one that combined with everything stacks, one that combined with nothing applies alone.</summary>
    [Fact]
    public async Task An_automatic_discount_is_named_for_its_title_and_combines_as_it_did()
    {
        var shop = new Shop();

        await shop.ProposeAsync();

        var shipping = shop.Proposed("ENVIO-GRATIS-DESDE-60");
        Assert.Equal((null, CombinationPolicy.Stackable), (shipping.CouponCode, shipping.Combination));
        Assert.Equal((new FreeShipping(), Eur(60m)), (shipping.Effect, shipping.Condition.MinimumSubtotal!.Value));
        Assert.Equal(CombinationPolicy.ExclusiveGlobal, shop.Proposed("SPRING15").Combination);
    }

    /// <summary>Without its limit the discount would give away more than the store did; a person decides, so it is said and left.</summary>
    [Fact]
    public async Task A_discount_whose_limit_the_engine_cannot_hold_is_said_and_not_proposed()
    {
        var shop = new Shop();

        var result = await shop.ProposeAsync();

        Assert.DoesNotContain(shop.Proposals.Written!, promotion => promotion.Code == "HOLA5");
        Assert.Contains(result.Notes, note => note.Code == DiscountNoteCode.LimitNotExpressible && note.About == "Bienvenida");
    }

    [Theory]
    [InlineData(SourceDiscountRestriction.UsageLimit, DiscountNoteCode.LimitNotExpressible)]
    [InlineData(SourceDiscountRestriction.UsesPerOrder, DiscountNoteCode.LimitNotExpressible)]
    [InlineData(SourceDiscountRestriction.SomeCustomers, DiscountNoteCode.CustomersNotExpressible)]
    [InlineData(SourceDiscountRestriction.SomeCountries, DiscountNoteCode.ConditionNotExpressible)]
    [InlineData(SourceDiscountRestriction.MaximumShippingPrice, DiscountNoteCode.ConditionNotExpressible)]
    [InlineData(SourceDiscountRestriction.SubscriptionsOnly, DiscountNoteCode.ConditionNotExpressible)]
    public async Task Each_restriction_is_said_for_what_it_is(SourceDiscountRestriction restriction, DiscountNoteCode said)
    {
        var shop = new Shop();
        shop.Source.Discounts.Add(FakeDiscountSource.Rule("9", "Restringido", codes: ["ONLY"], restrictions: [restriction]));

        var result = await shop.ProposeAsync();

        Assert.DoesNotContain(shop.Proposals.Written!, promotion => promotion.Code == "ONLY");
        Assert.Contains(result.Notes, note => note.Code == said && note.About == "Restringido");
    }

    /// <summary>Take three, pay two: the engine's own deal, needing the units it is counted on.</summary>
    [Fact]
    public async Task A_buy_x_get_y_that_gives_the_same_items_free_is_the_engines_deal()
    {
        var shop = new Shop();
        shop.Source.Discounts.Add(FakeDiscountSource.Rule("4", "3x2 en tazas", new SourceBuyXGetY(2, 1, 100m, SameItems: true), new CategoryItems("MUG")));

        await shop.ProposeAsync();

        var deal = shop.Proposed("3X2-EN-TAZAS");
        Assert.Equal((new BuyXGetY(2, 1), 3, "MUG"), (deal.Effect, deal.Condition.MinimumQuantity!.Value, deal.Condition.CategoryCode));
    }

    [Theory]
    [MemberData(nameof(EffectsTheEngineLacks))]
    public async Task An_effect_the_engine_lacks_is_said_and_not_proposed(SourceDiscountEffect effect)
    {
        var shop = new Shop();
        shop.Source.Discounts.Add(FakeDiscountSource.Rule("5", "Raro", effect, codes: ["ODD"]));

        var result = await shop.ProposeAsync();

        Assert.DoesNotContain(shop.Proposals.Written!, promotion => promotion.Code == "ODD");
        Assert.Contains(result.Notes, note => note.Code == DiscountNoteCode.EffectNotExpressible && note.About == "Raro");
    }

    public static TheoryData<SourceDiscountEffect> EffectsTheEngineLacks() =>
    [
        new SourceAmountOff(new Money(2m, "EUR"), OnEachItem: true),
        new SourceBuyXGetY(2, 1, 50m, SameItems: true),
        new SourceBuyXGetY(2, 1, 100m, SameItems: false),
        new SourceOtherEffect("DiscountAutomaticApp"),
    ];

    /// <summary>One SKU is a condition the engine has; several, or something the catalogue cannot name, are not.</summary>
    [Fact]
    public async Task A_target_is_one_category_or_one_sku()
    {
        var shop = new Shop();
        shop.Source.Discounts.Add(FakeDiscountSource.Rule("6", "Moka", target: new SkuItems(["MOKA-OLV"]), codes: ["MOKA"]));
        shop.Source.Discounts.Add(FakeDiscountSource.Rule("7", "Dos cosas", target: new SkuItems(["A", "B"]), codes: ["TWO"]));
        shop.Source.Discounts.Add(FakeDiscountSource.Rule("8", "Colección suelta", target: new UnplacedItems("ofertas"), codes: ["LOOSE"]));

        var result = await shop.ProposeAsync();

        Assert.Equal("MOKA-OLV", shop.Proposed("MOKA").Condition.Sku);
        Assert.Equal(["Colección suelta", "Dos cosas"],
            result.Notes.Where(note => note.Code == DiscountNoteCode.TargetNotExpressible).Select(note => note.About!).Order(StringComparer.Ordinal));
    }

    /// <summary>A code the shop already runs is the shop's, and an ended discount is nothing to propose.</summary>
    [Fact]
    public async Task What_is_already_here_or_over_is_said_and_not_proposed()
    {
        var shop = new Shop();
        shop.Source.Discounts.Add(FakeDiscountSource.Rule("10", "Cinco en menaje", codes: ["kitchen5"]));
        shop.Source.Discounts.Add(FakeDiscountSource.Rule("11", "Rebajas pasadas", codes: ["OLD"], expired: true));

        var result = await shop.ProposeAsync();

        Assert.Equal(2, shop.Proposals.Written!.Count);
        Assert.Contains(result.Notes, note => note.Code == DiscountNoteCode.AlreadyHere && note.About == "Cinco en menaje");
        Assert.Contains(result.Notes, note => note.Code == DiscountNoteCode.Expired && note.About == "Rebajas pasadas");
    }

    /// <summary>Narrowed, never widened: a partial combination applies alone and a second code is dropped, each said.</summary>
    [Fact]
    public async Task What_is_narrowed_is_proposed_and_said()
    {
        var shop = new Shop();
        shop.Source.Discounts.Add(FakeDiscountSource.Rule("12", "Dos códigos", codes: ["FIRST", "SECOND"], combination: SourceCombination.Some));

        var result = await shop.ProposeAsync();

        Assert.Equal(CombinationPolicy.ExclusiveGlobal, shop.Proposed("FIRST").Combination);
        Assert.Contains(result.Notes, note => note.Code == DiscountNoteCode.CombinationNarrowed && note.About == "Dos códigos");
        Assert.Contains(result.Notes, note => note.Code == DiscountNoteCode.CodesNarrowed && note.About == "Dos códigos" && note.Text.Contains("SECOND", StringComparison.Ordinal));
    }

    /// <summary>Two discounts with the same title are two promotions, not one written twice.</summary>
    [Fact]
    public async Task Two_automatic_discounts_with_one_title_get_two_codes()
    {
        var shop = new Shop();
        shop.Source.Discounts.Add(FakeDiscountSource.Rule("13", "Envío gratis desde 60 €", new SourceFreeShipping(), minimumSubtotal: 80m));

        await shop.ProposeAsync();

        Assert.Equal(["ENVIO-GRATIS-DESDE-60", "ENVIO-GRATIS-DESDE-60-2"],
            shop.Proposals.Written!.Select(promotion => promotion.Code).Where(code => code.StartsWith("ENVIO", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_rehearsal_says_what_would_be_proposed_and_writes_nothing()
    {
        var shop = new Shop();

        var rehearsal = await shop.ProposeAsync(rehearse: true, culture: "en");

        Assert.Equal((2, 1, null), (rehearsal.Proposed.Count, rehearsal.Left, rehearsal.File));
        Assert.Null(shop.Proposals.Written);
        Assert.StartsWith("Bienvenida: ", Assert.Single(rehearsal.Notes).Text, StringComparison.Ordinal);
    }

    /// <summary>Nothing to propose writes no file: an empty proposal is not a migration.</summary>
    [Fact]
    public async Task Nothing_proposable_writes_no_file()
    {
        var shop = new Shop();
        shop.Source.Discounts.RemoveRange(0, 2);

        var result = await shop.ProposeAsync();

        Assert.Equal((0, null), (result.Proposed.Count, result.File));
        Assert.Null(shop.Proposals.Written);
    }

    private sealed class OneSource(IDiscountSourceConnector source) : IDiscountSourceRegistry
    {
        public IReadOnlyCollection<string> Sources => [source.Source];

        public IDiscountSourceConnector Get(string name) => source;
    }

    private sealed class Committed(List<Promotion> promotions) : IPromotionReader
    {
        public Task<IReadOnlyList<Promotion>> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Promotion>>(promotions);
    }

    private sealed class Proposals : IPromotionProposals
    {
        public IReadOnlyList<Promotion>? Written { get; private set; }

        public Task<string> WriteAsync(IReadOnlyList<Promotion> promotions, CancellationToken cancellationToken = default)
        {
            Written = promotions;
            return Task.FromResult("promotions.proposed.json");
        }
    }
}
