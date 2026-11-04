using System.Text;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Exports.Features.ProductFeed;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Exports.Tests;

/// <summary>
/// The product feed (E-2; spec, *The product feed*): one row per variant on sale,
/// grouped by its product's code, linked to its page in the feed's culture, with its
/// pictures, its price, whether it can be bought now, and Google's category from the
/// mapping a person wrote — the nearest one up the branch when its own has none.
/// </summary>
public sealed class ProductFeedTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ExportedProduct Shirt() => new(
        "B07TUVW404", true, "Camiseta técnica", "camiseta-tecnica", "Transpirable.", "Trail Co",
        [new("APPAREL", "Ropa"), new("SPORTSWEAR", "Ropa deportiva"), new("SHIRT", "Camisetas")],
        [new("front", "De frente"), new("back", null)],
        [new("GENDER", "género", "WOMEN", "mujer")],
        [
            new("SHIRT-NAVY-M", "8412345678905", new Money(24.90m, "EUR"), null,
                [new("COLOR", "color", "NAVY_BLUE", "azul marino"), new("SIZE", "talla", "M", "M")], ["navy-front", "front", "back"]),
            new("SHIRT-BLACK-M", null, new Money(26.90m, "EUR"), null,
                [new("COLOR", "color", "BLACK", "negro"), new("SIZE", "talla", "M", "M")], []),
        ]);

    private static ExportedProduct Moka() => new(
        "B11COF0401", true, "Cafetera italiana", "cafetera-italiana", null, null,
        [new("HOME", "Hogar"), new("KITCHEN", "Cocina"), new("COFFEE_MAKER", "Cafeteras")], [],
        [new("COLOR", "color", "SILVER", "plata")],
        [new("MOKA-DEFAULT", null, new Money(29.90m, "EUR"), null, [], [])]);

    private static ExportedProduct Draft() => new(
        "B99DRAFT01", false, "Borrador", "borrador", null, null, [], [], [],
        [new("DRAFT-DEFAULT", null, new Money(1m, "EUR"), null, [], [])]);

    private static ProductFeedHandler Handler(params ExportedProduct[] products) => new(
        new Catalogue(products),
        new Shelves(new Dictionary<string, int> { ["SHIRT-NAVY-M"] = 3 }),
        new GoogleCategoryMapping(new Dictionary<string, int> { ["SHIRT"] = 212, ["KITCHEN"] = 638 }),
        Options.Create(new ExportsOptions { StorefrontUrl = "https://shop.example/" }),
        new FixedFormats());

    private static async Task<(ProductFeedFile File, List<string[]> Lines)> Feed(string culture, params ExportedProduct[] products)
    {
        var file = await Handler(products).HandleAsync(new ProductFeedQuery(culture, "https://api.example"), Ct);
        var lines = Encoding.UTF8.GetString(file.Content).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('\t')).ToList();
        return (file, lines);
    }

    private static string Cell(List<string[]> lines, int row, string column) => lines[row][Array.IndexOf(lines[0], column)];

    [Fact]
    public async Task Each_variant_on_sale_is_a_row_grouped_by_its_products_code()
    {
        var (file, lines) = await Feed("es", Shirt(), Draft(), Moka());

        Assert.Equal(["SHIRT-NAVY-M", "SHIRT-BLACK-M", "MOKA-DEFAULT"], lines.Skip(1).Select(line => line[0]));
        Assert.Equal(["B07TUVW404", "B07TUVW404", "B11COF0401"], lines.Skip(1).Select(line => line[1]));
        Assert.Equal((3, "text/tab-separated-values; charset=utf-8", "google-es.tsv"), (file.Rows, file.ContentType, file.FileName));
    }

    /// <summary>A variant's title says what tells it apart; its link is its product's page in the feed's culture.</summary>
    [Fact]
    public async Task A_variant_is_named_by_what_tells_it_apart_and_linked_to_its_page()
    {
        var (_, lines) = await Feed("en", Shirt(), Moka());

        Assert.Equal("Camiseta técnica - azul marino / M", Cell(lines, 1, "title"));
        Assert.Equal("Cafetera italiana", Cell(lines, 3, "title"));
        Assert.Equal("https://shop.example/en/p/camiseta-tecnica/B07TUVW404", Cell(lines, 1, "link"));
    }

    /// <summary>A variant's own pictures first, then its gallery; one with none of its own shows its product's; one with no picture at all has none.</summary>
    [Fact]
    public async Task A_variants_pictures_are_its_own_gallery_or_its_products()
    {
        var (_, lines) = await Feed("es", Shirt(), Moka());

        Assert.Equal("https://api.example/api/images/navy-front", Cell(lines, 1, "image_link"));
        Assert.Equal("https://api.example/api/images/front,https://api.example/api/images/back", Cell(lines, 1, "additional_image_link"));
        Assert.Equal(("https://api.example/api/images/front", "https://api.example/api/images/back"), (Cell(lines, 2, "image_link"), Cell(lines, 2, "additional_image_link")));
        Assert.Equal("", Cell(lines, 3, "image_link"));
    }

    [Fact]
    public async Task What_can_be_bought_now_is_in_stock_and_the_rest_is_out()
    {
        var (_, lines) = await Feed("es", Shirt());

        Assert.Equal(["in_stock", "out_of_stock"], lines.Skip(1).Select(line => Cell(lines, lines.IndexOf(line), "availability")));
    }

    /// <summary>The category's own mapping, or the nearest one up its branch; none is counted, not guessed.</summary>
    [Fact]
    public async Task Googles_category_is_the_nearest_one_mapped_up_the_branch()
    {
        var (file, lines) = await Feed("es", Shirt(), Moka(), Draft());

        Assert.Equal(("212", "638"), (Cell(lines, 1, "google_product_category"), Cell(lines, 3, "google_product_category")));
        Assert.Equal("Ropa > Ropa deportiva > Camisetas", Cell(lines, 1, "product_type"));
        Assert.Equal(0, file.Uncategorised);

        var (lonely, _) = await Feed("es", Moka() with { Category = [new("BATHROOM", "Baño")] });
        Assert.Equal(1, lonely.Uncategorised);
    }

    /// <summary>
    /// Colour from the variant, or the product's own when it does not vary by it; size
    /// from the variant; gender in Google's words for the catalogue's; a description
    /// the product lacks is its name, since Google requires one.
    /// </summary>
    [Fact]
    public async Task Colour_size_gender_and_description_come_from_where_the_catalogue_keeps_them()
    {
        var (_, lines) = await Feed("es", Shirt(), Moka());

        Assert.Equal(("azul marino", "M", "female"), (Cell(lines, 1, "color"), Cell(lines, 1, "size"), Cell(lines, 1, "gender")));
        Assert.Equal(("plata", "", ""), (Cell(lines, 3, "color"), Cell(lines, 3, "size"), Cell(lines, 3, "gender")));
        Assert.Equal(("Transpirable.", "Cafetera italiana"), (Cell(lines, 1, "description"), Cell(lines, 3, "description")));
        Assert.Equal(("8412345678905", "24.90 EUR", "Trail Co"), (Cell(lines, 1, "gtin"), Cell(lines, 1, "price"), Cell(lines, 1, "brand")));
    }

    // The tests below were found by the exports' Stryker close: each mutant they name survived the suite above.

    /// <summary>The variant's own colour wins over the product's; a product that says no colour anywhere has none, and no error.</summary>
    [Fact]
    public async Task A_variants_colour_wins_and_a_product_without_one_has_none()
    {
        var both = Shirt() with { Attributes = [new("COLOR", "color", "GREY", "gris")] };
        var none = Moka() with { Code = "B11COF0402", Attributes = [] };

        var (_, lines) = await Feed("es", both, none);

        Assert.Equal(("azul marino", ""), (Cell(lines, 1, "color"), Cell(lines, 3, "color")));
    }

    [Theory]
    [InlineData("MEN", "male")]
    [InlineData("UNISEX", "unisex")]
    [InlineData("KIDS", "")]
    public async Task Gender_is_said_in_googles_words_or_not_at_all(string key, string expected)
    {
        var (_, lines) = await Feed("es", Shirt() with { Attributes = [new("GENDER", "género", key, key)] });

        Assert.Equal(expected, Cell(lines, 1, "gender"));
    }

    /// <summary>A branch mapped only at its root takes the root's: the nearest one up is as far up as it goes.</summary>
    [Fact]
    public void A_branch_mapped_only_at_its_root_takes_the_roots() =>
        Assert.Equal(536, new GoogleCategoryMapping(new Dictionary<string, int> { ["HOME"] = 536 })
            .Of([new("HOME", "Hogar"), new("KITCHEN", "Cocina"), new("COFFEE_MAKER", "Cafeteras")]));

    /// <summary>The defaults a configuration without the AppHost has: the storefront's development address, the committed mapping.</summary>
    [Fact]
    public void Without_configuration_the_feed_links_the_development_storefront_and_reads_the_committed_mapping()
    {
        var options = new ExportsOptions();

        Assert.Equal(("http://localhost:4200", "seed/google-categories.sample.json"), (options.StorefrontUrl, options.GoogleCategoriesFile));
    }

    [Theory]
    [InlineData("es", true)]
    [InlineData("en", true)]
    [InlineData("fr", false)]
    public void A_feed_is_written_in_a_language_the_shop_speaks(string culture, bool valid) =>
        Assert.Equal(valid, new ProductFeedValidator().Validate(new ProductFeedQuery(culture, "https://api.example")).IsValid);

    private sealed class Catalogue(ExportedProduct[] products) : ICatalogueExport
    {
        public Task<IReadOnlyList<ExportedProduct>> ProductsAsync(string culture, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExportedProduct>>(products);

        public Task<ExportedProduct?> ProductAsync(string code, string culture, CancellationToken cancellationToken = default) =>
            Task.FromResult(products.FirstOrDefault(product => product.Code == code));
    }

    private sealed class Shelves(Dictionary<string, int> available) : IAvailabilityReader
    {
        public Task<IReadOnlyDictionary<string, int>> AvailableAsync(IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, int>>(skus.Where(available.ContainsKey).ToDictionary(sku => sku, sku => available[sku]));
    }

    private sealed class FixedFormats : IExportFormats
    {
        public IExportFormat<TRow>? Find<TRow>(string format) =>
            format == GoogleMerchantFeed.Key ? (IExportFormat<TRow>)(object)new GoogleMerchantFeed() : null;
    }
}
