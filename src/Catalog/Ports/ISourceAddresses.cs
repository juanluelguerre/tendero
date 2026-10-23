using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// What a source's old addresses lead to here (phase 16, P16-11): a product by
/// the handle the import kept beside its external id, a collection by the
/// category the import kept it as. Read by the redirects, written by the import.
/// </summary>
public interface ISourceAddresses
{
    /// <summary>The product a source called by this handle, or null. Handles are lowercase.</summary>
    Task<ProductAddress?> ProductAsync(string handle, CancellationToken ct);

    /// <summary>The category a source's collection is here, or null. Handles are lowercase.</summary>
    Task<string?> CategoryAsync(string collectionHandle, CancellationToken ct);
}

/// <summary>What a product's page address is made of, and whether the page is there.</summary>
public sealed record ProductAddress(string Code, LocalizedText Slug, ProductStatus Status);
