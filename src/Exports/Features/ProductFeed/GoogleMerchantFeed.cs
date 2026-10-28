using System.Globalization;

namespace ElGuerre.Tendero.Exports.Features.ProductFeed;

/// <summary>
/// A product feed as Merchant Center reads one (E-2), checked against Google's
/// product data specification on 2026-10-25 (support.google.com/merchants/answer/7052112):
/// tab-separated UTF-8 under Google's attribute names; <c>price</c> a number with a
/// point and its ISO 4217 code (VAT included, as Spain's prices are); availability,
/// condition and gender in Google's English values; the extra pictures in one cell,
/// separated by commas; <c>identifier_exists</c> said <c>no</c> only when there is no
/// GTIN, and left to its default otherwise; Google's length limits kept (an id of 50,
/// a title of 150, a description of 5,000). What the shop does not know stays empty:
/// Google refuses a placeholder ("N/A") where it accepts nothing.
/// </summary>
internal sealed class GoogleMerchantFeed : IExportFormat<FeedItem>
{
    public const string Key = "google-merchant";

    private static string[] Header =>
    [
        "id", "item_group_id", "title", "description", "link", "image_link", "additional_image_link", "availability", "price",
        "brand", "gtin", "identifier_exists", "condition", "google_product_category", "product_type", "color", "size", "gender",
    ];

    public string Format => Key;

    public string ContentType => "text/tab-separated-values; charset=utf-8";

    public string Extension => "tsv";

    public Task WriteAsync(IReadOnlyList<FeedItem> rows, ExportWriting writing, Stream output, CancellationToken cancellationToken = default) =>
        Csv.WriteAsync(output, Header, rows.Select(Line), CsvDialect.Feed, cancellationToken);

    private static IReadOnlyList<string> Line(FeedItem item) =>
    [
        Cut(item.Id, 50),
        Cut(item.ItemGroupId, 50),
        Cut(item.Title, 150),
        Cut(item.Description, 5000),
        item.Link,
        item.ImageLink ?? "",
        string.Join(',', item.AdditionalImageLinks.Take(10)),
        item.Availability switch
        {
            FeedAvailability.InStock => "in_stock",
            FeedAvailability.OutOfStock => "out_of_stock",
        },
        $"{ExportNumbers.Amount(item.Price)} {item.Price.Currency}",
        item.Brand ?? "",
        item.Gtin ?? "",
        item.Gtin is null ? "no" : "",
        "new",
        item.GoogleProductCategory?.ToString(CultureInfo.InvariantCulture) ?? "",
        item.ProductType,
        item.Color ?? "",
        item.Size ?? "",
        item.Gender switch
        {
            FeedGender.Female => "female",
            FeedGender.Male => "male",
            FeedGender.Unisex => "unisex",
            null => "",
        },
    ];

    private static string Cut(string text, int limit) => text.Length <= limit ? text : text[..limit];
}
