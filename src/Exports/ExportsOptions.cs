namespace ElGuerre.Tendero.Exports;

/// <summary>What the exports need to know about the shop (section <c>Exports</c>).</summary>
public sealed class ExportsOptions
{
    public const string SectionName = "Exports";

    /// <summary>The storefront's address, which a feed links each product's page to. The AppHost gives the storefront's.</summary>
    public string StorefrontUrl { get; set; } = "http://localhost:4200";

    /// <summary>
    /// The shop's categories as Google's taxonomy ids (<c>seed/google-categories.sample.json</c>),
    /// a mapping a person writes, as the Shopify one is. A category with none takes its
    /// nearest ancestor's; a missing file leaves every row without one, and counts them.
    /// </summary>
    public string GoogleCategoriesFile { get; set; } = "seed/google-categories.sample.json";
}
