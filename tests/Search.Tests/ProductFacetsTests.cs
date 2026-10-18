using System.Text.Json.Nodes;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Elasticsearch;
using ElGuerre.Tendero.Search.Offers;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;
using static ElGuerre.Tendero.Search.Tests.ElasticsearchBodies;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// The facets of a listing (R-7.3, F-2) without a cluster: which filters narrow
/// each facet's counts, the rule that a facet shows only when it splits the
/// results, how the counts become what a column draws, and the request the
/// client really writes. The end-to-end half is in the integration tests,
/// against a real Elasticsearch.
/// </summary>
public sealed class ProductFacetsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Today = new(2026, 1, 15);

    private static readonly SearchFilters Everything = new()
    {
        Brands = ["Vertex"],
        Within = ["SHOES"],
        Facets = ["COLOR:BLACK", "COLOR:RED", "SIZE:42"],
        MinPrice = 30,
        MaxPrice = 80,
        OnOffer = true,
        NewArrivals = true,
        InStock = true,
    };

    // ---------- The disjunctive filter set ----------

    /// <summary>
    /// A facet's counts are narrowed by every OTHER facet's choice and never by
    /// its own: choosing Vertex still shows how many Caminante would add.
    /// </summary>
    [Theory]
    [InlineData(FacetCodes.Brand)]
    [InlineData(FacetCodes.Category)]
    [InlineData(FacetCodes.Price)]
    [InlineData(FacetCodes.Offer)]
    [InlineData(FacetCodes.New)]
    [InlineData(FacetCodes.InStock)]
    [InlineData("COLOR")]
    [InlineData("SIZE")]
    public void Each_facet_is_counted_under_every_filter_but_its_own(string facet)
    {
        var filters = ProductFilters.From(Everything, OfferScope.Nothing, Today);

        string[] all = [FacetCodes.Category, FacetCodes.Brand, FacetCodes.Price, FacetCodes.Offer, FacetCodes.New, FacetCodes.InStock, "COLOR", "SIZE"];

        Assert.Equal(all.Where(code => code != facet).Order(), filters.Narrowing(facet).Order());
    }

    /// <summary>Two colours are one group — they add up — and a size is another, which narrows.</summary>
    [Fact]
    public void Values_of_one_code_are_one_filter_and_each_code_is_its_own()
    {
        var filters = ProductFilters.From(Everything, OfferScope.Nothing, Today);

        Assert.Equal(["COLOR", "SIZE"], filters.ChosenCodes);
        Assert.Single(filters.Chosen, filter => filter.Facet == "COLOR");
    }

    [Fact]
    public void Nothing_chosen_narrows_nothing()
    {
        var filters = ProductFilters.From(null, OfferScope.Nothing, Today);

        Assert.Null(filters.All());
        Assert.Empty(filters.Narrowing(FacetCodes.Brand));
    }

    // ---------- The request the client writes ----------

    /// <summary>
    /// With nothing chosen there is no post filter, so the query the golden set
    /// measures is the query it always measured — the aggregations ride beside it
    /// and score nothing.
    /// </summary>
    [Fact]
    public async Task An_unfiltered_search_sends_no_post_filter()
    {
        var body = await LexicalBody(new ProductSearchQuery("zapatillas", "es"));

        Assert.Null(body["post_filter"]);
        Assert.NotNull(Aggregation(body, FacetCodes.Brand));
    }

    [Fact]
    public async Task The_filters_narrow_the_hits_through_the_post_filter_and_each_facet_leaves_its_own_out()
    {
        var body = await LexicalBody(new ProductSearchQuery("zapatillas", "es", Filters: Everything));

        var postFilter = body["post_filter"]!.ToJsonString();
        Assert.Contains("brand.raw", postFilter);
        Assert.Contains("COLOR:BLACK", postFilter);
        Assert.Contains("priceAmount", postFilter);

        // The query itself carries none of them: they would narrow the counts too.
        Assert.DoesNotContain("brand.raw", body["query"]!.ToJsonString());

        var brandFilter = Aggregation(body, FacetCodes.Brand)!["filter"]!.ToJsonString();
        Assert.DoesNotContain("brand.raw", brandFilter);
        Assert.Contains("COLOR:BLACK", brandFilter);

        var colourFilter = Aggregation(body, FacetAggregations.ValuesOf(0))!["filter"]!.ToJsonString();
        Assert.DoesNotContain("COLOR:BLACK", colourFilter);
        Assert.Contains("SIZE:42", colourFilter);
        Assert.Contains("brand.raw", colourFilter);
    }

    /// <summary>A product is what a shopper counts, and the index holds variants: every count is a cardinality of productId.</summary>
    [Fact]
    public async Task Every_count_is_of_products_exact_at_laboratory_scale()
    {
        var body = await LexicalBody(new ProductSearchQuery("", "es", Filters: Everything));
        var aggregations = body["aggregations"]!.ToJsonString();

        Assert.Contains("\"cardinality\":{\"field\":\"productId\",\"precision_threshold\":3000}", aggregations);
        Assert.DoesNotContain("\"value_count\"", aggregations);
    }

    [Fact]
    public async Task On_offer_asks_for_the_branches_and_skus_the_running_offers_reach()
    {
        var offers = new OfferScope(false, ["APPAREL"], ["PAN-24"]);
        var body = await LexicalBody(new ProductSearchQuery("", "es", Filters: new SearchFilters { OnOffer = true }), offers);

        var postFilter = body["post_filter"]!.ToJsonString();
        Assert.Contains("APPAREL", postFilter);
        Assert.Contains("PAN-24", postFilter);
        Assert.Contains("\"sku\"", postFilter);
    }

    [Fact]
    public async Task With_no_offer_running_on_offer_matches_nothing_and_with_one_for_everything_it_matches_all()
    {
        var chosen = new ProductSearchQuery("", "es", Filters: new SearchFilters { OnOffer = true });

        Assert.Contains("match_none", (await LexicalBody(chosen, OfferScope.Nothing))["post_filter"]!.ToJsonString());
        Assert.Contains("match_all", (await LexicalBody(chosen, new OfferScope(true, [], [])))["post_filter"]!.ToJsonString());
    }

    /// <summary>"The last thirty days" is a range computed when asked (TestClock: 2026-01-15).</summary>
    [Fact]
    public async Task New_arrivals_is_the_last_thirty_days_from_the_clock()
    {
        var body = await LexicalBody(new ProductSearchQuery("", "es", Filters: new SearchFilters { NewArrivals = true }));

        var postFilter = body["post_filter"]!.ToJsonString();
        Assert.Contains("availableFrom", postFilter);
        Assert.Contains("2025-12-16", postFilter);
        Assert.Contains("2026-01-15", postFilter);
    }

    [Theory]
    [InlineData(ProductSort.PriceAsc, "asc")]
    [InlineData(ProductSort.PriceDesc, "desc")]
    public async Task A_price_order_sorts_by_the_variants_price(ProductSort sort, string order)
    {
        var body = await LexicalBody(new ProductSearchQuery("", "es", Sort: sort));

        Assert.Contains($"{{\"priceAmount\":{{\"order\":\"{order}\"}}}}", body["sort"]!.ToJsonString());
    }

    /// <summary>
    /// Hybrid search's union: the products meaning found are a second way to
    /// match, scoring zero, so the words keep their ranking and the counts hold both.
    /// </summary>
    [Fact]
    public async Task What_meaning_found_is_matched_beside_the_words_and_scores_nothing()
    {
        var union = (await LexicalBody(new ProductSearchQuery("regalo", "es", AlsoMatching: ["p1", "p2"])))["query"]!.ToJsonString();
        var words = (await LexicalBody(new ProductSearchQuery("regalo", "es")))["query"]!.ToJsonString();

        Assert.Contains("{\"terms\":{\"boost\":0,\"productId\":[\"p1\",\"p2\"]}}", union);
        Assert.Contains("\"minimum_should_match\":1", union);
        Assert.DoesNotContain("productId", words);
    }

    [Fact]
    public async Task The_card_reader_applies_the_filters_it_is_given_and_none_when_given_none()
    {
        var (client, bodies) = Capturing();

        await Assert.ThrowsAnyAsync<Exception>(() => Adapters.HitReader(client)
            .FindAsync(["p1"], "es", null, new SearchFilters { Brands = ["Vertex"] }, Ct));
        await Assert.ThrowsAnyAsync<Exception>(() => Adapters.HitReader(client)
            .FindAsync(["p1"], "es", null, SearchFilters.None, Ct));

        Assert.Contains("brand.raw", bodies[0]);
        Assert.DoesNotContain("brand.raw", bodies[1]);
    }

    /// <summary>
    /// No filters chosen is no clause at all, not an empty one: the card reader's
    /// bool holds the ids and the branch, and nothing else (13c's scoped Stryker
    /// run found an empty filter clause unheld).
    /// </summary>
    [Fact]
    public async Task With_no_filters_chosen_the_card_reader_sends_no_filter_clause_for_them()
    {
        var (client, bodies) = Capturing();

        await Assert.ThrowsAnyAsync<Exception>(() => Adapters.HitReader(client)
            .FindAsync(["p1"], "es", null, SearchFilters.None, Ct));

        var filters = JsonNode.Parse(Assert.Single(bodies))!["query"]!["bool"]!["filter"]!.AsArray();
        Assert.DoesNotContain(filters, clause => clause!.AsObject().ContainsKey("bool"));
    }

    // ---------- Whether a facet shows ----------

    [Theory]
    [InlineData(new long[] { 3, 5 }, 8, true)]           // two brands
    [InlineData(new long[] { 8 }, 8, false)]             // one brand
    [InlineData(new long[] { 8 }, 9, false)]             // one brand and a product with none
    [InlineData(new long[] { 5, 0 }, 5, false)]          // a value with no products is not a second
    [InlineData(new long[] { 5, 5, 5 }, 5, false)]       // HOME, KITCHEN, COOKWARE over one leaf
    [InlineData(new long[] { 10, 10, 6, 4 }, 10, true)]  // two leaves under one department
    [InlineData(new long[] { 6, 6 }, 6, false)]          // every shoe in every size
    public void Options_show_when_two_hold_products_and_one_would_narrow(long[] counts, long @base, bool shows)
    {
        var options = new OptionCounts(@base, counts.Select((count, index) => (count, index)).ToDictionary(pair => $"V{pair.index}", pair => pair.count));

        Assert.Equal(shows, FacetRules.OptionsSplit(options, anyChosen: false));
    }

    [Fact]
    public void Something_chosen_always_shows_its_facet()
    {
        Assert.True(FacetRules.OptionsSplit(new OptionCounts(8, new Dictionary<string, long> { ["Vertex"] = 8 }), anyChosen: true));
        Assert.True(FacetRules.SwitchShows(new SwitchCounts(13, 0), chosen: true));
        Assert.True(FacetRules.PriceShows(null, chosen: true));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(12, true)]
    [InlineData(13, false)]
    public void A_switch_shows_when_it_would_narrow_the_results(long on, bool shows) =>
        Assert.Equal(shows, FacetRules.SwitchShows(new SwitchCounts(13, on), chosen: false));

    [Fact]
    public void A_price_range_shows_with_two_prices_and_not_with_one()
    {
        Assert.True(FacetRules.PriceShows(new PriceSpread(10, 20, []), chosen: false));
        Assert.False(FacetRules.PriceShows(new PriceSpread(20, 20, []), chosen: false));
        Assert.False(FacetRules.PriceShows(null, chosen: false));
    }

    // ---------- What a column draws ----------

    [Fact]
    public void Facets_come_in_the_specs_order_labelled_in_the_culture()
    {
        var facets = Assemble(Counts(), SearchFilters.None, "en");

        Assert.Equal(
            [FacetKind.Category, FacetKind.Brand, FacetKind.Price, FacetKind.Offer, FacetKind.New, FacetKind.InStock,
             FacetKind.Axis, FacetKind.Axis, FacetKind.Attribute, FacetKind.Flag],
            facets.Select(facet => facet.Kind));
        Assert.Equal(["category", "brand", "price", "offer", "new", "stock", "COLOR", "SIZE", "MATERIAL", "INDUCTION"],
            facets.Select(facet => facet.Code));
        Assert.Equal(["Category", "Brand", "Price", "On offer", "New arrivals", "Available", "colour", "size", "material", "induction"],
            facets.Select(facet => facet.Label));
    }

    /// <summary>Every built-in facet is named in Spanish too (invariant 6), not only in the language the tests happened to read.</summary>
    [Fact]
    public void The_built_in_facets_are_labelled_in_spanish()
    {
        var facets = Assemble(Counts(), SearchFilters.None, "es");

        Assert.Equal(["Categoría", "Marca", "Precio", "En oferta", "Novedades", "Disponible"],
            facets.Take(6).Select(facet => facet.Label));
    }

    [Fact]
    public void Options_carry_the_definitions_labels_and_order_and_brands_come_by_count()
    {
        var facets = Assemble(Counts(), SearchFilters.None, "es");

        var colour = facets.Single(facet => facet.Code == "COLOR");
        Assert.Equal(["COLOR:BLACK", "COLOR:RED"], colour.Options.Select(option => option.Value));
        Assert.Equal(["negro", "rojo"], colour.Options.Select(option => option.Label));
        // A swatch where the definition gives one, so a colour is drawn beside its name.
        Assert.Equal(["#201D1B", null], colour.Options.Select(option => option.Swatch));

        var brand = facets.Single(facet => facet.Code == FacetCodes.Brand);
        Assert.Equal(["Vertex", "Caminante"], brand.Options.Select(option => option.Value));

        var category = facets.Single(facet => facet.Code == FacetCodes.Category);
        Assert.Contains(category.Options, option => option is { Value: "SHOES", Label: "Zapatillas" });
    }

    [Fact]
    public void A_yes_or_no_attribute_is_a_switch_whose_one_option_is_yes()
    {
        var induction = Assemble(Counts(), SearchFilters.None, "es").Single(facet => facet.Code == "INDUCTION");

        var yes = Assert.Single(induction.Options);
        Assert.Equal(("INDUCTION:true", 2L), (yes.Value, yes.Count));
    }

    [Fact]
    public void A_chosen_option_nobody_carries_is_still_returned_so_it_can_be_unchosen()
    {
        var chosen = new SearchFilters { Brands = ["Filo"], Facets = ["COLOR:OLIVE"] };

        var facets = Assemble(Counts(), chosen, "es");

        Assert.Contains(facets.Single(facet => facet.Code == FacetCodes.Brand).Options, option => option is { Value: "Filo", Count: 0, Chosen: true });
        Assert.Contains(facets.Single(facet => facet.Code == "COLOR").Options, option => option is { Value: "COLOR:OLIVE", Count: 0, Chosen: true });
    }

    [Fact]
    public void A_facet_that_does_not_split_the_results_is_left_out()
    {
        var counts = Counts() with
        {
            Brands = new OptionCounts(8, new Dictionary<string, long> { ["Vertex"] = 8 }),
            Offer = new SwitchCounts(8, 0),
        };

        var facets = Assemble(counts, SearchFilters.None, "es");

        Assert.DoesNotContain(facets, facet => facet.Code is FacetCodes.Brand or FacetCodes.Offer);
    }

    [Fact]
    public void The_price_facet_carries_the_range_the_choice_and_the_bands()
    {
        var bands = new[] { new PriceBand(null, new Money(30, "EUR"), 3, false), new PriceBand(new Money(30, "EUR"), null, 5, true) };

        var price = Assemble(Counts(), new SearchFilters { MinPrice = 30 }, "es", bands).Single(facet => facet.Kind == FacetKind.Price);

        Assert.Empty(price.Options);
        Assert.Equal(
            (new Money(18.50m, "EUR"), new Money(169m, "EUR"), (Money?)new Money(30m, "EUR"), (Money?)null),
            (price.Price!.Min, price.Price.Max, price.Price.ChosenMin, price.Price.ChosenMax));
        Assert.Equal(bands, price.Price.Bands);
    }

    // ---------- The edges R-7/R-8's Stryker close found untested ----------

    /// <summary>The price filter is the range asked for, both ends inclusive, not merely a filter on the price field.</summary>
    [Fact]
    public async Task A_price_filter_is_the_range_asked_for()
    {
        var body = await LexicalBody(new ProductSearchQuery("zapatillas", "es", Filters: new SearchFilters { MinPrice = 30, MaxPrice = 80 }));

        var range = body["post_filter"]!.ToJsonString();
        Assert.Contains("\"gte\":30", range);
        Assert.Contains("\"lte\":80", range);
    }

    [Theory]
    [InlineData("COLOR:RED", "COLOR")]
    [InlineData("COLOR:", null)]      // a code with no value is not a choice
    [InlineData(":RED", null)]        // nor a value with no code
    [InlineData("COLOR", null)]
    public void A_choice_is_a_code_and_a_value(string pair, string? code) =>
        Assert.Equal(code, FacetValues.CodeOf(pair));

    [Theory]
    [InlineData("COLOR", true)]
    [InlineData("brand", false)]     // the built-in facets are lower case
    [InlineData("", false)]
    public void An_attributes_code_starts_upper_case(string facet, bool attribute) =>
        Assert.Equal(attribute, ProductFilters.IsAttributeCode(facet));

    /// <summary>A code is read literally by the index's regular expression: anything but a letter or a digit is escaped.</summary>
    [Fact]
    public void A_codes_prefix_pattern_escapes_what_is_not_a_letter_or_a_digit() =>
        Assert.Equal(@"SIZE\_EU\.2:.*", FacetAggregations.PrefixPattern("SIZE_EU.2"));

    /// <summary>Categories by how many products each holds, the code breaking a tie; brands the same, their name breaking it.</summary>
    [Fact]
    public void Categories_and_brands_come_by_count_and_a_tie_by_name()
    {
        var counts = Counts() with
        {
            Brands = new OptionCounts(8, new Dictionary<string, long> { ["vertex"] = 4, ["Caminante"] = 4 }),
        };

        var facets = Assemble(counts, SearchFilters.None, "es");

        Assert.Equal(["APPAREL", "SHOES", "HOME", "KITCHEN"], facets.Single(facet => facet.Code == FacetCodes.Category).Options.Select(option => option.Value));
        Assert.Equal(["Caminante", "vertex"], facets.Single(facet => facet.Code == FacetCodes.Brand).Options.Select(option => option.Value));
    }

    /// <summary>A price chosen keeps the price facet even when the results left hold one price — either end alone is a choice.</summary>
    [Theory]
    [InlineData(30, null)]
    [InlineData(null, 80)]
    public void A_price_chosen_keeps_the_price_facet(int? min, int? max)
    {
        var counts = Counts() with { Price = new PriceSpread(49.90m, 49.90m, []) };

        var facets = Assemble(counts, new SearchFilters { MinPrice = min, MaxPrice = max }, "es");

        Assert.Contains(facets, facet => facet.Kind == FacetKind.Price);
        Assert.DoesNotContain(Assemble(counts, SearchFilters.None, "es"), facet => facet.Kind == FacetKind.Price);
    }

    /// <summary>Codes the definitions do not know come after the ones they do, in the code's order.</summary>
    [Fact]
    public void Codes_nobody_defined_come_last_in_their_own_order()
    {
        var counts = Counts() with
        {
            Codes = new Dictionary<string, CodeCounts>
            {
                ["ZED"] = new(false, new OptionCounts(8, new Dictionary<string, long> { ["ZED:A"] = 2, ["ZED:B"] = 3 })),
                ["COLOR"] = new(true, new OptionCounts(8, new Dictionary<string, long> { ["COLOR:RED"] = 2, ["COLOR:BLACK"] = 3 })),
                ["ABC"] = new(false, new OptionCounts(8, new Dictionary<string, long> { ["ABC:Y"] = 2, ["ABC:X"] = 3 })),
            },
        };

        var facets = Assemble(counts, SearchFilters.None, "es");

        Assert.Equal(["COLOR", "ABC", "ZED"], facets.Where(facet => facet.Kind is FacetKind.Axis or FacetKind.Attribute).Select(facet => facet.Code));
        // With no definition to order them, a code's values come by value.
        Assert.Equal(["ABC:X", "ABC:Y"], facets.Single(facet => facet.Code == "ABC").Options.Select(option => option.Value));
    }

    /// <summary>The definition decides whether a code is a variant axis; what the index says is only the fallback.</summary>
    [Fact]
    public void The_definition_decides_whether_a_code_is_an_axis()
    {
        var counts = Counts() with
        {
            Codes = new Dictionary<string, CodeCounts>
            {
                ["COLOR"] = new(false, new OptionCounts(8, new Dictionary<string, long> { ["COLOR:RED"] = 2, ["COLOR:BLACK"] = 3 })),
            },
        };

        Assert.Equal(FacetKind.Axis, Assemble(counts, SearchFilters.None, "es").Single(facet => facet.Code == "COLOR").Kind);
    }

    /// <summary>Options come in the order the shopkeeper gave them, not the alphabet's; a value the definition lacks comes last.</summary>
    [Fact]
    public void Options_come_in_the_order_the_shopkeeper_gave()
    {
        var clock = new TestClock();
        var fit = AttributeDefinition.Define(clock, "FIT", Adapters.Text("corte", "fit"), AttributeKind.Option, isFacet: true);
        fit.AddOption(clock, "SLIM", Adapters.Text("entallado", "slim"));
        fit.AddOption(clock, "REGULAR", Adapters.Text("normal", "regular"));
        fit.AddOption(clock, "LOOSE", Adapters.Text("holgado", "loose"));
        var counts = Counts() with
        {
            Codes = new Dictionary<string, CodeCounts>
            {
                ["FIT"] = new(false, new OptionCounts(8, new Dictionary<string, long> { ["FIT:LOOSE"] = 1, ["FIT:REGULAR"] = 3, ["FIT:SLIM"] = 2, ["FIT:OTHER"] = 1 })),
            },
        };

        var facets = FacetAssembler.Build(counts, [], ProductFilters.From(SearchFilters.None, OfferScope.Nothing, Today), "es",
            new AttributeDefinitions([fit]), Adapters.ShopCategories());

        Assert.Equal(["FIT:SLIM", "FIT:REGULAR", "FIT:LOOSE", "FIT:OTHER"], facets.Single(facet => facet.Code == "FIT").Options.Select(option => option.Value));
    }

    /// <summary>A value no product carries now is not offered, unless it is the one chosen.</summary>
    [Fact]
    public void A_value_with_no_products_is_not_offered()
    {
        var counts = Counts() with
        {
            Codes = new Dictionary<string, CodeCounts>
            {
                ["MATERIAL"] = new(false, new OptionCounts(8, new Dictionary<string, long> { ["MATERIAL:CAST_IRON"] = 1, ["MATERIAL:ALUMINIUM"] = 2, ["MATERIAL:GLASS"] = 0 })),
            },
        };

        Assert.DoesNotContain(Assemble(counts, SearchFilters.None, "es").Single(facet => facet.Code == "MATERIAL").Options,
            option => option.Value == "MATERIAL:GLASS");
    }

    /// <summary>A yes-or-no that narrows nothing is hidden, unless something of it was chosen — even "no", so it can be unchosen.</summary>
    [Fact]
    public void A_yes_or_no_that_narrows_nothing_shows_only_when_chosen()
    {
        var counts = Counts() with
        {
            Codes = new Dictionary<string, CodeCounts>
            {
                ["INDUCTION"] = new(false, new OptionCounts(8, new Dictionary<string, long> { ["INDUCTION:true"] = 8 })),
            },
        };

        Assert.DoesNotContain(Assemble(counts, SearchFilters.None, "es"), facet => facet.Code == "INDUCTION");
        Assert.Contains(Assemble(counts, new SearchFilters { Facets = ["INDUCTION:false"] }, "es"), facet => facet.Code == "INDUCTION");
    }

    private static FacetCounts Counts() => new(
        Total: 8,
        Categories: new OptionCounts(8, new Dictionary<string, long> { ["APPAREL"] = 5, ["SHOES"] = 5, ["HOME"] = 3, ["KITCHEN"] = 3 }),
        Brands: new OptionCounts(8, new Dictionary<string, long> { ["Caminante"] = 3, ["Vertex"] = 5 }),
        Price: new PriceSpread(18.50m, 169m, [29.90m, 49.90m, 79.95m, 119m]),
        Offer: new SwitchCounts(8, 3),
        New: new SwitchCounts(8, 1),
        InStock: new SwitchCounts(8, 7),
        Codes: new Dictionary<string, CodeCounts>
        {
            ["INDUCTION"] = new(false, new OptionCounts(8, new Dictionary<string, long> { ["INDUCTION:true"] = 2, ["INDUCTION:false"] = 1 })),
            ["MATERIAL"] = new(false, new OptionCounts(8, new Dictionary<string, long> { ["MATERIAL:CAST_IRON"] = 1, ["MATERIAL:ALUMINIUM"] = 2 })),
            ["SIZE"] = new(true, new OptionCounts(8, new Dictionary<string, long> { ["SIZE:42"] = 4, ["SIZE:40"] = 3 })),
            ["COLOR"] = new(true, new OptionCounts(8, new Dictionary<string, long> { ["COLOR:RED"] = 2, ["COLOR:BLACK"] = 3 })),
        });

    private static IReadOnlyList<SearchFacet> Assemble(
        FacetCounts counts, SearchFilters asked, string culture, IReadOnlyList<PriceBand>? bands = null) =>
        FacetAssembler.Build(counts, bands ?? [], ProductFilters.From(asked, OfferScope.Nothing, Today), culture,
            Adapters.ShopDefinitions(), Adapters.ShopCategories());

    private static JsonNode? Aggregation(JsonNode body, string name) =>
        (body["aggregations"] ?? body["aggs"])?[name];

    private static async Task<JsonNode> LexicalBody(ProductSearchQuery query, OfferScope? offers = null)
    {
        var (client, bodies) = Capturing();

        await Assert.ThrowsAnyAsync<Exception>(() => Adapters.Lexical(client, offers).SearchAsync(query, Ct));

        return JsonNode.Parse(Assert.Single(bodies))!;
    }
}
