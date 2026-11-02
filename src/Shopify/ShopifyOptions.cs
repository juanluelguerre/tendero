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

    /// <summary>
    /// A merchant's own store (S2a, P16-14): the offline token
    /// <c>tools/shopify-connect</c> obtains by installing the app once, with the
    /// authorization-code grant. Used as it is, instead of the client credentials,
    /// which only reach stores in the app's own organisation. A custom app's
    /// offline token does not expire — Shopify requires expiring ones of public
    /// apps only (changelog, 2026-04-01 and 2027-01-01) — so it is a secret like
    /// the others and nothing is kept in the database (spec decision 2).
    /// </summary>
    public string? AccessToken { get; set; }

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
    /// A store's location to the code of the warehouse it is here (S2a, P16-10),
    /// written by a person like <see cref="Categories"/>. A location not in it is
    /// proposed as a warehouse, and its stock is not read until it is mapped.
    /// </summary>
    public Dictionary<string, string> Locations { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The JSON file <see cref="Locations"/> is read from, for the same reason as the categories'.</summary>
    public string? LocationsFile { get; set; }

    /// <summary>
    /// The metafield namespace a product's attributes are read from (spec decision
    /// 6, amended 2026-10-23): a merchant keeps them in metafields, and the round
    /// trip writes the seed's there. Each key is an attribute, in the store's words.
    /// </summary>
    public string AttributesNamespace { get; set; } = DefaultAttributesNamespace;

    /// <summary>
    /// Where the store can fetch Tendero's pictures from when it publishes a product
    /// (phase 16, S3): Shopify downloads a file from its URL, and a laptop's image
    /// store is not on the internet. Unset, a published product keeps the pictures
    /// the store already has.
    /// </summary>
    public Uri? PublicImagesBaseUrl { get; set; }

    /// <summary>
    /// Whether the store is also a sales channel Tendero publishes to and receives orders
    /// from (phase 16, S3; ADR 0070). Off unless said: a store connected to import from —
    /// a merchant leaving Shopify — must not find itself rewritten by Tendero.
    /// </summary>
    public bool SalesChannel { get; set; }

    /// <summary>
    /// The secret the store signs its webhooks with (phase 16, S3): the app's client
    /// secret, unless the store was connected with an offline token and the app's
    /// secret is kept here apart.
    /// </summary>
    public string? WebhookSecret { get; set; }

    public const string DefaultAttributesNamespace = "tendero";

    /// <summary>A store, and a way in: its own token, or the app's credentials.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Store) &&
        (!string.IsNullOrWhiteSpace(AccessToken) ||
         (!string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret)));
}
