using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The seed's product in three colours, imported for real (phase 13a, I-4d):
/// the seed file as it is committed, its pictures stored, a real Postgres. What
/// only the whole path proves — the file's <c>variants</c> become variants on
/// the colour axis, each with the picture the file says is its own, and the
/// pictures' reader then holds each picture to its own variant's colour. That is
/// what a doubt of *it belongs to another variant* is raised against. Its fourth
/// picture, the back, is no variant's: it is held to no colour at all.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class VariantPicturesFromTheSeedTests(PostgresFixture postgres)
{
    private const string Pannier = "B13BAG0711";

    private static string Seed(string file)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Tendero.slnx")))
            folder = folder.Parent;
        return Path.Combine(folder!.FullName, "seed", file);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_seeds_product_in_three_colours_has_a_variant_and_a_picture_of_each_and_each_picture_is_held_to_its_own()
    {
        postgres.SkipIfUnavailable();
        Assert.SkipUnless(
            new[] { "", "-BLACK", "-NAVY_BLUE", "-BACK" }.All(picture => File.Exists(Seed(Path.Combine("images", $"{Pannier}{picture}.webp")))),
            "needs the pannier's four pictures in the seed");
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("seed_variant_pictures");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var images = Directory.CreateTempSubdirectory("tendero-variant-pictures-");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // The committed seed itself, so its pictures resolve beside it.
                    ["Catalog:Connectors:Seed:FilePath"] = Seed("products.sample.json"),
                    ["Catalog:Attributes:FilePath"] = Seed("attributes.sample.json"),
                    ["Catalog:Categories:FilePath"] = Seed("categories.sample.json"),
                    ["Catalog:Images:RootPath"] = images.FullName,
                })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTenderoCqrs(typeof(ImportProductsCommand).Assembly);
            services.AddTenderoPersistence(factory.ConnectionString);
            services.AddCatalog(configuration);
            services.AddSingleton<IPrincipalAccessor, SystemPrincipalAccessor>();
            await using var provider = services.BuildServiceProvider();

            // Twice: a re-import must find the two variants and add none.
            for (var import = 0; import < 2; import++)
            {
                await using var scope = provider.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new ImportProductsCommand("seed"), ct);
            }

            await using var context = factory.Create();
            var pannier = (await context.Products.ToListAsync(ct)).Single(product => product.ExternalReferences.Any(reference => reference.ExternalId == Pannier));

            Assert.Equal(["COLOR"], pannier.VariantAxes);
            Assert.Equal(
                [($"{Pannier}-BLACK", "BLACK"), ($"{Pannier}-NAVY", "NAVY_BLUE"), ($"{Pannier}-OLIVE", "OLIVE")],
                pannier.Variants.OrderBy(variant => variant.Sku, StringComparer.Ordinal).Select(variant => (variant.Sku, variant.AxisValues["COLOR"])));
            Assert.Equal(4, pannier.Images.Count);

            // The gallery in the file's order: the cover, the black one, the navy one, the back.
            ImageId[] gallery = [.. pannier.Images.OrderBy(image => image.SortOrder).Select(image => image.Id)];
            var cover = pannier.PrimaryImage!.Id;
            Assert.Equal(cover, gallery[0]);
            ImageId? PictureOf(string sku) => pannier.Variants.Single(variant => variant.Sku.EndsWith(sku, StringComparison.Ordinal)).Image;
            Assert.Equal((gallery[0], gallery[1], gallery[2]), (PictureOf("OLIVE"), PictureOf("BLACK"), PictureOf("NAVY")));
            // The colour is the variants' to say: the product says none of its own.
            Assert.Null(pannier.AttributeFor("COLOR"));

            await using var reading = provider.CreateAsyncScope();
            var pictures = (await reading.ServiceProvider.GetRequiredService<IProductPictures>().AllAsync(ct))
                .Where(picture => picture.ProductId == pannier.Id.Value)
                .ToDictionary(picture => picture.ImageId, picture => picture.Colour);

            Assert.Equal(
                new() { [gallery[0].Value] = "OLIVE", [gallery[1].Value] = "BLACK", [gallery[2].Value] = "NAVY_BLUE", [gallery[3].Value] = null },
                pictures);
        }
        finally
        {
            images.Delete(recursive: true);
        }
    }
}
