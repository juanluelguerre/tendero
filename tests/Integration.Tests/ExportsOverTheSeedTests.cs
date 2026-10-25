using System.Text;
using System.Text.Json;
using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Catalog.Features.PublishImport;
using ElGuerre.Tendero.Exports;
using ElGuerre.Tendero.Exports.Features.CatalogueForAnotherPlatform;
using ElGuerre.Tendero.Exports.Features.ProductFeed;
using ElGuerre.Tendero.Inventory;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ElGuerre.Tendero.Integration.Tests.ShopifyRoundTripTests;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The exports over the seed (phase 16, E-2 and E-3; spec *Measures*). The seed is
/// imported into an empty shop on a real Postgres, with its pictures, and published;
/// then each export is written as its reader would take it and read back.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExportsOverTheSeedTests(PostgresFixture postgres)
{
    private static readonly string[] Required = ["id", "title", "description", "link", "image_link", "availability", "price"];

    /// <summary>
    /// The feed: a row per variant on sale, each with what Google's product data
    /// specification requires of every product — an id, a title, a description, a
    /// link, a picture, an availability and a price; every variant of a product under
    /// one group; and a Google category for every row, since every seed category has
    /// one up its branch.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Every_row_of_the_seeds_feed_has_what_google_requires_and_its_variants_are_grouped()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var shop = await SeededShopAsync("export_feed_seed", ct);

        ProductFeedFile feed;
        await using (var scope = shop.Provider.CreateAsyncScope())
            feed = await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>().SendAsync(new ProductFeedQuery("es", "https://api.example"), ct);

        var lines = Csv.Read(feed.Content, '\t');
        var header = lines[0].ToList();
        string Cell(IReadOnlyList<string> row, string column) => row[header.IndexOf(column)];
        var rows = lines.Skip(1).ToList();

        int variants;
        await using (var context = shop.Database.Create())
            variants = await context.Products.Where(product => product.Status == ProductStatus.Active).SelectMany(product => product.Variants).CountAsync(ct);

        Assert.Equal(variants, rows.Count);
        var missing = rows.SelectMany(row => Required.Where(column => Cell(row, column).Length == 0).Select(column => $"{Cell(row, "id")}: {column}")).ToList();
        Assert.True(missing.Count == 0, string.Join('\n', missing));
        Assert.All(rows, row => Assert.Matches(@"^\d+\.\d{2} EUR$", Cell(row, "price")));
        Assert.Equal(rows.Count, rows.Select(row => Cell(row, "id")).Distinct().Count());
        Assert.Equal(0, feed.Uncategorised);

        // Each product's variants under one group, which is its code, and no group across two products.
        Assert.All(rows.GroupBy(row => Cell(row, "item_group_id")), group =>
            Assert.Single(group.Select(row => Cell(row, "link")).Distinct()));
    }

    /// <summary>
    /// The catalogue as Shopify imports it, read back against the seed it came from
    /// (E-3; the round trip from the other side, our half): every seed product once,
    /// by title, with its brand as the vendor, its variants by SKU at their prices, and
    /// as many pictures as the seed gives it. Shopify's own importer reading this file
    /// into a clean development store is the author's half: it is a screen in
    /// Shopify's admin, with no API (<c>tools/shopify-seed/README.md</c>).
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_seed_exported_for_shopify_reads_back_as_the_seed()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var shop = await SeededShopAsync("export_shopify_seed", ct);

        ExportFile file;
        await using (var scope = shop.Provider.CreateAsyncScope())
            file = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new ExportCatalogueCommand("shopify-csv", "es", "https://api.example"), ct);

        var lines = Csv.Read(file.Content, ',');
        var header = lines[0].ToList();
        string Cell(IReadOnlyList<string> row, string column) => row[header.IndexOf(column)];
        var products = lines.Skip(1).GroupBy(row => Cell(row, "URL handle")).ToList();

        using var seed = JsonDocument.Parse(await File.ReadAllTextAsync(Seed(Path.Combine("seed", "products.sample.json")), ct));
        var expected = seed.RootElement.EnumerateArray().ToList();
        Assert.Equal(expected.Count, products.Count);

        var byTitle = products.ToDictionary(product => Cell(product.First(), "Title"));
        var differences = new List<string>();
        foreach (var item in expected)
        {
            var title = item.GetProperty("name").GetProperty("es").GetString()!;
            if (!byTitle.TryGetValue(title, out var product))
            {
                differences.Add($"{title}: not in the file");
                continue;
            }

            var first = product.First();
            var brand = item.TryGetProperty("brand", out var b) ? b.GetString() : null;
            if ((brand ?? "") != Cell(first, "Vendor"))
                differences.Add($"{title}: vendor «{Cell(first, "Vendor")}», seed «{brand}»");
            if (Cell(first, "Status") != "active")
                differences.Add($"{title}: status {Cell(first, "Status")}");

            var pictures = item.TryGetProperty("images", out var images) ? images.GetArrayLength() : 0;
            var written = product.Count(row => Cell(row, "Product image URL").Length > 0);
            if (pictures != written)
                differences.Add($"{title}: {written} pictures, seed {pictures}");

            var skus = product.Select(row => Cell(row, "SKU")).Where(sku => sku.Length > 0).ToList();
            if (skus.Count == 0 || skus.Distinct().Count() != skus.Count)
                differences.Add($"{title}: SKUs {string.Join(", ", skus)}");
            if (product.Where(row => Cell(row, "SKU").Length > 0).Any(row => !System.Text.RegularExpressions.Regex.IsMatch(Cell(row, "Price"), @"^\d+\.\d{2}$")))
                differences.Add($"{title}: a price not in euros and cents");
        }

        Assert.True(differences.Count == 0, $"{differences.Count} differences:\n{string.Join('\n', differences)}");
        Assert.Equal(expected.Count, file.Rows);
    }

    /// <summary>The seed imported and published into an empty shop on its own database.</summary>
    private async Task<SeededShop> SeededShopAsync(string database, CancellationToken ct)
    {
        var factory = await postgres.CreateDatabaseAsync(database);
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        var images = Path.Combine(Path.GetTempPath(), $"tendero-export-images-{Guid.NewGuid():N}");
        var provider = Compose(factory.ConnectionString, images);
        await using (var scope = provider.CreateAsyncScope())
        {
            var commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            var imported = await commands.SendAsync(new ImportProductsCommand("seed"), ct);
            await commands.SendAsync(new PublishImportCommand(new ImportRunId(Guid.Parse(imported.ImportId!))), ct);
        }

        return new SeededShop(provider, factory, images);
    }

    private sealed record SeededShop(ServiceProvider Provider, TenderoDbContextFactory Database, string Images) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            if (Directory.Exists(Images))
                Directory.Delete(Images, recursive: true);
        }
    }

    private static ServiceProvider Compose(string connectionString, string images)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Catalog:Connectors:Seed:FilePath"] = Seed(Path.Combine("seed", "products.sample.json")),
                ["Catalog:Attributes:FilePath"] = Seed(Path.Combine("seed", "attributes.sample.json")),
                ["Catalog:Categories:FilePath"] = Seed(Path.Combine("seed", "categories.sample.json")),
                ["Catalog:Images:RootPath"] = images,
                ["Inventory:Warehouses:FilePath"] = Seed(Path.Combine("seed", "warehouses.sample.json")),
                ["Exports:StorefrontUrl"] = "https://shop.example",
                ["Exports:GoogleCategoriesFile"] = Seed(Path.Combine("seed", "google-categories.sample.json")),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(typeof(ImportProductsCommand).Assembly, typeof(ProductFeedQuery).Assembly);
        services.AddTenderoPersistence(connectionString);
        services.AddCatalog(configuration);
        services.AddInventory(configuration);
        services.AddExports(configuration);
        services.AddSingleton<IPrincipalAccessor>(new SystemPrincipalAccessor());
        return services.BuildServiceProvider(validateScopes: true);
    }
}
