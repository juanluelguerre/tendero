namespace ElGuerre.Tendero.Shopify;

/// <summary>
/// Every query the adapter sends, in one place, so the day Shopify's schema
/// moves there is one file to read against it. Checked against the
/// <see cref="ShopifyOptions.PinnedApiVersion"/> reference; the round trip
/// against a real development store (P16-6) is what proves them.
/// </summary>
internal static class ShopifyQueries
{
    /// <summary>
    /// The whole catalogue as one bulk operation. Four connections (products,
    /// their media, their metafields under one namespace, their variants) within
    /// the five and the two levels a bulk query allows. <c>groupObjects: true</c>
    /// because the file is read as a stream, each product's children right after
    /// it; since 2026-01 that is no longer the default. Each locale Tendero speaks besides the store's own is
    /// asked for by an alias named for its language, <c>t_es</c> for
    /// <c>es-ES</c> (an alias cannot carry the hyphen), on its product's line.
    /// </summary>
    public static string Products(IEnumerable<string> translatedLocales, string attributesNamespace = ShopifyOptions.DefaultAttributesNamespace)
    {
        var translations = string.Concat(translatedLocales.Select(locale =>
            $" t_{ShopifyLanguage.Of(locale)}: translations(locale: \\\"{locale}\\\") {{ key value }}"));

        return
            $$"""
            mutation {
              bulkOperationRunQuery(
                groupObjects: true
                query: "{ products { edges { node { id handle title descriptionHtml vendor publishedAt category { id fullName }{{translations}} media { edges { node { ... on MediaImage { id image { url altText } } } } } metafields(namespace: \"{{attributesNamespace}}\") { edges { node { id key value } } } variants { edges { node { id sku barcode price taxable selectedOptions { name value } image { url } } } } } } } }"
              ) {
                bulkOperation { id status }
                userErrors { field message }
              }
            }
            """;
    }

    public static string BulkOperation(string id) =>
        $$"""{ node(id: "{{id}}") { ... on BulkOperation { status errorCode objectCount url } } }""";

    public const string Shop = "{ shop { currencyCode } shopLocales(published: true) { locale primary } }";
}
