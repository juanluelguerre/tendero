using System.Diagnostics;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Elasticsearch;
using ElGuerre.Tendero.Search.Offers;
using ElGuerre.Tendero.Search.Strategies;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The facets (R-7.3, F-2) against a real Elasticsearch, at the image and the
/// mapping the shop runs. The unit tests hold the request's shape; only a node can
/// say the counts come out right — disjunctive, per product and not per variant,
/// an axis pair matched on one variant, bands counted as choosing them filters.
///
/// Seven products, two departments, four brands, written straight into the index
/// so every price, size and stock flag is the test's own:
///
/// | Product | Brand | Variants (colour/size price stock) | Facts |
/// |---|---|---|---|
/// | Pulse | Vertex | black/40 49.90 · black/42 49.90 OUT · red/42 49.90 | mesh, new |
/// | Trail | Vertex | red/40 89.90 · red/42 89.90 | leather |
/// | Camino | Caminante | black/40 29.90 · black/42 34.90 | mesh |
/// | Sartén | Kuisina | 24.90 | aluminium, induction |
/// | Olla | Kuisina | 119.00 OUT | cast iron, induction |
/// | Cazo | Kuisina | 18.50 (SKU CAZO-1) | aluminium, no induction |
/// | Pañuelo | Lanar | 39.90, one variant | black, stated on the product (no variants along COLOR) |
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProductFacetsIntegrationTests(ElasticsearchFixture elasticsearch) : IClassFixture<ElasticsearchFixture>
{
    private static readonly TestClock Clock = new();
    private static readonly SemaphoreSlim Seeding = new(1, 1);
    private static ElasticsearchFixture? seeded;

    /// <summary>Shoes and one SKU of cookware are on offer.</summary>
    private static readonly OfferScope Offers = new(false, ["SHOES"], ["CAZO-1"]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- Disjunctive counts, per product ----------

    [Fact]
    public async Task A_category_page_counts_products_not_variants_and_leaves_out_what_does_not_split_it()
    {
        var page = await SearchAsync(Browse("SHOES"));

        Assert.Equal(3, page.Total);
        Assert.Equal([("Caminante", 1L), ("Vertex", 2L)], Options(page, FacetCodes.Brand).Select(o => (o.Value, o.Count)).Order());
        Assert.Equal([("COLOR:BLACK", 2L), ("COLOR:RED", 2L)], Options(page, "COLOR").Select(o => (o.Value, o.Count)));
        Assert.Equal(["negro", "rojo"], Options(page, "COLOR").Select(o => o.Label));
        Assert.Equal([("MATERIAL:MESH", 2L), ("MATERIAL:LEATHER", 1L)], Options(page, "MATERIAL").Select(o => (o.Value, o.Count)));

        // Every shoe comes in a 40 and a 42, every one is under APPAREL and SHOES,
        // and none is a pan: size, category and induction narrow nothing here.
        Assert.DoesNotContain(page.Facets!, facet => facet.Code is "SIZE" or FacetCodes.Category or "INDUCTION");
    }

    /// <summary>
    /// A colour stated on a product with no variants along it (the Pañuelo) and the
    /// same colour on another product's variants count together: two fields,
    /// disjoint products, summed — never one replacing the other.
    /// </summary>
    [Fact]
    public async Task A_colour_on_the_product_and_on_variants_counts_every_product_once()
    {
        var page = await SearchAsync(Browse("APPAREL"));

        Assert.Equal([("COLOR:BLACK", 3L), ("COLOR:RED", 2L)], Options(page, "COLOR").Select(o => (o.Value, o.Count)));

        var black = await SearchAsync(Browse("APPAREL", new SearchFilters { Facets = ["COLOR:BLACK"] }));
        Assert.Equal(3, black.Total);
    }

    [Fact]
    public async Task Choosing_a_brand_still_counts_the_other_brand_and_narrows_every_other_facet()
    {
        var page = await SearchAsync(Browse("SHOES", new SearchFilters { Brands = ["Vertex"] }));

        Assert.Equal(2, page.Total);
        Assert.Contains(Options(page, FacetCodes.Brand), option => option is { Value: "Caminante", Count: 1, Chosen: false });
        Assert.Contains(Options(page, FacetCodes.Brand), option => option is { Value: "Vertex", Count: 2, Chosen: true });
        Assert.Equal([("COLOR:BLACK", 1L), ("COLOR:RED", 2L)], Options(page, "COLOR").Select(o => (o.Value, o.Count)));
    }

    [Fact]
    public async Task Choices_in_one_facet_add_up_and_across_facets_they_narrow()
    {
        var either = await SearchAsync(Browse("SHOES", new SearchFilters { Facets = ["COLOR:BLACK", "COLOR:RED"] }));
        var both = await SearchAsync(Browse("SHOES", new SearchFilters { Brands = ["Vertex"], Facets = ["COLOR:BLACK"] }));

        Assert.Equal(3, either.Total);
        Assert.Equal(1, both.Total);
        Assert.Equal("Pulse", Assert.Single(both.Hits).Name);
    }

    /// <summary>
    /// Pulse comes in red and in a 40, but not in a red 40. Matching the pair on
    /// one variant document is why the index holds variants (ADR 0015).
    /// </summary>
    [Fact]
    public async Task Two_axes_are_matched_on_the_same_variant()
    {
        var red40 = await SearchAsync(Browse("SHOES", new SearchFilters { Facets = ["COLOR:RED", "SIZE:40"] }));
        Assert.Equal("Trail", Assert.Single(red40.Hits).Name);

        // Pulse's black 42 is out of stock; Camino's is not, and it is the card's variant.
        var black42 = await SearchAsync(Browse("SHOES", new SearchFilters { Facets = ["COLOR:BLACK", "SIZE:42"], InStock = true }));
        var hit = Assert.Single(black42.Hits);
        Assert.Equal(("Camino", "CAMINO-BLACK-42"), (hit.Name, hit.MatchedSku));
        Assert.Equal(1, black42.Total);
    }

    // ---------- Price ----------

    [Fact]
    public async Task A_price_range_narrows_the_total_and_the_price_facet_keeps_the_whole_range()
    {
        var page = await SearchAsync(Browse(filters: new SearchFilters { MinPrice = 30, MaxPrice = 100 }));

        // Pulse, Trail, Camino through its 34.90 variant, and the Pañuelo.
        Assert.Equal(4, page.Total);

        var price = Price(page);
        Assert.Equal((18.50m, 119m, (decimal?)30m, (decimal?)100m), (price.Min, price.Max, price.ChosenMin, price.ChosenMax));
    }

    [Fact]
    public async Task Each_price_band_counts_the_products_that_choosing_it_would_leave()
    {
        var page = await SearchAsync(Browse());
        var price = Price(page);

        Assert.InRange(price.Bands.Count, 2, PriceBands.MaxBands);

        foreach (var band in price.Bands)
        {
            Assert.Equal(ProductsPricedWithin(band.From, band.To), band.Count);

            var chosen = await SearchAsync(Browse(filters: new SearchFilters { MinPrice = band.From, MaxPrice = band.To }));
            Assert.Equal(band.Count, chosen.Total);
            Assert.Contains(Price(chosen).Bands, other => other.Chosen && other.From == band.From && other.To == band.To);
        }
    }

    // ---------- The switches ----------

    [Fact]
    public async Task On_offer_is_what_the_running_offers_reach_asked_when_the_search_runs()
    {
        var browse = await SearchAsync(Browse());
        Assert.Equal(4, Options(browse, FacetCodes.Offer).Single().Count);

        var onOffer = await SearchAsync(Browse(filters: new SearchFilters { OnOffer = true }));
        Assert.Equal(4, onOffer.Total);
        Assert.Contains(onOffer.Hits, hit => hit.MatchedSku == "CAZO-1");
    }

    [Fact]
    public async Task New_arrivals_and_in_stock_are_switches_with_their_counts()
    {
        var page = await SearchAsync(Browse());

        Assert.Equal(1, Options(page, FacetCodes.New).Single().Count);
        Assert.Equal(6, Options(page, FacetCodes.InStock).Single().Count);
        Assert.Equal(1, (await SearchAsync(Browse(filters: new SearchFilters { NewArrivals = true }))).Total);
    }

    [Fact]
    public async Task A_yes_or_no_attribute_is_a_switch_counting_the_yes()
    {
        var cookware = await SearchAsync(Browse("COOKWARE"));

        var induction = cookware.Facets!.Single(facet => facet.Code == "INDUCTION");
        Assert.Equal(FacetKind.Flag, induction.Kind);
        Assert.Equal(("INDUCTION:true", 2L), (induction.Options.Single().Value, induction.Options.Single().Count));
    }

    // ---------- Hybrid: the union ----------

    /// <summary>
    /// Words find the pan; meaning brings the pot and the saucepan. The filters
    /// (Kuisina, induction) keep the pan and the pot, and the page counts exactly
    /// those two — the union, narrowed — with the saucepan neither listed nor counted.
    /// </summary>
    [Fact]
    public async Task A_hybrid_search_counts_what_words_and_meaning_found_narrowed_by_the_filters()
    {
        await EnsureSeededAsync();
        var ids = await ProductIdsAsync();
        var lexical = Lexical();
        var vectors = new NearestFirst(ids["Olla"], ids["Cazo"], ids["Pulse"]);
        var hybrid = new HybridSearchStrategy(lexical, vectors, Cards(), new NoReranker(),
            Microsoft.Extensions.Options.Options.Create(new HybridSearchOptions()), NullLogger<HybridSearchStrategy>.Instance);

        var filters = new SearchFilters { Brands = ["Kuisina"], Facets = ["INDUCTION:true"] };
        var page = await hybrid.SearchAsync(new ProductSearchQuery("sartén", "es", Filters: filters), Ct);

        Assert.Equal(["Olla", "Sartén"], page.Hits.Select(hit => hit.Name).Order());
        Assert.Equal(2, page.Total);
        Assert.Contains(Options(page, FacetCodes.Brand), option => option is { Value: "Kuisina", Count: 2 });

        // Without filters the union is the pan and all three meaning found.
        var unfiltered = await hybrid.SearchAsync(new ProductSearchQuery("sartén", "es"), Ct);
        Assert.Equal(4, unfiltered.Total);
    }

    [Fact]
    public async Task What_only_meaning_found_is_counted_with_a_score_of_zero_and_the_words_keep_theirs()
    {
        await EnsureSeededAsync();
        var ids = await ProductIdsAsync();

        var page = await Lexical().SearchAsync(new ProductSearchQuery("sartén", "es", AlsoMatching: [ids["Olla"]]), Ct);

        Assert.Equal(2, page.Total);
        Assert.True(page.Hits.Single(hit => hit.Name == "Sartén").Score > 0);
        Assert.Equal(0, page.Hits.Single(hit => hit.Name == "Olla").Score);
    }

    // ---------- How long it takes ----------

    [Fact]
    public async Task A_faceted_browse_and_a_faceted_text_search_answer_in_time()
    {
        var browse = Browse("SHOES", new SearchFilters { Brands = ["Vertex"], Facets = ["COLOR:RED"], MaxPrice = 100 });
        var text = new ProductSearchQuery("zapatillas", "es", Filters: new SearchFilters { InStock = true, Facets = ["SIZE:42"] });

        var browseMs = await MedianMsAsync(browse);
        var textMs = await MedianMsAsync(text);

        TestContext.Current.SendDiagnosticMessage($"Faceted browse: {browseMs:F1} ms; faceted text search: {textMs:F1} ms (median of 20, warm).");
        Console.WriteLine($"[facets timing] browse {browseMs:F1} ms, text {textMs:F1} ms");

        Assert.True(browseMs < 1000, $"browse took {browseMs} ms");
        Assert.True(textMs < 1000, $"text search took {textMs} ms");
    }

    private async Task<double> MedianMsAsync(ProductSearchQuery query)
    {
        await SearchAsync(query);

        var times = new List<double>();
        for (var run = 0; run < 20; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            await Lexical().SearchAsync(query, Ct);
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        return times.Order().ElementAt(times.Count / 2);
    }

    // ---------- The catalogue ----------

    private static ProductSearchQuery Browse(string? category = null, SearchFilters? filters = null) =>
        new("", "es", PageSize: 50, Category: category, Filters: filters);

    private async Task<SearchResultPage> SearchAsync(ProductSearchQuery query)
    {
        await EnsureSeededAsync();
        return await Lexical().SearchAsync(query, Ct);
    }

    private static IReadOnlyList<FacetOption> Options(SearchResultPage page, string code) =>
        page.Facets!.Single(facet => facet.Code == code).Options;

    private static PriceFacetRange Price(SearchResultPage page) =>
        page.Facets!.Single(facet => facet.Kind == FacetKind.Price).Price!;

    private static readonly (string Name, decimal[] Prices)[] Priced =
    [
        ("Pulse", [49.90m]), ("Trail", [89.90m]), ("Camino", [29.90m, 34.90m]),
        ("Sartén", [24.90m]), ("Olla", [119m]), ("Cazo", [18.50m]), ("Pañuelo", [39.90m]),
    ];

    private static long ProductsPricedWithin(decimal? from, decimal? to) =>
        Priced.Count(product => product.Prices.Any(price => (from is null || price >= from) && (to is null || price <= to)));

    private ElasticsearchLexicalSearch Lexical() =>
        new(elasticsearch.Client, new Definitions(), new Categories(), new FixedOffers(Offers), Clock);

    private ElasticsearchHitReader Cards() => new(elasticsearch.Client, new FixedOffers(Offers), Clock);

    private async Task<Dictionary<string, string>> ProductIdsAsync()
    {
        var page = await Lexical().SearchAsync(Browse(), Ct);
        return page.Hits.ToDictionary(hit => hit.Name, hit => hit.ProductId);
    }

    private async Task EnsureSeededAsync()
    {
        elasticsearch.SkipIfUnavailable();

        await Seeding.WaitAsync(Ct);
        try
        {
            if (seeded == elasticsearch)
                return;

            await SearchIndexes.CreateAsync(elasticsearch.Client, "es", "spanish", Ct);

            var fresh = Clock.GetUtcNow().AddDays(-10);
            var old = new DateOnly(2025, 6, 1);

            ProductSearchDocument[] documents =
            [
                .. Product("Pulse", "Vertex", Shoes, DateOnly.FromDateTime(fresh.UtcDateTime), ["MATERIAL:MESH"],
                    ("BLACK", "40", 49.90m, true), ("BLACK", "42", 49.90m, false), ("RED", "42", 49.90m, true)),
                .. Product("Trail", "Vertex", Shoes, old, ["MATERIAL:LEATHER"],
                    ("RED", "40", 89.90m, true), ("RED", "42", 89.90m, true)),
                .. Product("Camino", "Caminante", Shoes, old, ["MATERIAL:MESH"],
                    ("BLACK", "40", 29.90m, true), ("BLACK", "42", 34.90m, true)),
                .. Product("Sartén", "Kuisina", Cookware, old, ["INDUCTION:true", "MATERIAL:ALUMINIUM"], (null, null, 24.90m, true)),
                .. Product("Olla", "Kuisina", Cookware, old, ["INDUCTION:true", "MATERIAL:CAST_IRON"], (null, null, 119m, false)),
                .. Product("Cazo", "Kuisina", Cookware, old, ["INDUCTION:false", "MATERIAL:ALUMINIUM"], (null, null, 18.50m, true)),
                .. Product("Pañuelo", "Lanar", Accessories, old, ["COLOR:BLACK"], (null, null, 39.90m, true)),
            ];

            var response = await elasticsearch.Client.BulkAsync(bulk => bulk
                .Index(ProductSearchDocument.IndexNameFor("es"))
                .IndexMany(documents, (operation, document) => operation.Id(document.Id)), Ct);
            Assert.True(response.IsValidResponse, response.DebugInformation);

            await elasticsearch.Client.Indices.RefreshAsync(ProductSearchDocument.IndexNameFor("es"), cancellationToken: Ct);
            seeded = elasticsearch;
        }
        finally
        {
            Seeding.Release();
        }
    }

    private static readonly string[] Shoes = ["APPAREL", "SHOES"];
    private static readonly string[] Cookware = ["HOME", "KITCHEN", "COOKWARE"];
    private static readonly string[] Accessories = ["APPAREL", "ACCESSORIES"];

    private static IEnumerable<ProductSearchDocument> Product(
        string name, string brand, string[] branch, DateOnly availableFrom, string[] facts,
        params (string? Colour, string? Size, decimal Price, bool InStock)[] variants)
    {
        var productId = Guid.CreateVersion7().ToString();
        var from = variants.Min(variant => variant.Price);
        var to = variants.Max(variant => variant.Price);

        return variants.Select(variant =>
        {
            var sku = variant.Colour is null
                ? (name == "Cazo" ? "CAZO-1" : name.ToUpperInvariant())
                : $"{name.ToUpperInvariant()}-{variant.Colour}-{variant.Size}";

            return new ProductSearchDocument
            {
                Id = Guid.CreateVersion7().ToString(),
                ProductId = productId,
                Sku = sku,
                Culture = "es",
                Name = name,
                Brand = brand,
                Category = branch[^1],
                CategoryCodes = branch,
                AvailableFrom = availableFrom,
                Slug = name.ToLowerInvariant(),
                Code = name.ToUpperInvariant(),
                AxisValues = variant.Colour is null ? [] : [$"COLOR:{variant.Colour}", $"SIZE:{variant.Size}"],
                FacetValues = facts,
                PriceAmount = variant.Price,
                InStock = variant.InStock,
                PriceFrom = from,
                PriceTo = to,
                PriceCurrency = "EUR",
                Status = "active",
            };
        });
    }

    private static LocalizedText Text(string es, string en) => new(new Dictionary<string, string> { ["es"] = es, ["en"] = en });

    private sealed class Definitions : IAttributeDefinitionReader
    {
        public Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default)
        {
            var color = AttributeDefinition.Define(Clock, "COLOR", Text("color", "colour"), AttributeKind.Option, isVariantAxis: true, isFacet: true);
            color.AddOption(Clock, "BLACK", Text("negro", "black"));
            color.AddOption(Clock, "RED", Text("rojo", "red"));

            var size = AttributeDefinition.Define(Clock, "SIZE", Text("talla", "size"), AttributeKind.Option, isVariantAxis: true, isFacet: true);
            size.AddOption(Clock, "40", Text("40", "40"));
            size.AddOption(Clock, "42", Text("42", "42"));

            var material = AttributeDefinition.Define(Clock, "MATERIAL", Text("material", "material"), AttributeKind.Option, isFacet: true);
            material.AddOption(Clock, "MESH", Text("malla", "mesh"));
            material.AddOption(Clock, "LEATHER", Text("piel", "leather"));
            material.AddOption(Clock, "ALUMINIUM", Text("aluminio", "aluminium"));
            material.AddOption(Clock, "CAST_IRON", Text("hierro fundido", "cast iron"));

            var induction = AttributeDefinition.Define(Clock, "INDUCTION", Text("inducción", "induction"), AttributeKind.Boolean, isFacet: true);

            return Task.FromResult(new AttributeDefinitions([color, size, material, induction]));
        }
    }

    private sealed class Categories : ICategoryReader
    {
        public Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default)
        {
            var apparel = Category.Define(Clock, "APPAREL", Text("Ropa", "Clothing"));
            var shoes = Category.Define(Clock, "SHOES", Text("Zapatillas", "Shoes"), apparel);
            var home = Category.Define(Clock, "HOME", Text("Hogar", "Home"));
            var kitchen = Category.Define(Clock, "KITCHEN", Text("Cocina", "Kitchen"), home);
            var cookware = Category.Define(Clock, "COOKWARE", Text("Menaje de cocina", "Cookware"), kitchen);

            return Task.FromResult(new CategoryTree([apparel, shoes, home, kitchen, cookware]));
        }
    }

    private sealed class FixedOffers(OfferScope scope) : IRunningOffers
    {
        public Task<OfferScope> NowAsync(CancellationToken ct = default) => Task.FromResult(scope);
    }

    /// <summary>A vector side that found these products, nearest first — whatever the filters say.</summary>
    private sealed class NearestFirst(params string[] productIds) : IVectorProductSearch
    {
        public Task<IReadOnlyList<VectorMatch>> SearchAsync(
            string text, string culture, string? category, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VectorMatch>>([.. productIds.Select((id, rank) => new VectorMatch(id, 0.9 - rank * 0.1))]);

        public Task<IReadOnlyDictionary<string, string>> TextsAsync(
            IReadOnlyCollection<string> productIds, string culture, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }
}
