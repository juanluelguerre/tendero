using ElGuerre.Tendero.Catalog.Adapters;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
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

    /// <summary>The colours the catalogue defines: black, grey, and a stainless steel that may be taken for grey.</summary>
    private sealed class Colours : IAttributeDefinitionReader
    {
        public Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default)
        {
            var colour = AttributeDefinition.Define(Clock, "COLOR", LocalizedText.From("es", "color").With("en", "colour"), AttributeKind.Option, isVariantAxis: true);
            colour.AddOption(Clock, "BLACK", LocalizedText.From("es", "negro").With("en", "black"));
            colour.AddOption(Clock, "GREY", LocalizedText.From("es", "gris").With("en", "grey"));
            colour.AddOption(Clock, "STAINLESS_STEEL", LocalizedText.From("es", "acero inoxidable").With("en", "stainless steel"), mistakenFor: ["grey"]);
            return Task.FromResult(new AttributeDefinitions([colour]));
        }
    }

    private static ProductPicturesReader Reader(params Product[] products) => new(new InMemoryProductCatalogReader(products), new Colours());

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

        var listed = await Reader(mug).AllAsync(TestContext.Current.CancellationToken);

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

        var listed = await Reader(first, second).AllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, listed.Select(picture => picture.ImageId));
    }

    [Fact]
    public async Task An_archived_products_pictures_are_nobodys_to_read()
    {
        var archived = AProduct("Taza", null, ("mug-cover", null));
        archived.Archive(Clock);

        Assert.Empty(await Reader(archived).AllAsync(TestContext.Current.CancellationToken));
    }
    // ---------- what a picture is of: the colour it is held to (I-4d) ----------

    private static async Task<Dictionary<string, string?>> ColoursOf(Product product) =>
        (await Reader(product).AllAsync(TestContext.Current.CancellationToken)).ToDictionary(picture => picture.ImageId, picture => picture.Colour);

    /// <summary>
    /// What an import brings: one way of being bought, and the colour as the
    /// product's own. Its cover shows that colour; a second picture of the
    /// gallery — a detail, the back — is of no variant in particular.
    /// </summary>
    [Fact]
    public async Task The_cover_of_a_product_of_one_colour_is_held_to_that_colour_and_its_other_pictures_to_none()
    {
        var sleeve = AProduct("Funda", null, ("cover", null), ("detail", null));
        sleeve.SetAttribute(Clock, AttributeValue.Option("COLOR", "BLACK"));
        sleeve.AddVariant(Clock, "SLEEVE-DEFAULT", new Money(10m, "EUR"));

        Assert.Equal(new() { ["cover"] = "BLACK", ["detail"] = null }, await ColoursOf(sleeve));
    }

    /// <summary>
    /// Two colours and a picture of each: a variant's own picture shows that
    /// variant, whatever its size. The cover is nobody's who has a picture.
    /// </summary>
    [Fact]
    public async Task A_variants_own_picture_is_held_to_that_variants_colour()
    {
        var pack = AProduct("Mochila", null, ("black-pack", null), ("grey-pack", null));
        pack.DefineAxes(Clock, ["COLOR", "SIZE"]);
        foreach (var (colour, picture) in new[] { ("BLACK", "black-pack"), ("GREY", "grey-pack") })
            foreach (var size in new[] { "S", "L" })
                pack.AddVariant(Clock, $"PACK-{colour}-{size}", new Money(10m, "EUR"), new Dictionary<string, string> { ["COLOR"] = colour, ["SIZE"] = size }, image: new ImageId(picture));

        Assert.Equal(new() { ["black-pack"] = "BLACK", ["grey-pack"] = "GREY" }, await ColoursOf(pack));
    }

    /// <summary>One picture for two colours has no colour to be held to: it cannot be both.</summary>
    [Fact]
    public async Task A_cover_shared_by_variants_of_two_colours_is_held_to_none()
    {
        var pack = AProduct("Mochila", null, ("cover", null));
        pack.DefineAxes(Clock, ["COLOR"]);
        pack.AddVariant(Clock, "PACK-BLACK", new Money(10m, "EUR"), new Dictionary<string, string> { ["COLOR"] = "BLACK" });
        pack.AddVariant(Clock, "PACK-GREY", new Money(10m, "EUR"), new Dictionary<string, string> { ["COLOR"] = "GREY" });

        Assert.Equal(new() { ["cover"] = null }, await ColoursOf(pack));
    }

    /// <summary>A colour that is no longer sold does not decide what a picture is held to.</summary>
    [Fact]
    public async Task A_discontinued_variant_does_not_count()
    {
        var pack = AProduct("Mochila", null, ("cover", null));
        pack.DefineAxes(Clock, ["COLOR"]);
        pack.AddVariant(Clock, "PACK-BLACK", new Money(10m, "EUR"), new Dictionary<string, string> { ["COLOR"] = "BLACK" });
        pack.AddVariant(Clock, "PACK-GREY", new Money(10m, "EUR"), new Dictionary<string, string> { ["COLOR"] = "GREY" });
        pack.DiscontinueVariant(Clock, "PACK-GREY");

        Assert.Equal(new() { ["cover"] = "BLACK" }, await ColoursOf(pack));
    }

    [Fact]
    public async Task A_product_that_says_no_colour_has_none_to_be_held_to()
    {
        var mug = AProduct("Taza", null, ("cover", null));
        mug.AddVariant(Clock, "MUG-DEFAULT", new Money(10m, "EUR"));

        Assert.Equal(new() { ["cover"] = null }, await ColoursOf(mug));
    }

    /// <summary>
    /// The colours a picture chooses among are the catalogue's, in both
    /// languages — and what may be taken for what is declared once and true of
    /// both: grey for stainless steel, and stainless steel for grey.
    /// </summary>
    [Fact]
    public async Task The_colours_a_picture_chooses_among_are_the_catalogues_and_a_mistake_is_one_in_both_directions()
    {
        var colours = await Reader().ColoursAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [("BLACK", "negro", "black"), ("GREY", "gris", "grey"), ("STAINLESS_STEEL", "acero inoxidable", "stainless steel")],
            colours.Select(colour => (colour.Code, colour.Spanish, colour.English)));
        Assert.Equal<IEnumerable<string>>([[], ["STAINLESS_STEEL"], ["GREY"]], colours.Select(colour => colour.MistakenFor));
    }
    /// <summary>
    /// A picture also says what the product's other variants hold: seeing one of
    /// those colours in it is how a picture filed under the wrong variant is
    /// told from a picture that is simply wrong. A product of one colour has no
    /// others, and neither has a picture that is held to none.
    /// </summary>
    [Fact]
    public async Task A_picture_says_the_colours_the_products_other_variants_hold()
    {
        var pack = AProduct("Mochila", null, ("black-pack", null), ("grey-pack", null), ("detail", null));
        pack.DefineAxes(Clock, ["COLOR"]);
        pack.AddVariant(Clock, "PACK-BLACK", new Money(10m, "EUR"), new Dictionary<string, string> { ["COLOR"] = "BLACK" }, image: new ImageId("black-pack"));
        pack.AddVariant(Clock, "PACK-GREY", new Money(10m, "EUR"), new Dictionary<string, string> { ["COLOR"] = "GREY" }, image: new ImageId("grey-pack"));
        // A colour that is no longer sold is nobody's picture to be mistaken for.
        pack.AddVariant(Clock, "PACK-STEEL", new Money(10m, "EUR"), new Dictionary<string, string> { ["COLOR"] = "STAINLESS_STEEL" });
        pack.DiscontinueVariant(Clock, "PACK-STEEL");
        var sleeve = AProduct("Funda", null, ("cover", null));
        sleeve.SetAttribute(Clock, AttributeValue.Option("COLOR", "BLACK"));
        sleeve.AddVariant(Clock, "SLEEVE-DEFAULT", new Money(10m, "EUR"));

        var others = (await Reader(pack, sleeve).AllAsync(TestContext.Current.CancellationToken))
            .ToDictionary(picture => picture.ImageId, picture => picture.OtherColours ?? []);

        Assert.Equal<IEnumerable<string>>(["GREY"], others["black-pack"]);
        Assert.Equal<IEnumerable<string>>(["BLACK"], others["grey-pack"]);
        Assert.Empty(others["detail"]);
        Assert.Empty(others["cover"]);
    }
}
