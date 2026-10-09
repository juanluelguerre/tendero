using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// What a picture shows, against a real Postgres (`docs/analysis/catalogue-model.md`,
/// proposal 1). What only a round trip proves: the tag survives the gallery's
/// jsonb, a picture that shows nothing writes null, and **a gallery stored
/// before pictures said anything** — every row there is today, with no such key
/// at all — reads as pictures that show nothing, with no migration to run.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PicturesOfVariantsPersistenceTests(PostgresFixture postgres)
{
    private static readonly TestClock Clock = new();
    private static readonly Money Price = new(10m, "EUR");

    [Fact]
    [Trait("Category", "Integration")]
    public async Task What_a_picture_shows_comes_back_and_a_gallery_stored_before_reads_as_showing_nothing()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("pictures_of_variants");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var shoe = Product.Create(Clock, LocalizedText.From("es", "Zapatilla").With("en", "Shoe"), Price);
        shoe.DefineAxes(Clock, ["COLOR", "SIZE"]);
        shoe.AddVariant(Clock, "SHOE-NAVY-38", Price, new Dictionary<string, string> { ["COLOR"] = "NAVY_BLUE", ["SIZE"] = "38" });
        shoe.AddVariant(Clock, "SHOE-BLACK-38", Price, new Dictionary<string, string> { ["COLOR"] = "BLACK", ["SIZE"] = "38" });
        foreach (var picture in new[] { "navy-side", "black-side", "sole" })
            shoe.AddImage(Clock, new ImageId(picture));
        shoe.Show(Clock, new ImageId("navy-side"), new Dictionary<string, string> { ["COLOR"] = "NAVY_BLUE" });
        shoe.Show(Clock, new ImageId("black-side"), new Dictionary<string, string> { ["COLOR"] = "BLACK", ["SIZE"] = "38" });

        await using (var context = factory.Create())
        {
            context.Products.Add(shoe);
            await context.SaveChangesAsync(ct);
        }

        await using (var reread = factory.Create())
        {
            var stored = await reread.Products.SingleAsync(product => product.Id == shoe.Id, ct);
            var shows = stored.Images.ToDictionary(image => image.Id.Value, image => image.Shows);

            Assert.Equal([new AxisValue("COLOR", "NAVY_BLUE")], shows["navy-side"]);
            Assert.Equal([new AxisValue("COLOR", "BLACK"), new AxisValue("SIZE", "38")], shows["black-side"]);
            Assert.Empty(shows["sole"]);
            Assert.Equal(
                ["black-side", "sole"],
                stored.PicturesOf(stored.Variants.Single(variant => variant.Sku == "SHOE-BLACK-38")).Select(image => image.Id.Value));

            // As it is written: the axis -> value pairs for a picture that shows something, null for one that does not.
            var json = await reread.Database
                .SqlQuery<string>($"select images::text as \"Value\" from catalog.products where id = {shoe.Id.Value}")
                .SingleAsync(ct);
            Assert.Contains("\"shows\": \"{\\\"COLOR\\\":\\\"NAVY_BLUE\\\"}\"", json, StringComparison.Ordinal);
            Assert.Contains("\"shows\": null", json, StringComparison.Ordinal);

            // A gallery as every one was stored until today: the same pictures, with no such key.
            await reread.Database.ExecuteSqlAsync(
                $"""
                 update catalog.products
                 set images = (select jsonb_agg(picture - 'shows') from jsonb_array_elements(images) as picture)
                 where id = {shoe.Id.Value}
                 """, ct);
        }

        await using var before = factory.Create();
        var old = await before.Products.SingleAsync(product => product.Id == shoe.Id, ct);

        Assert.Equal(3, old.Images.Count);
        Assert.All(old.Images, image => Assert.Empty(image.Shows));
        Assert.Equal(old.PrimaryImage!.Id, old.PictureOf(old.Variants[0])!.Id);
    }
}
