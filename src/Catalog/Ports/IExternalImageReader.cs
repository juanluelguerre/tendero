using ElGuerre.Tendero.Catalog.Connectors;

namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// Abre el contenido de una <see cref="ExternalImage"/>, venga de donde venga.
/// Aísla al slice de importación de saber si detrás hay HTTP o disco.
/// </summary>
public interface IExternalImageReader
{
    /// <summary>Null si la imagen no se puede leer (404, DNS caído, fichero que
    /// no está). Una imagen ilegible no puede abortar la importación de un
    /// catálogo de 147k productos.</summary>
    Task<StoredImage?> OpenAsync(ExternalImage image, CancellationToken ct = default);
}
