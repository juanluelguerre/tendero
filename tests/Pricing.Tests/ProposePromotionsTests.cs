using System.Runtime.CompilerServices;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Features.ProposePromotions;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.Pricing.Tests.Sources;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
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
        shop.Source.Discounts.Add(FakeDiscountSource.Rule("12", "Dos códigos", codes: ["FIRST", "SECOND", "THIRD"], combination: SourceCombination.Some));

        var result = await shop.ProposeAsync();

        Assert.Equal(CombinationPolicy.ExclusiveGlobal, shop.Proposed("FIRST").Combination);
        Assert.Contains(result.Notes, note => note.Code == DiscountNoteCode.CombinationNarrowed && note.About == "Dos códigos");
        Assert.Contains(result.Notes, note => note.Code == DiscountNoteCode.CodesNarrowed && note.About == "Dos códigos" && note.Text.Contains("SECOND, THIRD", StringComparison.Ordinal));
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

    // The tests below were found by phase 16 S2b's Stryker close: each mutant they
    // name survived the suite above.

    /// <summary>A shop with no promotion of its own gets the proposal from the first step on (the empty shop has no highest priority).</summary>
    [Fact]
    public async Task On_a_shop_with_no_promotions_the_proposal_starts_at_the_first_step()
    {
        var shop = new Shop();
        shop.Committed.Clear();

        await shop.ProposeAsync();

        Assert.Equal([10, 20], shop.Proposals.Written!.Select(promotion => promotion.Priority));
    }

    /// <summary>Free shipping is the order's, whatever the store said it reached.</summary>
    [Fact]
    public async Task Free_shipping_reaching_a_category_is_the_orders()
    {
        var shop = new Shop();
        shop.Source.Discounts.Add(FakeDiscountSource.Rule("14", "Envío gratis en menaje", new SourceFreeShipping(), new CategoryItems("KITCHEN"), codes: ["SHIPKITCHEN"]));

        await shop.ProposeAsync();

        Assert.Null(shop.Proposed("SHIPKITCHEN").Condition.CategoryCode);
    }

    /// <summary>A third discount with one title is a third code, counting up.</summary>
    [Fact]
    public async Task Three_automatic_discounts_with_one_title_count_up()
    {
        var shop = new Shop();
        shop.Source.Discounts.Add(FakeDiscountSource.Rule("13", "Envío gratis desde 60 €", new SourceFreeShipping(), minimumSubtotal: 80m));
        shop.Source.Discounts.Add(FakeDiscountSource.Rule("15", "Envío gratis desde 60 €", new SourceFreeShipping(), minimumSubtotal: 90m));

        await shop.ProposeAsync();

        Assert.Equal(["ENVIO-GRATIS-DESDE-60", "ENVIO-GRATIS-DESDE-60-2", "ENVIO-GRATIS-DESDE-60-3"],
            shop.Proposals.Written!.Select(promotion => promotion.Code).Where(code => code.StartsWith("ENVIO", StringComparison.Ordinal)));
    }

    /// <summary>What the engine cannot take off, or reach, is named in the note as the store had it.</summary>
    [Theory]
    [MemberData(nameof(WhatIsNamed))]
    public async Task What_cannot_be_expressed_is_named(SourceDiscountEffect effect, SourceDiscountTarget target, string named)
    {
        var shop = new Shop();
        shop.Source.Discounts.Add(FakeDiscountSource.Rule("16", "Raro", effect, target, codes: ["ODD"]));

        var result = await shop.ProposeAsync(culture: "en");

        Assert.Contains($"({named})", Assert.Single(result.Notes, note => note.About == "Raro").Text, StringComparison.Ordinal);
    }

    public static TheoryData<SourceDiscountEffect, SourceDiscountTarget, string> WhatIsNamed() => new()
    {
        { new SourceAmountOff(new Money(2m, "EUR"), OnEachItem: true), new AllItems(), "amount-off-each-item" },
        { new SourceBuyXGetY(2, 1, 50m, SameItems: true), new AllItems(), "buy 2 get 1 at 50% off" },
        { new SourceBuyXGetY(2, 1, 100m, SameItems: false), new AllItems(), "buy 2 get 1 of other items" },
        { new SourcePercentOff(10m), new SkuItems(["A", "B"]), "A, B" },
    };

    /// <summary>
    /// Every note says its sentence in the language asked, starting with the discount
    /// it is about and naming the detail it carries.
    /// </summary>
    [Theory]
    [InlineData(DiscountNoteCode.Expired, false, "tienda")]
    [InlineData(DiscountNoteCode.AlreadyHere, true, "código")]
    [InlineData(DiscountNoteCode.LimitNotExpressible, true, "límite")]
    [InlineData(DiscountNoteCode.CustomersNotExpressible, false, "clientes")]
    [InlineData(DiscountNoteCode.ConditionNotExpressible, true, "condición")]
    [InlineData(DiscountNoteCode.TargetNotExpressible, true, "categoría")]
    [InlineData(DiscountNoteCode.EffectNotExpressible, true, "descuenta")]
    [InlineData(DiscountNoteCode.CombinationNarrowed, false, "combinaba")]
    [InlineData(DiscountNoteCode.CodesNarrowed, true, "códigos")]
    public void A_note_is_said_in_the_language_asked_about_its_discount(DiscountNoteCode code, bool namesItsDetail, string spanishWord)
    {
        var note = new DiscountNote(code, "Rebajas", "the detail");

        var spanish = ProposePromotionsNotes.Said(note, english: false).Text;
        var english = ProposePromotionsNotes.Said(note, english: true).Text;

        Assert.Contains(spanishWord, spanish, StringComparison.Ordinal);
        Assert.DoesNotContain(spanishWord, english, StringComparison.Ordinal);
        Assert.All([spanish, english], text =>
        {
            Assert.StartsWith("Rebajas: ", text, StringComparison.Ordinal);
            Assert.True(text.Length > 30, text);
            Assert.Equal(namesItsDetail, text.Contains("the detail", StringComparison.Ordinal));
        });
    }

    /// <summary>The answer's notes are in the language the request asked for.</summary>
    [Fact]
    public async Task The_answer_is_in_the_language_asked()
    {
        var shop = new Shop();

        var english = await shop.ProposeAsync(rehearse: true, culture: "en");

        Assert.Equal(ProposePromotionsNotes.Said(new DiscountNote(DiscountNoteCode.LimitNotExpressible, "Bienvenida", "OncePerCustomer"), english: true).Text,
            Assert.Single(english.Notes).Text);
    }

    /// <summary>
    /// How much of a store's discounts comes across (spec, *Evidence of use*):
    /// discounts proposed and left, and the notes by code, by source; an outcome with
    /// nothing in it is not counted, and a rehearsal counts nothing.
    /// </summary>
    [Fact]
    public async Task A_proposal_counts_its_discounts_by_outcome_and_its_notes_by_code()
    {
        using var recorder = new InstrumentRecorder("tendero.pricing.import.discounts", "tendero.pricing.import.notes");
        var shop = new Shop();

        await shop.ProposeAsync(rehearse: true);
        Assert.Empty(recorder.Of("tendero.pricing.import.discounts"));

        await shop.ProposeAsync();
        shop.Source.Discounts.RemoveAt(2);
        await shop.ProposeAsync();

        Assert.Equal(
            [("fake", "proposed", 2L), ("fake", "left", 1L), ("fake", "proposed", 2L)],
            recorder.Measured("tendero.pricing.import.discounts")
                .Select(each => ((string)each.Tags["catalog.source"]!, (string)each.Tags["pricing.import.outcome"]!, each.Value)));
        Assert.Equal(
            [("fake", "LimitNotExpressible")],
            recorder.Of("tendero.pricing.import.notes").Select(tags => ((string)tags["catalog.source"]!, (string)tags["pricing.import.note"]!)));
    }

    /// <summary>A source must exist, not just look like one: the request is refused before anything is read.</summary>
    [Fact]
    public void An_unknown_source_is_refused_naming_the_ones_there_are()
    {
        var validator = new ProposePromotionsValidator(new OneSource(new FakeDiscountSource()));

        var refused = validator.Validate(new ProposePromotionsCommand("magento"));

        Assert.Equal("Unknown discount source 'magento'. Registered sources: fake.", Assert.Single(refused.Errors).ErrorMessage);
        Assert.True(validator.Validate(new ProposePromotionsCommand(FakeDiscountSource.Key)).IsValid);
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
