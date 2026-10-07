using ElGuerre.Tendero.Catalog.Adapters;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// Every picture of the catalogue, as values (phase 13a, I-4): what the job that
/// has pictures read asks, described or not, without ever being handed a product.
/// </summary>
public sealed class ProductPicturesReaderTests
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

    /// <summary>A described picture is read too: its alt text is a person's sentence for a reader, not what a claim rests on.</summary>
    [Fact]
    public async Task Every_picture_is_listed_whether_or_not_somebody_described_it()
    {
        var mug = AProduct("Taza", "Mug",
            ("mug-cover", null),
            ("mug-top", LocalizedText.Both("The mug from above.", "La taza desde arriba.")));

        var listed = await new ProductPicturesReader(new InMemoryProductCatalogReader(mug)).AllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["mug-cover", "mug-top"], listed.Select(picture => picture.ImageId));
        Assert.All(listed, picture => Assert.Equal((mug.Id.Value, mug.Code), (picture.ProductId, picture.Code)));
    }

    /// <summary>The order is the same on every run, so a batch is the same pictures until one of them is read.</summary>
    [Fact]
    public async Task Pictures_come_by_product_code_and_then_by_their_place_in_the_gallery()
    {
        var first = AProduct("Sartén", null, ("pan-2", null));
        var second = AProduct("Cafetera", null, ("pot-1", null), ("pot-2", null));
        var expected = new[] { first, second }.OrderBy(product => product.Code, StringComparer.Ordinal)
            .SelectMany(product => product.Images.OrderBy(image => image.SortOrder).Select(image => image.Id.Value));

        var listed = await new ProductPicturesReader(new InMemoryProductCatalogReader(first, second)).AllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, listed.Select(picture => picture.ImageId));
    }

    [Fact]
    public async Task An_archived_products_pictures_are_nobodys_to_read()
    {
        var archived = AProduct("Taza", null, ("mug-cover", null));
        archived.Archive(Clock);

        Assert.Empty(await new ProductPicturesReader(new InMemoryProductCatalogReader(archived)).AllAsync(TestContext.Current.CancellationToken));
    }
}
