namespace ElGuerre.Tendero.Shopify;

/// <summary>
/// One Shopify store to import from (phase 16). The app is created in Shopify's
/// Dev Dashboard — since 2026-01-01 a store's admin no longer creates custom
/// apps — and its client id and secret are exchanged for a 24-hour token by the
/// client-credentials grant, which works for stores in the app's own
/// organisation (docs/specs/shopify-import.md, *Connecting to a store*).
///
/// All three come from secrets, never the repository. Without them the source
/// is not registered, and asking for it is the import's ordinary 400.
/// </summary>
public sealed class ShopifyOptions
{
    public const string Section = "Shopify";

    /// <summary>
    /// The Admin API version every call names. Shopify releases one a quarter and
    /// supports each for a year; it is pinned, and moved on purpose.
    /// </summary>
    public const string PinnedApiVersion = "2026-10";

    /// <summary>The store's own domain: <c>tendero-dev.myshopify.com</c>.</summary>
    public string? Store { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string ApiVersion { get; set; } = PinnedApiVersion;

    /// <summary>How long to wait between two looks at a running bulk operation.</summary>
    public TimeSpan BulkPollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How long to wait before asking a throttled store again.</summary>
    public TimeSpan ThrottleBackoff { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Shopify's taxonomy category to our category code, written by a person, one
    /// file per store (spec decision 5). A category not in it leaves the product
    /// without one, and the import says so.
    /// </summary>
    public Dictionary<string, string> Categories { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The JSON file <see cref="Categories"/> is read from. A file, and not a
    /// configuration section, because a taxonomy id (<c>gid://shopify/…</c>)
    /// carries the colon configuration uses to separate its sections.
    /// </summary>
    public string? CategoriesFile { get; set; }

    /// <summary>
    /// The metafield namespace a product's attributes are read from (spec decision
    /// 6, amended 2026-10-23): a merchant keeps them in metafields, and the round
    /// trip writes the seed's there. Each key is an attribute, in the store's words.
    /// </summary>
    public string AttributesNamespace { get; set; } = DefaultAttributesNamespace;

    public const string DefaultAttributesNamespace = "tendero";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Store) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);
}
