namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// What the shop is, as configuration (section <c>Shop</c>). Down here because more
/// than one context will need it — "a day" is the same day for sales, stock and
/// campaigns.
/// </summary>
public sealed class ShopOptions
{
    public const string SectionName = "Shop";

    /// <summary>
    /// The currency the shop sells in when nothing it is looking at says: a
    /// period with no orders, a search with no results. Every amount carries
    /// its own currency (<see cref="Money"/>); this is only what an empty answer
    /// is written in. One shop, one currency — laboratory scale, and said once
    /// here where it was three literals in three contexts.
    /// </summary>
    public const string DefaultCurrency = "EUR";

    /// <summary>
    /// The shop's time zone, as an IANA id. A day on the dashboard is the shop's
    /// calendar day, whoever reads it and from wherever — one Monday for everyone,
    /// as Shopify and Seller Central report (ADR 0046). Validated when the API
    /// starts: a zone nobody has is a sentence at startup, not a 500 later.
    /// </summary>
    public string TimeZone { get; set; } = "Europe/Madrid";
}
