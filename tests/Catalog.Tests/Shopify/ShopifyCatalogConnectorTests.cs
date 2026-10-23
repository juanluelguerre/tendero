using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Connectors.Shopify;
using ElGuerre.Tendero.Catalog.Tests.Connectors;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Shopify;

/// <summary>
/// The Shopify adapter of the inbound port (phase 16, P16-4) passes the same
/// contract the seed does — the promise article 02 made, meeting its first real
/// source.
/// </summary>
public sealed class ShopifyCatalogConnectorContractTests : CatalogSourceConnectorContractTests
{
    protected override ICatalogSourceConnector CreateConnector() => ShopifyCatalogConnectorTests.AConnector().Connector;

    protected override int MinimumExpectedProducts => 2;
}

/// <summary>
/// The source exists only when a store is configured (spec, *The degraded
/// path*): a fresh clone has no Shopify, and asking for it is the import's
/// ordinary 400 naming the sources there are.
/// </summary>
public sealed class ShopifySourceRegistrationTests
{
    private static IReadOnlyCollection<string> Sources(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatalog(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider().CreateScope().ServiceProvider
            .GetRequiredService<ElGuerre.Tendero.Catalog.Ports.ICatalogSourceRegistry>().Sources;
    }

    [Fact]
    public void Without_a_stores_credentials_shopify_is_not_a_source() =>
        Assert.Equal(["seed"], Sources([]));

    [Fact]
    public void With_them_it_is()
    {
        var categories = Path.Combine(Path.GetTempPath(), $"shopify-categories-{Guid.NewGuid():N}.json");
        File.WriteAllText(categories, """{ "gid://shopify/TaxonomyCategory/hg-11-1-3": "COFFEE_MAKER" }""");
        try
        {
            var settings = new Dictionary<string, string?>
            {
                ["Shopify:Store"] = FakeShopify.Store,
                ["Shopify:ClientId"] = "client-id",
                ["Shopify:ClientSecret"] = "client-secret",
                ["Shopify:CategoriesFile"] = categories,
            };

            Assert.Equal(["seed", "shopify"], Sources(settings));

            var services = new ServiceCollection();
            services.AddCatalog(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
            var options = services.BuildServiceProvider().GetRequiredService<IOptions<ShopifyOptions>>().Value;
            Assert.Equal("COFFEE_MAKER", options.Categories["gid://shopify/TaxonomyCategory/hg-11-1-3"]);
        }
        finally
        {
            File.Delete(categories);
        }
    }
}

/// <summary>
/// How a store's product becomes an <see cref="ExternalProduct"/>
/// (docs/specs/shopify-import.md, *Mapping*): the store's language as the base and
/// the translations it publishes in the languages Tendero speaks, HTML as plain
/// text, the taxonomy through the mapping file, the lowest variant price, and each
/// variant with the identity the store gave it.
/// </summary>
public sealed class ShopifyCatalogConnectorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static (ShopifyCatalogConnector Connector, FakeShopify Store) AConnector(Action<FakeShopify>? arrange = null, bool mapped = true)
    {
        var store = new FakeShopify();
        arrange?.Invoke(store);
        var clock = new TestClock();
        var options = Options.Create(new ShopifyOptions
        {
            Store = FakeShopify.Store,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            BulkPollInterval = TimeSpan.Zero,
            ThrottleBackoff = TimeSpan.Zero,
            Categories = mapped
                ? new Dictionary<string, string> { ["gid://shopify/TaxonomyCategory/hg-11-1-3"] = "COFFEE_MAKER" }
                : [],
        });
        var http = new HttpClient(store);
        var admin = new ShopifyAdmin(http, new ShopifyAccessTokens(http, options, clock), options, clock);
        return (new ShopifyCatalogConnector(admin, options), store);
    }

    private static async Task<List<ExternalProduct>> Imported(ShopifyCatalogConnector connector)
    {
        var products = new List<ExternalProduct>();
        await foreach (var product in connector.StreamProductsAsync(Ct))
            products.Add(product);
        return products;
    }

    [Fact]
    public async Task A_stores_product_comes_with_its_texts_in_both_languages_as_plain_text()
    {
        var moka = (await Imported(AConnector().Connector))[0];

        Assert.Equal(("8001", "cafetera-moka", "Moka"), (moka.ExternalId, moka.Handle, moka.Brand));
        Assert.Equal(new Dictionary<string, string> { ["es"] = "Cafetera Moka", ["en"] = "Moka coffee maker" }, moka.Names);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["es"] = "De goteo, para diez tazas.\n\nJarra de vidrio.",
                ["en"] = "Drip, for ten cups.\n\nGlass jug.",
            },
            moka.Descriptions);
    }

    /// <summary>
    /// Only the languages Tendero speaks are asked for: the store's French is
    /// never read (and the import's notes will say so, P16-5).
    /// </summary>
    [Fact]
    public async Task Only_the_languages_tendero_speaks_are_asked_for()
    {
        var (connector, store) = AConnector();

        await Imported(connector);

        Assert.Contains("t_en: translations", store.LastBulkQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("t_fr", store.LastBulkQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("t_es", store.LastBulkQuery, StringComparison.Ordinal);
    }

    /// <summary>
    /// A store whose language is written with its region — <c>en-US</c> — is
    /// still English to the catalogue, whose texts are keyed by language alone;
    /// its Spanish is asked for under the language's alias, since a GraphQL alias
    /// cannot carry the hyphen.
    /// </summary>
    [Fact]
    public async Task A_store_language_with_a_region_is_keyed_by_the_language()
    {
        var (connector, store) = AConnector(store =>
            store.ShopLocales = """[{"locale":"en-US","primary":true},{"locale":"es-ES","primary":false}]""");

        var moka = (await Imported(connector))[0];

        Assert.Equal(["en"], moka.Names.Keys);
        Assert.Contains("t_es: translations(locale: \\\"es-ES\\\")", store.LastBulkQuery ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Its_category_price_and_date_come_from_the_store_and_the_mapping()
    {
        var products = await Imported(AConnector().Connector);

        Assert.Equal(("COFFEE_MAKER", 29.90m, "EUR"), (products[0].Category, products[0].PriceAmount, products[0].PriceCurrency));
        Assert.Equal(new DateOnly(2026, 9, 1), products[0].AvailableFrom);
        Assert.Equal((null, 9.00m, (DateOnly?)null), (products[1].Category, products[1].PriceAmount, products[1].AvailableFrom));
    }

    /// <summary>
    /// A store keeps a product's attributes in metafields; the ones under the
    /// configured namespace come as the product's attributes, in the store's
    /// words, resolved later against the definitions like the seed's (spec
    /// decision 6, amended 2026-10-23: without them the round trip would lose
    /// what search reads).
    /// </summary>
    [Fact]
    public async Task The_metafields_of_the_configured_namespace_come_as_its_attributes()
    {
        var products = await Imported(AConnector().Connector);

        Assert.Equal(new Dictionary<string, string> { ["material"] = "vidrio y acero", ["capacidad_tazas"] = "10" }, products[0].Attributes);
        Assert.Empty(products[1].Attributes);
    }

    /// <summary>
    /// Each variant carries what the store knows it by — its id, SKU, barcode and
    /// price — and what it varies by, in the store's words; a picture a variant
    /// names shows that variant's options.
    /// </summary>
    [Fact]
    public async Task Each_variant_comes_with_its_identity_and_each_picture_with_what_it_shows()
    {
        var moka = (await Imported(AConnector().Connector))[0];

        Assert.Equal(
            [("44001", "44001", "MOKA-OLV", "8400000000011", 29.90m, "Oliva"), ("44002", "44002", "MOKA-BLK", "8400000000028", 32.50m, "Negro")],
            moka.Variants!.Select(variant => (variant.Key, variant.ExternalId, variant.Sku, variant.Barcode, variant.PriceAmount, variant.Attributes["Color"])));
        Assert.Equal(
            [("https://cdn.shopify.com/s/files/1/moka-oliva.jpg", "Cafetera Moka en oliva", "Oliva"), ("https://cdn.shopify.com/s/files/1/moka-negro.jpg", "Cafetera Moka en negro", "Negro")],
            moka.Images.Select(image => (image.Location.ToString(), image.Alt!["es"], image.Shows!["Color"])));
    }

    /// <summary>
    /// What did not come across cleanly travels as notes (P16-5): on the product
    /// it concerns, or on the source as a whole once the stream has been read.
    /// The mug's list became lines and its one variant had no SKU; the store's
    /// French was never read.
    /// </summary>
    [Fact]
    public async Task What_did_not_come_across_cleanly_is_noted_where_it_belongs()
    {
        var (connector, _) = AConnector();

        var products = await Imported(connector);

        Assert.Null(products[0].Notes);
        Assert.Equal(
            [new SourceNote(ImportNoteCode.DescriptionHadLayout, "8002", null), new SourceNote(ImportNoteCode.VariantWithoutSku, "8002", "44003")],
            products[1].Notes);
        Assert.Equal([new SourceNote(ImportNoteCode.LocaleIgnored, null, "fr")], connector.SourceNotes);
    }

    [Fact]
    public async Task A_category_the_mapping_does_not_know_is_noted_with_its_name()
    {
        var (connector, _) = AConnector(mapped: false);

        var moka = (await Imported(connector))[0];

        Assert.Null(moka.Category);
        Assert.Equal(
            [new SourceNote(ImportNoteCode.CategoryNotMapped, "8001", "Home & Garden > Kitchen & Dining > Kitchen Appliances > Coffee Makers")],
            moka.Notes);
    }

    [Fact]
    public async Task Variants_that_share_a_sku_at_the_source_are_noted()
    {
        var (connector, _) = AConnector(store => store.ResultsFixture = "shared-sku.jsonl");

        var lamp = Assert.Single(await Imported(connector));

        Assert.Equal([new SourceNote(ImportNoteCode.SkuSharedAtSource, "8101", "FLEXO")], lamp.Notes);
    }

    /// <summary>
    /// A product sold one way has Shopify's "Default Title" variant: it comes as
    /// one variant that varies by nothing, with the store's id for it, so even a
    /// product with no options is found again by id.
    /// </summary>
    [Fact]
    public async Task A_product_sold_one_way_comes_as_one_variant_that_varies_by_nothing()
    {
        var mug = (await Imported(AConnector().Connector))[1];

        var variant = Assert.Single(mug.Variants!);
        Assert.Equal(("44003", null, 9.00m), (variant.ExternalId, variant.Sku, variant.PriceAmount));
        Assert.Empty(variant.Attributes);
        Assert.Equal("Gres esmaltado\n350 ml", mug.Descriptions!["es"]);
    }
}
