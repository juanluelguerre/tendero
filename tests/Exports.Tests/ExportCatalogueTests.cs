using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Exports.Features.CatalogueForAnotherPlatform;
using ElGuerre.Tendero.Exports.Features.ProductFeed;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Exports.Tests;

/// <summary>
/// The catalogue for another platform (E-3; spec, *The catalogue for another
/// platform*): every live product, on sale or in Draft, as another store imports it
/// — the proof that a merchant can leave Tendero as they left Shopify.
/// </summary>
public sealed class ExportCatalogueTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ExportedProduct Shirt() => new(
        "B07TUVW404", true, "Camiseta técnica", "camiseta-tecnica", "Transpirable.", "Trail Co",
        [new("APPAREL", "Ropa"), new("SHIRT", "Camisetas")],
        [new("front", "De frente"), new("back", null)],
        [],
        [
            new("SHIRT-NAVY-M", "8412345678905", new Money(24.90m, "EUR"), null,
                [new("COLOR", "color", "NAVY_BLUE", "azul marino"), new("SIZE", "talla", "M", "M")], ["navy", "front"]),
            new("SHIRT-BLACK-M", null, new Money(26.90m, "EUR"), null,
                [new("COLOR", "color", "BLACK", "negro"), new("SIZE", "talla", "M", "M")], []),
        ]);

    private static ExportedProduct Draft() => new(
        "B99DRAFT01", false, "Taza", "taza", null, null, [], [], [],
        [new("MUG-DEFAULT", null, new Money(10m, "EUR"), null, [], [])]);

    [Fact]
    public async Task Every_live_product_leaves_as_another_store_imports_it()
    {
        var formats = new CapturingFormats();
        var handler = new ExportCatalogueHandler(new Catalogue([Shirt(), Draft()]), new Shelves(new() { ["SHIRT-NAVY-M"] = 3, ["MUG-DEFAULT"] = 7 }),
            new GoogleCategoryMapping(new Dictionary<string, int> { ["SHIRT"] = 212 }), formats);

        var file = await handler.HandleAsync(new ExportCatalogueCommand("shopify-csv", "es", "https://api.example"), Ct);

        Assert.Equal((2, "text/csv; charset=utf-8", "catalogue-es.csv"), (file.Rows, file.ContentType, file.FileName));
        var (shirt, mug) = (formats.Written[0], formats.Written[1]);
        Assert.Equal(("camiseta-tecnica-b07tuvw404", "Camiseta técnica", "Transpirable.", "Trail Co", "Camisetas", true, 212),
            (shirt.Handle, shirt.Title, shirt.Description, shirt.Vendor, shirt.Type, shirt.OnSale, shirt.GoogleProductCategory));
        Assert.Equal(["color", "talla"], shirt.OptionNames);
        Assert.Equal(
            [
                new PictureForAnotherPlatform("https://api.example/api/images/front", "De frente"),
                new PictureForAnotherPlatform("https://api.example/api/images/back", null),
            ],
            shirt.Pictures);
        Assert.Equal(("taza-b99draft01", false, (string?)null, (int?)null), (mug.Handle, mug.OnSale, mug.Type, mug.GoogleProductCategory));
        Assert.Empty(mug.OptionNames);
    }

    /// <summary>Each variant with its options' labels, its barcode, what can still be sold of it, and its own first picture.</summary>
    [Fact]
    public async Task A_variant_leaves_with_what_can_still_be_sold_of_it()
    {
        var formats = new CapturingFormats();
        var handler = new ExportCatalogueHandler(new Catalogue([Shirt()]), new Shelves(new() { ["SHIRT-NAVY-M"] = 3 }), GoogleCategoryMapping.None, formats);

        await handler.HandleAsync(new ExportCatalogueCommand("shopify-csv", "es", "https://api.example/"), Ct);

        Assert.Equal(
            [
                "SHIRT-NAVY-M 8412345678905 24.90 EUR [azul marino/M] 3 https://api.example/api/images/navy",
                "SHIRT-BLACK-M - 26.90 EUR [negro/M] 0 -",
            ],
            formats.Written[0].Variants.Select(variant =>
                $"{variant.Sku} {variant.Barcode ?? "-"} {variant.Price} [{string.Join('/', variant.OptionValues)}] {variant.Inventory} {variant.PictureUrl ?? "-"}"));
    }

    /// <summary>
    /// A product every variant of which was discontinued leaves with none, and no
    /// options: the catalogue's export leaves those variants out. Found by the
    /// exports' Stryker close, where taking the first variant without asking survived.
    /// </summary>
    [Fact]
    public async Task A_product_with_nothing_left_to_sell_leaves_without_variants_or_options()
    {
        var formats = new CapturingFormats();
        var handler = new ExportCatalogueHandler(new Catalogue([Draft() with { Variants = [] }]), new Shelves([]), GoogleCategoryMapping.None, formats);

        await handler.HandleAsync(new ExportCatalogueCommand("shopify-csv", "es", "https://api.example"), Ct);

        Assert.Equal((0, 0), (formats.Written[0].Variants.Count, formats.Written[0].OptionNames.Count));
    }

    /// <summary>A format nobody registered is refused before anything is read.</summary>
    [Theory]
    [InlineData("shopify-csv", "es", true)]
    [InlineData("woocommerce-csv", "es", false)]
    [InlineData("shopify-csv", "fr", false)]
    public void Only_a_known_format_in_a_language_the_shop_speaks_is_asked_for(string format, string culture, bool valid) =>
        Assert.Equal(valid, new ExportCatalogueValidator(new CapturingFormats()).Validate(new ExportCatalogueCommand(format, culture, "https://api.example")).IsValid);

    private sealed class Catalogue(ExportedProduct[] products) : ICatalogueExport
    {
        public Task<IReadOnlyList<ExportedProduct>> ProductsAsync(string culture, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExportedProduct>>(products);
    }

    private sealed class Shelves(Dictionary<string, int> available) : IAvailabilityReader
    {
        public Task<IReadOnlyDictionary<string, int>> AvailableAsync(IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, int>>(skus.Where(available.ContainsKey).ToDictionary(sku => sku, sku => available[sku]));
    }

    /// <summary>Shopify's format, keeping what it was given to write.</summary>
    private sealed class CapturingFormats : IExportFormats, IExportFormat<ProductForAnotherPlatform>
    {
        public List<ProductForAnotherPlatform> Written { get; } = [];

        public string Format => ShopifyProductCsv.Key;

        public string ContentType => "text/csv; charset=utf-8";

        public string Extension => "csv";

        public IExportFormat<TRow>? Find<TRow>(string format) =>
            format == ShopifyProductCsv.Key && this is IExportFormat<TRow> found ? found : null;

        public Task WriteAsync(IReadOnlyList<ProductForAnotherPlatform> rows, ExportWriting writing, Stream output, CancellationToken cancellationToken = default)
        {
            Written.AddRange(rows);
            return Task.CompletedTask;
        }
    }
}
