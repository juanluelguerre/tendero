using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ElGuerre.Tendero.Shopify;

/// <summary>
/// A bulk operation's file, read as products. Asked for grouped, each product's
/// variants and pictures follow it on lines of their own carrying
/// <c>__parentId</c>, so a product is complete when the next one starts and the
/// file is never held whole — the port's promise for a large catalogue.
///
/// A child whose parent is not the product being read means the file was not
/// grouped, and is refused: guessing would put a variant on the wrong product.
/// </summary>
internal static class ShopifyBulkProducts
{
    private const string TranslationPrefix = "t_";

    public static async IAsyncEnumerable<ShopifyProduct> GatherAsync(
        IAsyncEnumerable<JsonElement> lines, [EnumeratorCancellation] CancellationToken ct)
    {
        Reading? reading = null;

        await foreach (var line in lines.WithCancellation(ct))
        {
            if (!line.TryGetProperty("__parentId", out var parent))
            {
                if (reading is not null)
                    yield return reading.Complete();

                reading = new Reading(line);
                continue;
            }

            if (reading is null || IdOf(parent.GetString()) != reading.Id)
            {
                throw new ShopifyException(
                    ShopifyFailure.UngroupedBulkFile,
                    $"A line of {parent.GetString()} came while reading {reading?.Id ?? "nothing"}: the bulk file is not grouped.");
            }

            reading.Add(line);
        }

        if (reading is not null)
            yield return reading.Complete();
    }

    /// <summary><c>gid://shopify/Product/8001</c> → <c>8001</c>.</summary>
    internal static string IdOf(string? gid) =>
        gid is null ? "" : gid[(gid.LastIndexOf('/') + 1)..];

    private sealed class Reading(JsonElement product)
    {
        private readonly List<ShopifyVariant> variants = [];
        private readonly List<ShopifyImage> images = [];
        private readonly Dictionary<string, string> metafields = new(StringComparer.OrdinalIgnoreCase);

        public string Id { get; } = IdOf(product.GetProperty("id").GetString());

        public void Add(JsonElement child)
        {
            var gid = child.GetProperty("id").GetString() ?? "";

            if (gid.Contains("/ProductVariant/", StringComparison.Ordinal))
                this.variants.Add(Variant(child));
            else if (gid.Contains("/Metafield/", StringComparison.Ordinal)
                     && Text(child, "key") is { } key && Text(child, "value") is { } value)
                this.metafields[key] = value;
            else if (gid.Contains("/MediaImage/", StringComparison.Ordinal)
                     && child.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.Object
                     && Text(image, "url") is { } url)
                this.images.Add(new ShopifyImage(url, Text(image, "altText")));
        }

        public ShopifyProduct Complete() => new(
            Id,
            Text(product, "handle") ?? "",
            Text(product, "title") ?? "",
            Text(product, "descriptionHtml"),
            Text(product, "vendor"),
            Text(product, "publishedAt") is { } published
                ? DateTimeOffset.Parse(published, CultureInfo.InvariantCulture)
                : null,
            product.TryGetProperty("category", out var category) && category.ValueKind == JsonValueKind.Object
                ? new ShopifyCategory(Text(category, "id") ?? "", Text(category, "fullName") ?? "")
                : null,
            Translations(product),
            this.variants,
            this.images,
            this.metafields);

        private static ShopifyVariant Variant(JsonElement line) => new(
            IdOf(line.GetProperty("id").GetString()),
            Text(line, "sku"),
            Text(line, "barcode"),
            decimal.Parse(Text(line, "price") ?? "0", CultureInfo.InvariantCulture),
            [.. line.GetProperty("selectedOptions").EnumerateArray()
                .Select(option => new ShopifyOption(Text(option, "name") ?? "", Text(option, "value") ?? ""))],
            line.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.Object ? Text(image, "url") : null,
            // Absent means taxed: Shopify's own default for a variant.
            !line.TryGetProperty("taxable", out var taxable) || taxable.ValueKind != JsonValueKind.False);

        /// <summary>The <c>t_en</c> aliases: each locale's translated title and description.</summary>
        private static Dictionary<string, ShopifyTexts> Translations(JsonElement product)
        {
            var texts = new Dictionary<string, ShopifyTexts>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in product.EnumerateObject())
            {
                if (!property.Name.StartsWith(TranslationPrefix, StringComparison.Ordinal)
                    || property.Value.ValueKind != JsonValueKind.Array
                    || property.Value.GetArrayLength() == 0)
                    continue;

                var byKey = property.Value.EnumerateArray()
                    .ToDictionary(entry => Text(entry, "key") ?? "", entry => Text(entry, "value"));

                texts[property.Name[TranslationPrefix.Length..]] = new ShopifyTexts(
                    byKey.GetValueOrDefault("title"), byKey.GetValueOrDefault("body_html"));
            }

            return texts;
        }

        /// <summary>A string property, with null, absent and empty all meaning nothing.</summary>
        private static string? Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                && value.GetString() is { Length: > 0 } text
                ? text
                : null;
    }
}
