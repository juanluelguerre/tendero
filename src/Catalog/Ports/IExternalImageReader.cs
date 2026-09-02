using ElGuerre.Tendero.Catalog.Connectors;

namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// Opens the content of an <see cref="ExternalImage"/>, wherever it comes from.
/// It keeps the import slice from having to know whether HTTP or a disk is
/// behind it.
/// </summary>
public interface IExternalImageReader
{
    /// <summary>Null when the image cannot be read (404, DNS down, a file that
    /// is not there). One unreadable image cannot abort the import of a
    /// 147k-product catalogue.</summary>
    Task<StoredImage?> OpenAsync(ExternalImage image, CancellationToken ct = default);
}
