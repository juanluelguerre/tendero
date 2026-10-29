using System.Globalization;

namespace ElGuerre.Tendero.Exports.Features.CatalogueForAnotherPlatform;

/// <summary>
/// The catalogue as Shopify's product importer reads it (E-3), checked against
/// Shopify's help on 2026-10-25 (help.shopify.com/en/manual/products/import-export/using-csv):
///
/// - the current template's headers — <c>URL handle</c>, <c>Barcodes</c> — and only
///   the columns Tendero has something for, since Shopify reads a file with fewer;
/// - a product's first line carries its own fields with its first variant and its
///   first picture; each next line repeats only the handle, with the next variant,
///   the next picture, or both, as Shopify's own export lays them out;
/// - a product sold one way is written as Shopify writes one: an option called
///   <c>Title</c> with the value <c>Default Title</c>;
/// - the description is HTML, each paragraph its own, the text escaped;
/// - commas and a decimal point whatever the reader's language: the reader is
///   Shopify, not a spreadsheet; no BOM.
///
/// Inventory goes as one number, which Shopify applies only to a store with one
/// location; a store with more takes its stock by its own import (P16-15's way).
/// </summary>
internal sealed class ShopifyProductCsv : IExportFormat<ProductForAnotherPlatform>
{
    public const string Key = "shopify-csv";

    /// <summary>Shopify keeps three options at most; a fourth has nowhere to go.</summary>
    private const int Options = 3;

    private static string[] Header =>
    [
        "Title", "URL handle", "Description", "Vendor", "Type", "Tags", "Published on online store", "Status", "SKU", "Barcodes",
        "Option1 name", "Option1 value", "Option2 name", "Option2 value", "Option3 name", "Option3 value", "Price", "Charge tax",
        "Inventory tracker", "Inventory quantity", "Continue selling when out of stock", "Requires shipping", "Fulfillment service",
        "Product image URL", "Image position", "Image alt text", "Variant image URL", "Gift card", "Google Shopping / Google Product Category",
    ];

    private static CsvDialect Dialect => new(',', Quoted: true, ByteOrderMark: false);

    public string Format => Key;

    public string ContentType => "text/csv; charset=utf-8";

    public string Extension => "csv";

    public Task WriteAsync(IReadOnlyList<ProductForAnotherPlatform> rows, ExportWriting writing, Stream output, CancellationToken cancellationToken = default) =>
        Csv.WriteAsync(output, Header, rows.SelectMany(Lines), Dialect, cancellationToken);

    private static IEnumerable<IReadOnlyList<string>> Lines(ProductForAnotherPlatform product)
    {
        var lines = Math.Max(product.Variants.Count, product.Pictures.Count);
        for (var line = 0; line < lines; line++)
        {
            var first = line == 0;
            var variant = line < product.Variants.Count ? product.Variants[line] : null;
            var picture = line < product.Pictures.Count ? product.Pictures[line] : null;

            yield return
            [
                first ? product.Title : "",
                product.Handle,
                first ? Html(product.Description) : "",
                first ? product.Vendor ?? "" : "",
                first ? product.Type ?? "" : "",
                "",
                first ? (product.OnSale ? "true" : "false") : "",
                first ? (product.OnSale ? "active" : "draft") : "",
                variant?.Sku ?? "",
                variant?.Barcode ?? "",
                .. Option(product, variant, 0, first),
                .. Option(product, variant, 1, first),
                .. Option(product, variant, 2, first),
                variant is null ? "" : ExportNumbers.Amount(variant.Price),
                variant is null ? "" : "true",
                variant is null ? "" : "shopify",
                variant?.Inventory.ToString(CultureInfo.InvariantCulture) ?? "",
                variant is null ? "" : "deny",
                variant is null ? "" : "true",
                variant is null ? "" : "manual",
                picture?.Url ?? "",
                picture is null ? "" : (line + 1).ToString(CultureInfo.InvariantCulture),
                picture?.Alt ?? "",
                variant?.PictureUrl ?? "",
                first ? "false" : "",
                first ? product.GoogleProductCategory?.ToString(CultureInfo.InvariantCulture) ?? "" : "",
            ];
        }
    }

    /// <summary>An option's name on the first line only, its value on every variant's; a product sold one way is Shopify's "Default Title".</summary>
    private static string[] Option(ProductForAnotherPlatform product, VariantForAnotherPlatform? variant, int index, bool first)
    {
        if (product.OptionNames.Count == 0)
            return index == 0 ? [first ? "Title" : "", variant is null ? "" : "Default Title"] : ["", ""];

        if (index >= Math.Min(product.OptionNames.Count, Options))
            return ["", ""];

        return [first ? product.OptionNames[index] : "", variant is null || index >= variant.OptionValues.Count ? "" : variant.OptionValues[index]];
    }

    private static string Html(string? text) =>
        text is null
            ? ""
            : string.Concat(text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries)
                .Select(paragraph => $"<p>{Escaped(paragraph.Trim())}</p>"));

    /// <summary>
    /// What HTML needs escaped, and nothing else: <c>WebUtility.HtmlEncode</c> also turns
    /// every accented letter into a number (<c>&amp;#241;</c> for ñ), which a merchant
    /// would then meet in Shopify's editor.
    /// </summary>
    private static string Escaped(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
