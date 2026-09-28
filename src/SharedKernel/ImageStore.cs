namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// The image store. A port, not an implementation: today there is a file
/// system adapter; the day scaling demands it, an S3 one arrives (SeaweedFS
/// locally, R2/S3/B2 in production) without touching the domain — exactly as
/// with the catalogue connectors and the payment providers.
///
/// Content is addressed by its hash: storing the same image twice returns the
/// same <see cref="ImageId"/> and writes once.
///
/// **It lives in the kernel since a second context stores images** (R-8, ADR
/// 0044): a campaign's picture is stored and served as a product's is, by id
/// through <c>/api/images/{id}</c>. Catalog wrote the port and still owns the
/// adapter; Pricing may reference the kernel and nothing else, so the port moved
/// down rather than Pricing reaching into Catalog.
/// </summary>
public interface IImageStore
{
    Task<ImageId> SaveAsync(Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Returns null when the key does not exist. A non-existent id is a
    /// 404 and not an exception: keys come from the URL and users can invent them.</summary>
    Task<StoredImage?> OpenAsync(ImageId id, CancellationToken ct = default);
}

public sealed record StoredImage(Stream Content, string ContentType);
