using System.Text;
using ElGuerre.Tendero.Exports.Features.CatalogueForAnotherPlatform;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Exports.Tests;

/// <summary>Shopify's product CSV passes the contract every format does.</summary>
public sealed class ShopifyProductCsvContractTests : ExportFormatContractTests<ProductForAnotherPlatform>
{
    protected override IExportFormat<ProductForAnotherPlatform> CreateFormat() => new ShopifyProductCsv();

    protected override IReadOnlyList<ProductForAnotherPlatform> Rows() => [ShopifyProductCsvTests.Shirt, ShopifyProductCsvTests.Moka];

    protected override string KeyColumn(string culture) => "URL handle";

    protected override string KeyOf(ProductForAnotherPlatform row) => row.Handle;

    protected override string MoneyColumn(string culture) => "Price";

    protected override Money MoneyOf(ProductForAnotherPlatform row) => row.Variants[0].Price;

    protected override string TextColumn(string culture) => "Title";

    protected override ProductForAnotherPlatform WithText(ProductForAnotherPlatform row, string text) => row with { Title = text };

    protected override char SeparatorFor(string culture) => ',';

    protected override char DecimalMarkFor(string culture) => '.';
}

/// <summary>
/// The catalogue as Shopify's product importer reads it (E-3), checked against
/// Shopify's help on 2026-10-25 (help.shopify.com/en/manual/products/import-export/using-csv):
/// the current headers (<c>URL handle</c>, <c>Barcodes</c>); a product's first line
/// carrying its own fields, its first variant and its first picture, the next lines
/// only its handle with a variant, a picture or both; a product with no options
/// written as Shopify writes one, a <c>Title</c> of <c>Default Title</c>.
/// </summary>
public sealed class ShopifyProductCsvTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static readonly ProductForAnotherPlatform Shirt = new(
        "camiseta-tecnica-b07tuvw404", "Camiseta técnica", "Transpirable & ligera.\n\nPara correr <de verdad>.", "Trail Co", "Camisetas", true,
        ["color", "talla"],
        [
            new("SHIRT-NAVY-M", "8412345678905", new Money(24.90m, "EUR"), ["azul marino", "M"], 3, "https://api.example/api/images/navy"),
            new("SHIRT-BLACK-M", null, new Money(26.90m, "EUR"), ["negro", "M"], 0, null),
        ],
        [new("https://api.example/api/images/navy", "De frente"), new("https://api.example/api/images/back", null), new("https://api.example/api/images/side", null)],
        212);

    internal static readonly ProductForAnotherPlatform Moka = new(
        "cafetera-italiana-b11cof0401", "Cafetera italiana", null, null, "Cafeteras", false, [],
        [new("MOKA-DEFAULT", null, new Money(29.90m, "EUR"), [], 5, null)], [], null);

    private static async Task<List<IReadOnlyList<string>>> Written(params ProductForAnotherPlatform[] products)
    {
        using var output = new MemoryStream();
        await new ShopifyProductCsv().WriteAsync(products, new ExportWriting("es"), output, Ct);
        return [.. Csv.Read(output.ToArray(), ',')];
    }

    private static string Cell(List<IReadOnlyList<string>> file, int line, string column) => file[line][file[0].ToList().IndexOf(column)];

    [Fact]
    public async Task The_header_is_shopifys_current_template()
    {
        var header = (await Written())[0];

        Assert.Equal(
            [
                "Title", "URL handle", "Description", "Vendor", "Type", "Tags", "Published on online store", "Status", "SKU", "Barcodes",
                "Option1 name", "Option1 value", "Option2 name", "Option2 value", "Option3 name", "Option3 value", "Price", "Charge tax",
                "Inventory tracker", "Inventory quantity", "Continue selling when out of stock", "Requires shipping", "Fulfillment service",
                "Product image URL", "Image position", "Image alt text", "Variant image URL", "Gift card", "Google Shopping / Google Product Category",
            ],
            header);
    }

    /// <summary>The first line is the product and its first variant and picture; then a line per variant or picture left, with only the handle repeated.</summary>
    [Fact]
    public async Task A_product_takes_a_line_per_variant_or_picture_and_says_its_own_fields_once()
    {
        var file = await Written(Shirt);

        Assert.Equal(4, file.Count);
        Assert.Equal(["camiseta-tecnica-b07tuvw404", "camiseta-tecnica-b07tuvw404", "camiseta-tecnica-b07tuvw404"], file.Skip(1).Select(line => line[1]));
        Assert.Equal(("Camiseta técnica", "Trail Co", "Camisetas", "true", "active"),
            (Cell(file, 1, "Title"), Cell(file, 1, "Vendor"), Cell(file, 1, "Type"), Cell(file, 1, "Published on online store"), Cell(file, 1, "Status")));
        Assert.Equal(("", "", ""), (Cell(file, 2, "Title"), Cell(file, 2, "Vendor"), Cell(file, 2, "Status")));
        Assert.Equal((["SHIRT-NAVY-M", "SHIRT-BLACK-M", ""]), file.Skip(1).Select(line => line[8]));
        Assert.Equal(("color", "azul marino", "talla", "M", "24.90", "3"),
            (Cell(file, 1, "Option1 name"), Cell(file, 1, "Option1 value"), Cell(file, 1, "Option2 name"), Cell(file, 1, "Option2 value"), Cell(file, 1, "Price"), Cell(file, 1, "Inventory quantity")));
        Assert.Equal(("", "negro", "26.90"), (Cell(file, 2, "Option1 name"), Cell(file, 2, "Option1 value"), Cell(file, 2, "Price")));
        Assert.Equal(["1", "2", "3"], file.Skip(1).Select(line => line[24]));
        Assert.Equal(("De frente", "https://api.example/api/images/side", ""), (Cell(file, 1, "Image alt text"), Cell(file, 3, "Product image URL"), Cell(file, 3, "Price")));
        Assert.Equal(("https://api.example/api/images/navy", ""), (Cell(file, 1, "Variant image URL"), Cell(file, 2, "Variant image URL")));
        Assert.Equal(("8412345678905", "212"), (Cell(file, 1, "Barcodes"), Cell(file, 1, "Google Shopping / Google Product Category")));
    }

    /// <summary>Each paragraph its own, and the text escaped: Shopify's description is HTML.</summary>
    [Fact]
    public async Task A_description_is_written_as_the_html_shopify_keeps()
    {
        var file = await Written(Shirt);

        Assert.Equal("<p>Transpirable &amp; ligera.</p><p>Para correr &lt;de verdad&gt;.</p>", Cell(file, 1, "Description"));
    }

    /// <summary>Only what HTML needs escaped is: a Spanish description keeps its letters, which a merchant edits afterwards in Shopify.</summary>
    [Fact]
    public async Task A_description_keeps_its_accents()
    {
        var file = await Written(Moka with { Description = "Para la montaña: café \"de verdad\"." });

        Assert.Equal("<p>Para la montaña: café &quot;de verdad&quot;.</p>", Cell(file, 1, "Description"));
    }

    [Fact]
    public async Task A_draft_without_options_is_written_as_shopify_writes_one()
    {
        var file = await Written(Moka);

        Assert.Equal(("draft", "false", "Title", "Default Title", "", "5"),
            (Cell(file, 1, "Status"), Cell(file, 1, "Published on online store"), Cell(file, 1, "Option1 name"), Cell(file, 1, "Option1 value"), Cell(file, 1, "Description"), Cell(file, 1, "Inventory quantity")));
        Assert.Equal(("true", "shopify", "deny", "true", "manual", "false"),
            (Cell(file, 1, "Charge tax"), Cell(file, 1, "Inventory tracker"), Cell(file, 1, "Continue selling when out of stock"), Cell(file, 1, "Requires shipping"), Cell(file, 1, "Fulfillment service"), Cell(file, 1, "Gift card")));
    }

    [Fact]
    public async Task Shopify_reads_commas_whatever_the_readers_language()
    {
        using var output = new MemoryStream();
        await new ShopifyProductCsv().WriteAsync([Moka], new ExportWriting("es"), output, Ct);

        Assert.StartsWith("Title,URL handle,", Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
        Assert.Equal(("shopify-csv", "text/csv; charset=utf-8", "csv"), (new ShopifyProductCsv().Format, new ShopifyProductCsv().ContentType, new ShopifyProductCsv().Extension));
    }
}
