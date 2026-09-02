using System.Security.Cryptography;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Catalog.Adapters;

public sealed class FileSystemImageStoreOptions
{
    public const string SectionName = "Catalog:Images";

    /// <summary>The store's root. Outside the repository: it is data, not code.</summary>
    public string RootPath { get; set; } = Path.Combine(Path.GetTempPath(), "tendero-images");
}

/// <summary>
/// The file system adapter. Files are spread across subfolders by the first two
/// characters of the hash: a directory with 147k entries is slow to list on any
/// file system.
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
        // Staging first: the destination is unknown until the hash is known, and
        // hashing in memory does not scale to a whole catalogue.
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
            // Move without overwrite, and a collision treated as success. A
            // preceding `File.Exists` leaves a window between the check and the
            // move: two imports of the same photo cross it and the second throws.
            // It does not matter who wins — the content is identical, which is
            // the premise of content addressing.
            File.Move(staging, destination, overwrite: false);
        }
        catch (IOException) when (File.Exists(destination))
        {
            logger.LogDebug("Image {ImageId} already stored", id);
        }
        finally
        {
            // If the move never happened, the staging file is orphaned forever:
            // 147k files in the worst case.
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
