using ElGuerre.Tendero.Catalog.Connectors;

namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// Resolves a connector by its source name. It exists so the import slice does
/// not have to inject the <c>IServiceProvider</c>: handing a handler the
/// container is a service locator — it depends on everything, declares nothing,
/// and forces you to stand up a whole container to test a loop.
///
/// The source arrives in the request, so resolution cannot be ordinary
/// injection; what it can be is behind a port, which is exactly what the rest of
/// the system does with Elasticsearch or with the image store (ADR 0003).
/// </summary>
public interface ICatalogSourceRegistry
{
    /// <summary>The registered sources, in stable order. It is what lets an
    /// unknown source be rejected with a 400 that says which ones exist, rather
    /// than with a 500 from the DI container.</summary>
    IReadOnlyCollection<string> Sources { get; }

    ICatalogSourceConnector Get(string source);
}

/// <summary>
/// Nobody registered a connector under that name. A caller's error, not the
/// server's: the command's validation turns it into a 400 before it gets here,
/// and this is the net underneath for whoever calls the port directly.
/// </summary>
public sealed class UnknownCatalogSourceException(string source, IEnumerable<string> known)
    : Exception($"Unknown catalog source '{source}'. Registered sources: {string.Join(", ", known)}.")
{
    public string RequestedSource { get; } = source;
}
