using System.Text;
using ElGuerre.Tendero.Exports.Features.ProductFeed;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Exports.Tests;

/// <summary>The Merchant Center format passes the contract every format does.</summary>
public sealed class GoogleMerchantFeedContractTests : ExportFormatContractTests<FeedItem>
{
    protected override IExportFormat<FeedItem> CreateFormat() => new GoogleMerchantFeed();

    protected override IReadOnlyList<FeedItem> Rows() => [GoogleMerchantFeedTests.Navy, GoogleMerchantFeedTests.Moka];

    protected override string KeyColumn(string culture) => "id";

    protected override string KeyOf(FeedItem row) => row.Id;

    protected override string MoneyColumn(string culture) => "price";

    protected override Money MoneyOf(FeedItem row) => row.Price;

    protected override string TextColumn(string culture) => "description";

    protected override FeedItem WithText(FeedItem row, string text) => row with { Description = text };

    protected override char SeparatorFor(string culture) => '\t';

    protected override char DecimalMarkFor(string culture) => '.';
}

/// <summary>
/// A product feed as Merchant Center reads one (E-2), checked against Google's
/// product data specification on 2026-10-25: tab-separated UTF-8 with Google's
/// attribute names, the price a number with a point and its currency, the
/// availability and gender in Google's English values, extra pictures in one cell
/// separated by commas, and identifiers said missing when there is no GTIN.
/// </summary>
public sealed class GoogleMerchantFeedTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static readonly FeedItem Navy = new(
        "SHIRT-NAVY-M", "B07TUVW404", "Camiseta técnica - azul marino / M", "Transpirable.",
        "https://shop.example/es/p/camiseta-tecnica/B07TUVW404", "https://api.example/api/images/front",
        ["https://api.example/api/images/back", "https://api.example/api/images/side"],
        FeedAvailability.InStock, new Money(24.90m, "EUR"), "Trail Co", "8412345678905",
        212, "Ropa > Camisetas", "azul marino", "M", FeedGender.Female);

    internal static readonly FeedItem Moka = new(
        "MOKA-DEFAULT", "B11COF0401", "Cafetera italiana", "De aluminio.",
        "https://shop.example/es/p/cafetera-italiana/B11COF0401", null, [],
        FeedAvailability.OutOfStock, new Money(29.90m, "EUR"), null, null,
        null, "Hogar > Cocina > Cafeteras", null, null, null);

    private static async Task<string> Written(params FeedItem[] items)
    {
        using var output = new MemoryStream();
        await new GoogleMerchantFeed().WriteAsync(items, new ExportWriting("es"), output, Ct);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    [Fact]
    public async Task The_header_is_googles_attribute_names()
    {
        var file = await Written();

        Assert.Equal(
            "id\titem_group_id\ttitle\tdescription\tlink\timage_link\tadditional_image_link\tavailability\tprice\tbrand\tgtin\tidentifier_exists\tcondition\tgoogle_product_category\tproduct_type\tcolor\tsize\tgender\r\n",
            file);
    }

    [Fact]
    public async Task A_variant_is_written_in_googles_words()
    {
        var line = (await Written(Navy)).Split("\r\n")[1].Split('\t');

        Assert.Equal(
            [
                "SHIRT-NAVY-M", "B07TUVW404", "Camiseta técnica - azul marino / M", "Transpirable.",
                "https://shop.example/es/p/camiseta-tecnica/B07TUVW404", "https://api.example/api/images/front",
                "https://api.example/api/images/back,https://api.example/api/images/side",
                "in_stock", "24.90 EUR", "Trail Co", "8412345678905", "", "new", "212", "Ropa > Camisetas", "azul marino", "M", "female",
            ],
            line);
    }

    /// <summary>No GTIN says so (identifier_exists no); what the shop does not know stays empty, never a placeholder.</summary>
    [Fact]
    public async Task What_a_product_lacks_stays_empty_and_a_missing_gtin_is_said()
    {
        var line = (await Written(Moka)).Split("\r\n")[1].Split('\t');

        Assert.Equal(
            [
                "MOKA-DEFAULT", "B11COF0401", "Cafetera italiana", "De aluminio.", "https://shop.example/es/p/cafetera-italiana/B11COF0401",
                "", "", "out_of_stock", "29.90 EUR", "", "", "no", "new", "", "Hogar > Cocina > Cafeteras", "", "", "",
            ],
            line);
    }

    /// <summary>Google's limits: an id of 50, a title of 150, a description of 5000 characters.</summary>
    [Fact]
    public async Task What_is_longer_than_google_reads_is_cut_to_its_limit()
    {
        var long_ = Navy with { Title = new string('t', 200), Description = new string('d', 6000) };

        var line = (await Written(long_)).Split("\r\n")[1].Split('\t');

        Assert.Equal((150, 5000), (line[2].Length, line[3].Length));
    }

    [Fact]
    public void The_format_is_a_tab_separated_file()
    {
        var format = new GoogleMerchantFeed();

        Assert.Equal(("google-merchant", "text/tab-separated-values; charset=utf-8", "tsv"), (format.Format, format.ContentType, format.Extension));
    }
}
