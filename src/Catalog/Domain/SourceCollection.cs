namespace ElGuerre.Tendero.Catalog.Domain;

/// <summary>
/// A source's collection as the category it is here (phase 16, P16-11): what a
/// store's <c>/collections/menaje</c> leads to once the shop has left it. Kept
/// by the import, read by the redirects, keyed by the source and its handle.
/// </summary>
public sealed class SourceCollection
{
    private SourceCollection() { } // EF Core

    public SourceCollection(string source, string handle, string categoryCode, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);

        Source = source.ToLowerInvariant();
        Handle = handle.ToLowerInvariant();
        PointTo(categoryCode, at);
    }

    public string Source { get; private set; } = default!;

    /// <summary>The collection's name in the source's URLs, lowercase as Shopify writes it.</summary>
    public string Handle { get; private set; } = default!;

    public string CategoryCode { get; private set; } = default!;

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The collection is this category now; a re-import may move it, as the store's products move.</summary>
    public void PointTo(string categoryCode, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryCode);

        CategoryCode = AttributeDefinition.Normalise(categoryCode);
        UpdatedAt = at;
    }
}
