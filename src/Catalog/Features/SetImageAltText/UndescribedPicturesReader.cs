using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Features.SetImageAltText;

/// <summary>
/// Catalog's answer to <see cref="IUndescribedPictures"/>: every picture of a
/// product in the catalogue that is not described in each language the shop
/// speaks. Per picture and per language — <c>catalogue_gaps</c> answers per
/// product and for one language, which is what a person reads and not what a job
/// that describes pictures needs.
///
/// In memory over the live catalogue: a hundred products (the scale rule's
/// "deferred with its number"); the full catalogue makes it SQL.
/// </summary>
public sealed class UndescribedPicturesReader(IProductCatalogReader products) : IUndescribedPictures
{
    public async Task<IReadOnlyList<UndescribedPicture>> AllAsync(CancellationToken cancellationToken = default) =>
    [
        .. (await products.LiveAsync(cancellationToken))
            .OrderBy(product => product.Code, StringComparer.Ordinal)
            .SelectMany(product => product.Images
                .OrderBy(image => image.SortOrder)
                .Select(image => (Image: image, Missing: CultureNegotiation.Supported
                    .Where(culture => image.Alt is null || !image.Alt.Cultures.Contains(culture))
                    .ToList()))
                .Where(picture => picture.Missing.Count > 0)
                .Select(picture => new UndescribedPicture(
                    product.Id.Value, product.Code, product.Name.In("en"), product.Description?.In("en"),
                    picture.Image.Id.Value, picture.Missing))),
    ];
}
