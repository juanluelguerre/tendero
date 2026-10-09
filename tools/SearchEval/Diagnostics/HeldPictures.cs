using System.Security.Cryptography;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.SearchEval.Diagnostics;

/// <summary>
/// The seed's pictures in memory, keyed on the hash of their bytes as the shop's
/// store keys them. The evaluation measures what the pictures rank, and a folder
/// on disk in the middle would only add ways to fail that are not that.
/// </summary>
internal sealed class HeldPictures : IImageStore
{
    private readonly Dictionary<ImageId, (byte[] Bytes, string ContentType)> held = [];

    /// <summary>Every picture held, once each: what the floors are measured over.</summary>
    public IReadOnlyCollection<(ImageId Id, byte[] Bytes)> All => [.. this.held.Select(entry => (entry.Key, entry.Value.Bytes))];

    public async Task<ImageId> SaveAsync(Stream content, string contentType, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        var bytes = buffer.ToArray();
        var id = new ImageId("sha256-" + Convert.ToHexStringLower(SHA256.HashData(bytes)));
        this.held[id] = (bytes, contentType);
        return id;
    }

    public Task<StoredImage?> OpenAsync(ImageId id, CancellationToken ct = default) =>
        Task.FromResult(this.held.TryGetValue(id, out var picture) ? new StoredImage(new MemoryStream(picture.Bytes), picture.ContentType) : null);
}
