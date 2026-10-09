using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.SetImageAltText;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// Which pictures nobody has described (phase 13a, I-3): per picture and per
/// language, as values — what the job that proposes alt text asks, without ever
/// being handed a product.
/// </summary>
public sealed class UndescribedPicturesReaderTests
{
    private static readonly TestClock Clock = new();

    private static Product AProduct(string spanish, string? english, params (string Image, LocalizedText? Alt)[] gallery)
    {
        var name = LocalizedText.From("es", spanish);
        var product = Product.Create(Clock, english is null ? name : name.With("en", english), new Money(10m, "EUR"));
        foreach (var (image, alt) in gallery)
            product.AddImage(Clock, new ImageId(image), alt);
        return product;
    }

    [Fact]
    public async Task A_picture_is_listed_with_the_languages_it_is_not_described_in_and_a_described_one_is_not()
    {
        var mug = AProduct("Taza", "Mug",
            ("mug-cover", null),
            ("mug-side", LocalizedText.From("es", "La taza de lado.")),
            ("mug-top", LocalizedText.Both("The mug from above.", "La taza desde arriba.")));

        var listed = await new UndescribedPicturesReader(new InMemoryProductCatalogReader(mug)).AllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["mug-cover", "mug-side"], listed.Select(picture => picture.ImageId));
        Assert.Equal(["es", "en"], listed[0].Missing);
        Assert.Equal(["en"], listed[1].Missing);
        // What the model is told the picture is of: the product's own words, in English when it has them.
        Assert.Equal((mug.Id.Value, mug.Code, "Mug"), (listed[0].ProductId, listed[0].Code, listed[0].Name));
    }

    /// <summary>The order is the same on every run, so a batch of five is the same five until one of them is described.</summary>
    [Fact]
    public async Task Pictures_come_by_product_code_and_then_by_their_place_in_the_gallery()
    {
        var first = AProduct("Sartén", null, ("pan-2", null));
        var second = AProduct("Cafetera", null, ("pot-1", null), ("pot-2", null));
        var expected = new[] { first, second }.OrderBy(product => product.Code, StringComparer.Ordinal)
            .SelectMany(product => product.Images.OrderBy(image => image.SortOrder).Select(image => image.Id.Value));

        var listed = await new UndescribedPicturesReader(new InMemoryProductCatalogReader(first, second)).AllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, listed.Select(picture => picture.ImageId));
        // A product with no English name is still named: in the language it has.
        Assert.All(listed, picture => Assert.False(string.IsNullOrWhiteSpace(picture.Name)));
    }

    [Fact]
    public async Task An_archived_products_pictures_are_nobodys_to_describe()
    {
        var archived = AProduct("Taza", null, ("mug-cover", null));
        archived.Archive(Clock);

        Assert.Empty(await new UndescribedPicturesReader(new InMemoryProductCatalogReader(archived)).AllAsync(TestContext.Current.CancellationToken));
    }
}
