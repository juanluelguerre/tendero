using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The attributes a claim about a product may be typed with, as Catalog gives
/// them to Knowledge (ADR 0014): which of them, by the product's category (ADR
/// 0056) and by what the claim was read from. Over the seed — its products
/// imported into a real Postgres, its definitions and its categories read from
/// their files — so what is held is what the shop is offered.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AttributeVocabularyTests(PostgresFixture postgres)
{
    private static string Data(string file) => Path.Combine(AppContext.BaseDirectory, "TestData", file);

    private async Task<Func<string?, SourceKind, Task<IReadOnlyList<string>>>> Shop(string database)
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync(database);
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Catalog:Connectors:Seed:FilePath"] = Data("products.sample.json"),
                ["Catalog:Attributes:FilePath"] = Data("attributes.sample.json"),
                ["Catalog:Categories:FilePath"] = Data("categories.sample.json"),
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(typeof(ImportProductsCommand).Assembly);
        services.AddTenderoPersistence(factory.ConnectionString);
        services.AddCatalog(configuration);
        services.AddSingleton<IPrincipalAccessor, SystemPrincipalAccessor>();
        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new ImportProductsCommand("seed"), ct);

        return async (category, readFrom) =>
        {
            ProductId product;
            await using (var context = factory.Create())
            {
                product = category is null
                    ? ProductId.New()
                    : (await context.Products.FirstAsync(found => found.Category == category, ct)).Id;
            }

            await using var scope = provider.CreateAsyncScope();
            var vocabulary = await scope.ServiceProvider.GetRequiredService<IAttributeVocabulary>().CurrentAsync(product, readFrom, ct);
            return [.. vocabulary.Select(attribute => attribute.Code)];
        };
    }

    /// <summary>
    /// A description is offered what its product's category declares — its own
    /// and its ancestors' — and nothing of another's: cookware is not offered a
    /// lamp's colour temperature, nor a coffee maker's cups. Never what tells
    /// variants apart, nor a size range.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_description_is_offered_the_attributes_of_its_products_category()
    {
        var offered = await Shop("vocabulary_by_category");

        var cookware = await offered("COOKWARE", SourceKind.ProductDescription);
        var shoes = await offered("SHOES", SourceKind.ProductDescription);

        Assert.Equal(["CAPACITY_L", "DIAMETERS_CM", "INDUCTION", "MATERIAL", "PIECES"], cookware.Order(StringComparer.Ordinal));
        // COLOR and SIZE are variant axes, and the size range is the import's: offered to no source.
        Assert.Equal(["DROP_MM", "GENDER", "MATERIAL"], shoes.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A picture is asked only what a picture shows, of what the category
    /// declares: how many, for cookware — and nothing at all for a shoe, where a
    /// pair counted as two is not two pieces.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_picture_is_offered_what_a_picture_shows_of_what_the_category_declares()
    {
        var offered = await Shop("vocabulary_for_pictures");

        Assert.Equal(["PIECES"], await offered("COOKWARE", SourceKind.ProductImage));
        Assert.Empty(await offered("SHOES", SourceKind.ProductImage));
    }

    /// <summary>
    /// A product the catalogue does not have — or one of no category — has had
    /// nothing declared for it: it is offered what every product was before there
    /// were declarations.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_product_nothing_is_declared_for_is_offered_every_attribute()
    {
        var offered = await Shop("vocabulary_undeclared");

        var description = await offered(null, SourceKind.ProductDescription);

        Assert.Contains("CUPS", description);
        Assert.Contains("COLOUR_TEMPERATURE", description);
        Assert.Contains("PIECES", description);
        Assert.DoesNotContain("COLOR", description);
        Assert.DoesNotContain("SIZE_RANGE", description);
        Assert.Equal(["PIECES"], await offered(null, SourceKind.ProductImage));
    }
}
