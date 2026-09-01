using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Ports;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Catalog.Adapters;

/// <summary>
/// Lee imágenes de origen por HTTP o del disco, según el esquema del Uri. Es el
/// único sitio que sabe que un origen puede ser remoto: ni el slice de
/// importación ni el dominio se enteran.
/// </summary>
internal sealed class ExternalImageReader(
    HttpClient http,
    ILogger<ExternalImageReader> logger) : IExternalImageReader
{
    public async Task<StoredImage?> OpenAsync(ExternalImage image, CancellationToken ct = default)
    {
        try
        {
            if (image.Location.IsFile)
                return OpenFile(image.Location);

            var response = await http.GetAsync(image.Location, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Image {Location} answered {Status}", image.Location, response.StatusCode);
                return null;
            }

            return new StoredImage(
                await response.Content.ReadAsStreamAsync(ct),
                response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            // Una imagen ilegible no puede tumbar la importación de un catálogo
            // entero: se registra, el producto entra sin ella y se reintenta en
            // la siguiente importación.
            logger.LogWarning(exception, "Could not read image {Location}", image.Location);
            return null;
        }
    }

    private static StoredImage? OpenFile(Uri location)
    {
        var path = location.LocalPath;
        if (!File.Exists(path))
            return null;

        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };

        return new StoredImage(File.OpenRead(path), contentType);
    }
}
