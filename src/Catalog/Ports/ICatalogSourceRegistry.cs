using ElGuerre.Tendero.Catalog.Connectors;

namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// Resuelve un conector por el nombre de su origen. Existe para que el slice de
/// importación no tenga que inyectar el <c>IServiceProvider</c>: pedirle el
/// contenedor a un handler es un service locator — depende de todo, no declara
/// nada, y obliga a montar un contenedor entero para probar un bucle.
///
/// El origen llega en la petición, así que la resolución no puede ser una
/// inyección normal; lo que sí puede es estar detrás de un puerto, que es
/// exactamente lo que hace el resto del sistema con Elasticsearch o con el
/// almacén de imágenes (ADR 0003).
/// </summary>
public interface ICatalogSourceRegistry
{
    /// <summary>Los orígenes registrados, en orden estable. Es lo que permite
    /// que un origen desconocido se rechace con un 400 que dice cuáles hay, en
    /// vez de con un 500 del contenedor de dependencias.</summary>
    IReadOnlyCollection<string> Sources { get; }

    ICatalogSourceConnector Get(string source);
}

/// <summary>
/// Nadie ha registrado un conector con ese nombre. Es un error del llamante, no
/// del servidor: la validación del comando lo convierte en 400 antes de llegar
/// aquí, y esto es la red por debajo para quien invoque el puerto directamente.
/// </summary>
public sealed class UnknownCatalogSourceException(string source, IEnumerable<string> known)
    : Exception($"Unknown catalog source '{source}'. Registered sources: {string.Join(", ", known)}.")
{
    public string RequestedSource { get; } = source;
}
