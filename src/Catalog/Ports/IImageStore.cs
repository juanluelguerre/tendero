using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// The product image store. A port, not an implementation: today there is a file
/// system adapter; the day scaling demands it, an S3 one arrives (SeaweedFS
/// locally, R2/S3/B2 in production) without touching the domain — exactly as
/// with the catalogue connectors and the payment providers.
///
/// Content is addressed by its hash: storing the same image twice returns the
/// same <see cref="ImageId"/> and writes once.
/// </summary>
public interface IImageStore
{
    Task<ImageId> SaveAsync(Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Returns null when the key does not exist. A non-existent id is a
    /// 404 and not an exception: keys come from the URL and users can invent them.</summary>
    Task<StoredImage?> OpenAsync(ImageId id, CancellationToken ct = default);
}

public sealed record StoredImage(Stream Content, string ContentType);
