using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The product page's lookup, against a real Postgres.
///
/// It is here and not in the unit tests because the query cannot exist in LINQ:
/// the slug is jsonb behind a value converter, EF sees a
/// <see cref="LocalizedText"/> and Postgres sees an object, so the adapter writes
/// SQL by hand. Hand-written SQL is the one place in this repository where the
/// snake_case convention is not applied for you, and getting a name wrong
/// compiles, passes every unit test and fails only against a database.
///
/// That is not a hypothetical: the first version of the query named
/// <c>catalog."Products"</c> and <c>"Slug"</c>, in the PascalCase the model
/// declares, and there is no test below this one that could have found it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProductBySlugTests(PostgresFixture postgres)
{
    private static readonly TestClock Clock = new();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_slug_resolves_in_the_culture_it_was_asked_for()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await ArrangeAsync("slug_lookup", ct);
        await scope.SaveAsync(AShirt(), ct);

        var spanish = await scope.FindAsync("camisa-de-lino", "es", ct);
        var english = await scope.FindAsync("linen-shirt", "en", ct);

        Assert.NotNull(spanish);
        Assert.NotNull(english);
        Assert.Equal(spanish.Id, english.Id);
    }

    /// <summary>
    /// The redirect path: an English visitor following a Spanish link still
    /// arrives. This is the half of the query that is NOT indexable, and the
    /// reason it exists rather than answering 404.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_slug_from_another_culture_still_finds_the_product()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await ArrangeAsync("slug_cross_culture", ct);
        var shirt = await scope.SaveAsync(AShirt(), ct);

        var found = await scope.FindAsync("camisa-de-lino", "en", ct);

        Assert.NotNull(found);
        Assert.Equal(shirt.Id, found.Id);
    }

    /// <summary>
    /// The tie-break, and the reason the ORDER BY is there at all.
    ///
    /// Two products can slugify alike — nothing in the schema forbids it, because
    /// uniqueness across a jsonb object's values is not expressible as a
    /// constraint. When they do, the one that owns the slug in the culture being
    /// asked for is the one that URL means. Without the ORDER BY this returns
    /// whichever row Postgres reached first, which is a page that changes
    /// identity between two requests.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_requested_culture_wins_when_two_products_slugify_alike()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await ArrangeAsync("slug_collision", ct);

        // Two different products whose slugs collide across cultures: the English
        // name of one is the Spanish name of the other.
        var spanishOwner = await scope.SaveAsync(
            AProduct(es: "Aroma", en: "Scent"), ct);
        var englishOwner = await scope.SaveAsync(
            AProduct(es: "Perfume", en: "Aroma"), ct);

        Assert.Equal(spanishOwner.Id, (await scope.FindAsync("aroma", "es", ct))!.Id);
        Assert.Equal(englishOwner.Id, (await scope.FindAsync("aroma", "en", ct))!.Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_slug_nobody_owns_is_nothing()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await ArrangeAsync("slug_miss", ct);
        await scope.SaveAsync(AShirt(), ct);

        Assert.Null(await scope.FindAsync("no-existe", "es", ct));
    }

    /// <summary>
    /// The variants come back with it. They are a separate table with
    /// <c>AutoInclude</c>, and <c>FromSql</c> is a different query pipeline from
    /// LINQ — so that the auto-include still applies is worth one assertion
    /// rather than an assumption.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_product_arrives_with_its_variants()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await ArrangeAsync("slug_variants", ct);
        await scope.SaveAsync(AShirt(), ct);

        var found = await scope.FindAsync("camisa-de-lino", "es", ct);

        Assert.Equal(2, found!.Variants.Count);
        Assert.Contains(found.Variants, variant => variant.Sku == "SHIRT-NAVY-38");
        Assert.Equal(["COLOR", "SIZE"], found.VariantAxes);
    }

    // ---------- fixtures ----------

    private static Product AShirt()
    {
        var product = AProduct(es: "Camisa de lino", en: "Linen shirt");

        product.DefineAxes(Clock, ["COLOR", "SIZE"]);
        product.AddVariant(Clock, "SHIRT-NAVY-38", new Money(29.90m, "EUR"),
            new Dictionary<string, string> { ["COLOR"] = "NAVY_BLUE", ["SIZE"] = "38" });
        product.AddVariant(Clock, "SHIRT-NAVY-40", new Money(34.90m, "EUR"),
            new Dictionary<string, string> { ["COLOR"] = "NAVY_BLUE", ["SIZE"] = "40" });

        return product;
    }

    private static Product AProduct(string es, string en)
    {
        var product = Product.Create(
            Clock,
            new LocalizedText(new Dictionary<string, string> { ["es"] = es, ["en"] = en }),
            new Money(29.90m, "EUR"));

        product.Publish(Clock);
        return product;
    }

    private async Task<TestScope> ArrangeAsync(string database, CancellationToken ct)
    {
        var factory = await postgres.CreateDatabaseAsync(database);

        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        return new TestScope(factory);
    }

    private sealed class TestScope : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        public TestScope(TenderoDbContextFactory factory)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTenderoPersistence(factory.ConnectionString);

            _provider = services.BuildServiceProvider(validateScopes: true);
        }

        public async Task<Product> SaveAsync(Product product, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TenderoDbContext>();

            context.Products.Add(product);
            await context.SaveChangesAsync(ct);

            return product;
        }

        public async Task<Product?> FindAsync(string slug, string culture, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();
            var products = scope.ServiceProvider.GetRequiredService<IProductCatalogReader>();

            return await products.FindBySlugAsync(slug, culture, ct);
        }

        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }
}
