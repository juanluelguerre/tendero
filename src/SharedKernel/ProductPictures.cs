namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// A picture of a product in the catalogue (phase 13a, I-4), as values: enough to
/// open it and to say whose it is — and nothing of the product itself. Not its
/// name: the model that reads the picture is told nothing of what it is of.
/// </summary>
/// <param name="ProductId">The product the picture is of.</param>
/// <param name="Code">The product's code: what a person reads of it.</param>
/// <param name="ImageId">The picture, by the id the image store keeps it under.</param>
public sealed record ProductPicture(Guid ProductId, string Code, string ImageId);

/// <summary>
/// The pictures of the catalogue's products. A port in the kernel because the
/// answer is Catalog's and the one who asks — Knowledge, to have each one read —
/// may not reference it (invariant 1): what crosses is values.
/// </summary>
public interface IProductPictures
{
    /// <summary>Every picture of every product in the catalogue (not archived), in
    /// a stable order: by product code, then by their place in the gallery.</summary>
    Task<IReadOnlyList<ProductPicture>> AllAsync(CancellationToken cancellationToken = default);
}
