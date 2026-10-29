namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// A source's collections as the categories they are here (phase 16, P16-11):
/// the table the import fills and the redirects read, so a store's
/// <c>/collections/menaje</c> keeps leading somewhere after the shop has left it.
/// Written within the import's unit of work, which commits it.
/// </summary>
public interface ISourceCollections
{
    /// <summary>The collection is this category now: added, or pointed at it again.</summary>
    Task KeepAsync(string source, string handle, string categoryCode, CancellationToken ct);
}
