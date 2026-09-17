using System.Text.Json;
using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Catalog.Features.GetProduct;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Catalog.Features.PublishProduct;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests.Ucp;
using ElGuerre.Tendero.Ucp.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The UCP catalogue binding over the **shipped catalogue**, against a real
/// Postgres, validated against the specification's own JSON Schemas.
///
/// Everything else about this binding is proven over a fixture: one invented
/// product with one variant, one colour and one image. That is enough to check
/// a mapping and not enough to trust it. This runs the same code over the
/// hundred products the repository actually ships — products with no
/// description, products with several images, products whose axes have options
/// no variant uses — because the shapes that break a schema are the ones nobody
/// thought to invent.
///
/// It is the answer to "is it conformant?" that a fixture cannot give.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UcpCatalogIntegrationTests(PostgresFixture postgres)
{
    private const string BaseUrl = "https://shop.example";

    private const string SearchResponse = "https://ucp.dev/schemas/shopping/catalog_search.json#/$defs/search_response";
    private const string LookupResponse = "https://ucp.dev/schemas/shopping/catalog_lookup.json#/$defs/lookup_response";
    private const string ProductResponse = "https://ucp.dev/schemas/shopping/catalog_lookup.json#/$defs/get_product_response";
    private const string ProductType = "https://ucp.dev/schemas/shopping/types/product.json";

    private static readonly JsonSerializerOptions Wire = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static JsonElement Json<T>(T value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value, Wire)).RootElement.Clone();

    /// <summary>
    /// Every product in the shipped catalogue, mapped and validated one at a
    /// time so a failure names the product rather than the batch.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Every_product_in_the_catalogue_maps_to_a_conformant_ucp_product()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await ArrangeAsync("ucp_catalogue", ct);
        var codes = await scope.ImportAndPublishAsync(ct);

        Assert.True(codes.Count >= 100, $"The seed should carry a hundred products; it carried {codes.Count}.");

        foreach (var code in codes)
        {
            var detail = await scope.DetailAsync(code, "es", ct);
            Assert.NotNull(detail);

            UcpSchemas.Validate(
                ProductType,
                Json(UcpCatalogMapper.From(detail!, BaseUrl)),
                $"Product {code}");
        }
    }

    /// <summary>
    /// And in English, because the mapper resolves labels and a culture with a
    /// missing translation is exactly where an empty required string appears.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_catalogue_maps_conformantly_in_english_too()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await ArrangeAsync("ucp_catalogue_en", ct);
        var codes = await scope.ImportAndPublishAsync(ct);

        // All of them, not a sample. English is where a missing translation
        // turns a required string empty, and the products that have one are
        // exactly the ones a sample would skip.
        foreach (var code in codes)
        {
            var detail = await scope.DetailAsync(code, "en", ct);
            Assert.NotNull(detail);

            UcpSchemas.Validate(ProductType, Json(UcpCatalogMapper.From(detail!, BaseUrl)), $"Product {code} in English");
        }
    }

    /// <summary>
    /// The three answers the binding returns, each over real products.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_three_answers_validate_over_real_products()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await ArrangeAsync("ucp_answers", ct);
        var codes = await scope.ImportAndPublishAsync(ct);

        var page = new List<ProductDetail>();
        foreach (var code in codes.Take(10))
            page.Add((await scope.DetailAsync(code, "es", ct))!);

        UcpSchemas.Validate(
            SearchResponse,
            Json(UcpCatalogAnswers.Search(page, BaseUrl, limit: 10, offset: 0, total: codes.Count, notices: [])),
            "A search_catalog answer over the shipped catalogue");

        UcpSchemas.Validate(
            LookupResponse,
            Json(UcpCatalogAnswers.Lookup(page, [.. page.Select(p => p.Code), "ZZZZZZZZZZ"], BaseUrl)),
            "A lookup_catalog answer over the shipped catalogue, with one miss");

        UcpSchemas.Validate(
            ProductResponse,
            Json(UcpCatalogAnswers.Product(page[0], BaseUrl)),
            "A get_product answer over the shipped catalogue");
    }

    /// <summary>
    /// The money, over every product, because minor units are the one number an
    /// agent cannot sanity-check: 39 instead of 3900 is a plausible price.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Every_price_is_in_minor_units()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await ArrangeAsync("ucp_money", ct);
        var codes = await scope.ImportAndPublishAsync(ct);

        foreach (var code in codes)
        {
            var detail = (await scope.DetailAsync(code, "es", ct))!;
            var product = UcpCatalogMapper.From(detail, BaseUrl);

            Assert.Equal(
                (long)decimal.Round(detail.PriceFrom * 100m, 0, MidpointRounding.ToEven),
                product.PriceRange.Min.Amount);

            foreach (var (variant, source) in product.Variants.Zip(detail.Variants))
            {
                Assert.Equal(
                    (long)decimal.Round(source.PriceAmount * 100m, 0, MidpointRounding.ToEven),
                    variant.Price.Amount);
            }
        }
    }

    private async Task<TestScope> ArrangeAsync(string database, CancellationToken ct)
    {
        var factory = await postgres.CreateDatabaseAsync(database);

        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        return new TestScope(factory);
    }

    /// <summary>
    /// The real slices, registered by assembly exactly as the API does, so this
    /// does not end up testing wiring that only exists in tests.
    /// </summary>
    private sealed class TestScope : IAsyncDisposable
    {
        private readonly ServiceProvider provider;

        public TestScope(TenderoDbContextFactory factory)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Catalog:Connectors:Seed:FilePath"] =
                        Path.Combine(AppContext.BaseDirectory, "TestData", "products.sample.json"),
                    ["Catalog:Attributes:FilePath"] =
                        Path.Combine(AppContext.BaseDirectory, "TestData", "attributes.sample.json"),
                    ["Catalog:Categories:FilePath"] =
                        Path.Combine(AppContext.BaseDirectory, "TestData", "categories.sample.json")
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTenderoCqrs(typeof(ImportProductsCommand).Assembly);
            services.AddTenderoPersistence(factory.ConnectionString);
            services.AddCatalog(configuration);

            this.provider = services.BuildServiceProvider(validateScopes: true);
        }

        /// <summary>
        /// Import, then PUBLISH, because importing never does (ADR 0012) and the
        /// product page — which the binding reads through — answers only for
        /// Active products. A catalogue of drafts is invisible to an agent for
        /// the same reason it is invisible to a shopper.
        /// </summary>
        public async Task<IReadOnlyList<string>> ImportAndPublishAsync(CancellationToken ct)
        {
            await using (var importing = this.provider.CreateAsyncScope())
            {
                var dispatcher = importing.ServiceProvider.GetRequiredService<ICommandDispatcher>();
                await dispatcher.SendAsync(new ImportProductsCommand("seed"), ct);
            }

            List<(ProductId Id, string Code)> products;

            await using (var reading = this.provider.CreateAsyncScope())
            {
                var context = reading.ServiceProvider.GetRequiredService<TenderoDbContext>();

                products = await context.Set<Catalog.Domain.Product>()
                    .AsNoTracking()
                    .Select(product => new ValueTuple<ProductId, string>(product.Id, product.Code.ToString()))
                    .ToListAsync(ct);
            }

            foreach (var product in products)
            {
                await using var publishing = this.provider.CreateAsyncScope();
                var dispatcher = publishing.ServiceProvider.GetRequiredService<ICommandDispatcher>();

                await dispatcher.SendAsync(new PublishProductCommand(product.Id), ct);
            }

            return [.. products.Select(product => product.Code)];
        }

        public async Task<ProductDetail?> DetailAsync(string code, string culture, CancellationToken ct)
        {
            await using var scope = this.provider.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();

            return await dispatcher.SendAsync(new GetProductQuery(code, culture), ct);
        }

        public ValueTask DisposeAsync() => this.provider.DisposeAsync();
    }
}
