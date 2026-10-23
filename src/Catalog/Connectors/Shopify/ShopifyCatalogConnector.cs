using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Catalog.Connectors.Shopify;

/// <summary>
/// The inbound port's Shopify adapter (phase 16, docs/specs/shopify-import.md).
/// Article 02 promised that a second source is one class and one registration
/// line; this is the class. It asks the store what it is (currency, languages),
/// reads the whole catalogue in one bulk operation, and maps each product to the
/// contract:
///
/// - the store's own language is the base, and the translations it publishes are
///   read in the languages Tendero speaks, the rest never asked for;
/// - descriptions are HTML and become plain text, paragraphs kept;
/// - the taxonomy category becomes ours through a mapping a person wrote;
/// - the product's price is its lowest variant's, the "from" price Shopify shows,
///   and each variant carries its own price, id, SKU and barcode;
/// - a picture a variant names shows that variant's options.
///
/// It is registered only when the store's credentials are configured.
/// </summary>
internal sealed class ShopifyCatalogConnector(ShopifyAdmin admin, IOptions<ShopifyOptions> options) : ICatalogSourceConnector
{
    public const string Key = "shopify";

    /// <summary>What Shopify calls the one variant of a product sold one way.</summary>
    private const string DefaultOption = "Default Title";

    private readonly List<SourceNote> sourceNotes = [];

    public string Source => Key;

    /// <summary>The store's languages the shop does not speak, once the stream has been read.</summary>
    public IReadOnlyList<SourceNote> SourceNotes => this.sourceNotes;

    public async IAsyncEnumerable<ExternalProduct> StreamProductsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        this.sourceNotes.Clear();
        var shop = await ShopifyShop.ReadAsync(admin, cancellationToken);
        var language = ShopifyLanguage.Of(shop.PrimaryLocale);
        var translated = shop.Locales
            .Where(locale => ShopifyLanguage.Spoken.Contains(ShopifyLanguage.Of(locale)) && ShopifyLanguage.Of(locale) != language)
            .ToList();

        this.sourceNotes.AddRange(shop.Locales
            .Where(locale => !ShopifyLanguage.Spoken.Contains(ShopifyLanguage.Of(locale)))
            .Select(locale => new SourceNote(ImportNoteCode.LocaleIgnored, null, locale)));

        var file = await admin.RunBulkAsync(ShopifyQueries.Products(translated, options.Value.AttributesNamespace), cancellationToken);
        if (file is null)
            yield break;

        var languages = translated.Select(ShopifyLanguage.Of).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await foreach (var product in ShopifyBulkProducts.GatherAsync(admin.ReadLinesAsync(file, cancellationToken), cancellationToken))
            yield return Map(product, shop.Currency, language, languages);
    }

    private ExternalProduct Map(ShopifyProduct product, string currency, string language, IReadOnlySet<string> translated)
    {
        var names = new Dictionary<string, string> { [language] = product.Title };
        var descriptions = new Dictionary<string, string>();
        if (ShopifyHtml.ToText(product.DescriptionHtml) is { Length: > 0 } description)
            descriptions[language] = description;

        foreach (var (key, texts) in product.Translations.Where(each => translated.Contains(each.Key)))
        {
            if (texts.Title is { Length: > 0 } title)
                names[key] = title;
            if (ShopifyHtml.ToText(texts.DescriptionHtml) is { Length: > 0 } text)
                descriptions[key] = text;
        }

        var variants = product.Variants.Select(Variant).ToList();
        var category = product.Category is { } known ? options.Value.Categories.GetValueOrDefault(known.Id) : null;

        return new ExternalProduct(
            ExternalId: product.Id,
            Names: names,
            Descriptions: descriptions.Count > 0 ? descriptions : null,
            Brand: product.Vendor,
            Category: category,
            PriceAmount: product.Variants.Count > 0 ? product.Variants.Min(variant => variant.Price) : 0m,
            PriceCurrency: currency,
            Images: [.. product.Images.Select(image => Image(image, product, language))],
            Attributes: new Dictionary<string, string>(product.Metafields, StringComparer.OrdinalIgnoreCase),
            AvailableFrom: product.PublishedAt is { } published ? DateOnly.FromDateTime(published.UtcDateTime) : null,
            Variants: variants,
            Handle: product.Handle,
            Notes: NotesOn(product, category));
    }

    /// <summary>
    /// What did not come across cleanly about one product (P16-5): a category the
    /// mapping does not know, a description whose table or list became lines, a
    /// variant with no SKU, SKUs its variants share. Null when there is nothing.
    /// </summary>
    private static List<SourceNote>? NotesOn(ShopifyProduct product, string? category)
    {
        var notes = new List<SourceNote>();

        if (product.Category is { } unmapped && category is null)
            notes.Add(new SourceNote(ImportNoteCode.CategoryNotMapped, product.Id, unmapped.FullName));

        if (ShopifyHtml.HasLayout(product.DescriptionHtml)
            || product.Translations.Values.Any(texts => ShopifyHtml.HasLayout(texts.DescriptionHtml)))
            notes.Add(new SourceNote(ImportNoteCode.DescriptionHadLayout, product.Id, null));

        notes.AddRange(product.Variants
            .Where(variant => variant.Sku is null)
            .Select(variant => new SourceNote(ImportNoteCode.VariantWithoutSku, product.Id, variant.Id)));

        notes.AddRange(product.Variants
            .Where(variant => variant.Sku is not null)
            .GroupBy(variant => variant.Sku!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new SourceNote(ImportNoteCode.SkuSharedAtSource, product.Id, group.Key)));

        return notes.Count > 0 ? notes : null;
    }

    private static ExternalVariant Variant(ShopifyVariant variant) => new(
        Key: variant.Id,
        Attributes: OptionsOf(variant),
        ExternalId: variant.Id,
        Sku: variant.Sku,
        Barcode: variant.Barcode,
        PriceAmount: variant.Price);

    /// <summary>What a variant varies by, in the store's words; nothing for "Default Title".</summary>
    private static Dictionary<string, string> OptionsOf(ShopifyVariant variant) =>
        variant.Options
            .Where(option => option.Value != DefaultOption)
            .ToDictionary(option => option.Name, option => option.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A picture, and what it shows: the options every variant that names it has
    /// in common (a colour's picture is every size of the colour). A picture no
    /// variant names shows the product.
    /// </summary>
    private static ExternalImage Image(ShopifyImage image, ShopifyProduct product, string language)
    {
        var naming = product.Variants
            .Where(variant => string.Equals(variant.ImageUrl, image.Url, StringComparison.Ordinal))
            .Select(OptionsOf)
            .ToList();

        var shows = naming.Count == 0
            ? null
            : naming[0]
                .Where(option => naming.All(other => other.TryGetValue(option.Key, out var value) && value == option.Value))
                .ToDictionary(option => option.Key, option => option.Value);

        return new ExternalImage(
            new Uri(image.Url),
            image.AltText is { Length: > 0 } alt ? new Dictionary<string, string> { [language] = alt } : null,
            shows is { Count: > 0 } ? shows : null);
    }
}
