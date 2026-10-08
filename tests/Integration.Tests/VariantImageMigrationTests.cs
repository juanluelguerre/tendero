using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The migration that retires a variant's own picture
/// (`docs/analysis/catalogue-model.md`, proposal 1), over a database as it was
/// the day before: what each variant pointed at becomes what that picture says
/// it shows, and only then does the column go. Read back through the model, so
/// what the SQL writes is held to what the converter reads.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class VariantImageMigrationTests(PostgresFixture postgres)
{
    private const string Before = "20261008090131_ProductImageShows";

    private static readonly TestClock Clock = new();
    private static readonly Money Price = new(10m, "EUR");

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_picture_a_variant_pointed_at_says_that_variants_colour_and_the_others_say_nothing()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("variant_image_migration");
        await using (var old = factory.Create())
            await old.GetService<IMigrator>().MigrateAsync(Before, ct);

        var shoe = Product.Create(Clock, LocalizedText.From("es", "Zapatilla").With("en", "Shoe"), Price);
        shoe.DefineAxes(Clock, ["COLOR", "SIZE"]);
        foreach (var colour in new[] { "NAVY_BLUE", "BLACK", "OLIVE" })
            foreach (var size in new[] { "38", "39" })
                shoe.AddVariant(Clock, $"SHOE-{colour}-{size}", Price, new Dictionary<string, string> { ["COLOR"] = colour, ["SIZE"] = size });
        shoe.DiscontinueVariant(Clock, "SHOE-OLIVE-38");
        shoe.DiscontinueVariant(Clock, "SHOE-OLIVE-39");
        foreach (var picture in new[] { "navy", "black", "olive", "shared", "sole" })
            shoe.AddImage(Clock, new ImageId(picture));
        // One size: bought one way, with a picture it named all the same.
        var mug = Product.Create(Clock, LocalizedText.From("es", "Taza").With("en", "Mug"), Price);
        mug.AddVariant(Clock, "MUG-DEFAULT", Price);
        mug.AddImage(Clock, new ImageId("mug"));

        await using (var context = factory.Create())
        {
            // The model no longer knows the column; the table still has it.
            context.Products.AddRange(shoe, mug);
            await context.SaveChangesAsync(ct);

            // As it was: a variant names one picture; a colour no longer sold
            // has one too; and one picture is named by two colours still sold.
            await context.Database.ExecuteSqlAsync(
                $"""
                 update catalog.variants set image_id = case sku
                     when 'SHOE-NAVY_BLUE-38' then 'navy'
                     when 'SHOE-NAVY_BLUE-39' then 'shared'
                     when 'SHOE-BLACK-38' then 'black'
                     when 'SHOE-BLACK-39' then 'shared'
                     when 'SHOE-OLIVE-38' then 'olive'
                     when 'SHOE-OLIVE-39' then 'olive'
                     when 'MUG-DEFAULT' then 'mug'
                 end
                 """, ct);
        }

        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        await using var after = factory.Create();
        var products = await after.Products.ToListAsync(ct);
        var shows = products.Single(product => product.Id == shoe.Id).Images
            .ToDictionary(image => image.Id.Value, image => image.Shows);

        // Navy 38 pointed at it, alone.
        Assert.Equal([new AxisValue("COLOR", "NAVY_BLUE")], shows["navy"]);
        Assert.Equal([new AxisValue("COLOR", "BLACK")], shows["black"]);
        // Nobody sells olive: its picture would show on nothing.
        Assert.Empty(shows["olive"]);
        // Black 39 and navy 39 both named it: it cannot be both.
        Assert.Empty(shows["shared"]);
        Assert.Empty(shows["sole"]);
        // No colour to say.
        Assert.Empty(products.Single(product => product.Id == mug.Id).Images.Single().Shows);

        // What was written is something the aggregate would have accepted.
        var stored = products.Single(product => product.Id == shoe.Id);
        Assert.Equal(
            ["navy", "olive", "shared", "sole"],
            stored.PicturesOf(stored.Variants.Single(variant => variant.Sku == "SHOE-NAVY_BLUE-38")).Select(image => image.Id.Value));

        var columns = await after.Database
            .SqlQuery<string>($"select column_name as \"Value\" from information_schema.columns where table_schema = 'catalog' and table_name = 'variants'")
            .ToListAsync(ct);
        Assert.DoesNotContain("image_id", columns);
    }
}
