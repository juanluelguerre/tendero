namespace ElGuerre.Tendero.Ucp;

/// <summary>
/// Which version of the Universal Commerce Protocol this manifest speaks, and
/// where its definition lives.
///
/// **Pinned as a constant, deliberately.** UCP versions are dates, and a shop
/// that advertised "whatever the latest is" would be promising a document it has
/// never been read against. Moving this string is a deliberate act with a diff,
/// which is the same argument the repository makes about the embedding model's
/// identity travelling in the collection name.
///
/// **No conformance is claimed.** What is asserted here is that the profile has
/// the shape the specification describes for the capabilities Tendero actually
/// serves — read from the specification on 2026-09-08 — and nothing about
/// passing anybody's certification, which does not exist to pass yet.
/// </summary>
public static class UcpSpecification
{
    /// <summary>The specification version, which is a date in YYYY-MM-DD.</summary>
    public const string Version = "2026-04-08";

    /// <summary>Where a human reads what this document means.</summary>
    public const string BaseUrl = $"https://ucp.dev/{Version}/specification";

    /// <summary>
    /// Where the protocol's own JSON Schemas are published: under the version
    /// (<c>https://ucp.dev/2026-04-08/schemas/shopping/catalog_search.json</c>).
    /// It was the unversioned address the schema documents carry as their
    /// <c>$id</c>, and that address answers 404: a platform that fetched what the
    /// profile advertises got nothing (found 2026-10-13, an audit against the live
    /// specification).
    /// </summary>
    public const string SchemaBaseUrl = $"https://ucp.dev/{Version}/schemas";

    /// <summary>
    /// The path the specification fixes. It is not configurable and must not
    /// require authentication: discovery that needs a credential is not
    /// discovery.
    /// </summary>
    public const string WellKnownPath = "/.well-known/ucp";

    /// <summary>
    /// The service namespace a shop publishes under. Reverse-DNS, and the
    /// vendor form for anything of our own is <c>com.elguerre.tendero.*</c>.
    /// </summary>
    public const string ShoppingService = "dev.ucp.shopping";
}
