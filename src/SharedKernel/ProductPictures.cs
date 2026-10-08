namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// A picture of a product in the catalogue (phase 13a, I-4), as values: enough to
/// open it and to say whose it is — and nothing of the product itself. Not its
/// name: the model that reads the picture is told nothing of what it is of.
/// </summary>
/// <param name="ProductId">The product the picture is of.</param>
/// <param name="Code">The product's code: what a person reads of it.</param>
/// <param name="ImageId">The picture, by the id the image store keeps it under.</param>
/// <param name="Colour">The colour the picture has to be held to (phase 13a, I-4d):
/// the code of the one colour that the variants it shows hold — or null when it
/// shows no variant in particular, or variants of more than one colour.</param>
public sealed record ProductPicture(Guid ProductId, string Code, string ImageId, string? Colour = null);

/// <summary>A colour the catalogue defines, as values: what a picture may be asked to choose among.</summary>
/// <param name="MistakenFor">The codes of the colours the catalogue says this one may be
/// taken for — stainless steel and grey — in both directions: seeing one where
/// the other is held is not a difference.</param>
public sealed record PictureColour(string Code, string Spanish, string English, IReadOnlyList<string> MistakenFor);

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

    /// <summary>
    /// The colours a picture is asked to choose among: the options the catalogue
    /// defines for a product's colour. None, for a catalogue that defines none —
    /// and then no picture is asked its colour.
    /// </summary>
    Task<IReadOnlyList<PictureColour>> ColoursAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PictureColour>>([]);
}
