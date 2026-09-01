using System.Security.Cryptography;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Catalog.Adapters;

public sealed class FileSystemImageStoreOptions
{
    public const string SectionName = "Catalog:Images";

    /// <summary>Raíz del almacén. Fuera del repositorio: son datos, no código.</summary>
    public string RootPath { get; set; } = Path.Combine(Path.GetTempPath(), "tendero-images");
}

/// <summary>
/// Adaptador de sistema de ficheros. Los ficheros se reparten en subcarpetas por
/// los dos primeros caracteres del hash: un directorio con 147k entradas es
/// lento de listar en cualquier sistema de ficheros.
/// </summary>
internal sealed class FileSystemImageStore(
    IOptions<FileSystemImageStoreOptions> options,
    ILogger<FileSystemImageStore> logger) : IImageStore
{
    private static readonly Dictionary<string, string> ExtensionByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp",
        ["image/avif"] = ".avif",
        ["image/gif"] = ".gif",
        ["image/svg+xml"] = ".svg"
    };

    private static readonly Dictionary<string, string> ContentTypeByExtension =
        ExtensionByContentType.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    public async Task<ImageId> SaveAsync(Stream content, string contentType, CancellationToken ct = default)
    {
        // A temporal primero: hasta no tener el hash no se sabe el destino, y
        // hashear en memoria no escala a un catálogo entero.
        var extension = ExtensionByContentType.GetValueOrDefault(contentType, ".bin");
        var staging = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        string hash;
        await using (var buffer = File.Create(staging))
        {
            await content.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(buffer, ct));
        }

        var id = new ImageId(hash);
        var destination = PathFor(hash, extension);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        try
        {
            // Move sin overwrite, y el choque tratado como éxito. Un `File.Exists`
            // previo deja una ventana entre la comprobación y el movimiento: dos
            // importaciones con la misma foto la cruzan y la segunda lanza. Da
            // igual quién gane — el contenido es idéntico, ésa es la premisa del
            // direccionamiento por hash.
            File.Move(staging, destination, overwrite: false);
        }
        catch (IOException) when (File.Exists(destination))
        {
            logger.LogDebug("Image {ImageId} already stored", id);
        }
        finally
        {
            // Si el movimiento no llegó a ocurrir, el temporal se queda huérfano
            // para siempre: son 147k ficheros en el peor caso.
            if (File.Exists(staging))
                File.Delete(staging);
        }

        return id;
    }

    public Task<StoredImage?> OpenAsync(ImageId id, CancellationToken ct = default)
    {
        var folder = FolderFor(id.Value);
        if (!Directory.Exists(folder))
            return Task.FromResult<StoredImage?>(null);

        var found = Directory.EnumerateFiles(folder, id.Value + ".*").FirstOrDefault();
        if (found is null)
            return Task.FromResult<StoredImage?>(null);

        var contentType = ContentTypeByExtension.GetValueOrDefault(
            Path.GetExtension(found), "application/octet-stream");

        return Task.FromResult<StoredImage?>(
            new StoredImage(File.OpenRead(found), contentType));
    }

    private string FolderFor(string hash) => Path.Combine(options.Value.RootPath, hash[..2]);

    private string PathFor(string hash, string extension) =>
        Path.Combine(FolderFor(hash), hash + extension);
}
