namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// A product picture with no alt text in at least one of the shop's languages
/// (phase 13a, I-3), as values: enough to open the picture, to name the product it
/// is of, and to propose its description — and nothing of the product itself.
/// </summary>
/// <param name="ProductId">The product the picture is of.</param>
/// <param name="Code">The product's code: what a person reads of it.</param>
/// <param name="Name">The product's name, in English when it has one: what the model is told the picture is of.</param>
/// <param name="Description">Its description, the same way, or none.</param>
/// <param name="ImageId">The picture, by the id the image store keeps it under.</param>
/// <param name="Missing">The languages it is not described in.</param>
public sealed record UndescribedPicture(
    Guid ProductId, string Code, string Name, string? Description, string ImageId, IReadOnlyList<string> Missing);

/// <summary>
/// Which pictures of the catalogue nobody has described. A port in the kernel
/// because the answer is Catalog's and the one who asks is Assist, which may not
/// reference it (invariant 1): what crosses is values.
/// </summary>
public interface IUndescribedPictures
{
    /// <summary>The pictures of products in the catalogue (not archived) that lack
    /// alt text in a language, in a stable order: by product code, then by their
    /// place in the gallery.</summary>
    Task<IReadOnlyList<UndescribedPicture>> AllAsync(CancellationToken cancellationToken = default);
}
