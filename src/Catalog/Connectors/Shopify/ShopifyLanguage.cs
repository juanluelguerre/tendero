namespace ElGuerre.Tendero.Catalog.Connectors.Shopify;

/// <summary>
/// A store's locale as the catalogue's language. Shopify writes a locale with its
/// region when the merchant chose one (<c>en-US</c>, <c>pt-BR</c>); the catalogue's
/// texts are keyed by the language alone (<c>en</c>), as the contract requires.
/// </summary>
internal static class ShopifyLanguage
{
    /// <summary>The languages the catalogue speaks (invariant 6).</summary>
    public static readonly IReadOnlySet<string> Spoken = new HashSet<string>(["es", "en"], StringComparer.OrdinalIgnoreCase);

    public static string Of(string locale) =>
        (locale.Split('-', '_')[0]).ToLowerInvariant();
}
