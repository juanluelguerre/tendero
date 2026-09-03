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
/// The reason it is here rather than in the unit tests is the unique index. Fifty
/// bits of randomness make a collision unlikely; only the constraint makes it
/// impossible, and a constraint that was never exercised is a line in a migration
/// file rather than a guarantee. Nothing above this level can tell the two apart.
///
/// It replaces a suite that tested the same page keyed on the SLUG, which needed
/// five tests — one of them for a tie-break between two products claiming the
/// same address (ADR 0026). That test has no successor here, because the case it
/// covered can no longer occur.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProductCodeTests(PostgresFixture postgres)
{
    private static readonly TestClock Clock = new();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_code_resolves_to_the_product_it_names()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await ArrangeAsync("code_lookup", ct);
        var shirt = await scope.SaveAsync(AShirt(), ct);

        var found = await scope.FindAsync(shirt.Code, ct);

        Assert.NotNull(found);
        Assert.Equal(shirt.Id, found.Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_code_nobody_owns_is_nothing()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await ArrangeAsync("code_miss", ct);
        await scope.SaveAsync(AShirt(), ct);

        Assert.Null(await scope.FindAsync(ProductCode.New(), ct));
    }

    /// <summary>
    /// The guarantee, and the only test in the repository that can make it.
    ///
    /// Two products holding one address is what a slug could never rule out —
    /// uniqueness across the values of a jsonb object is not expressible as a
    /// constraint — and it is the whole reason the URL carries a code instead.
    /// Asserting it needs a database, because the rule lives in one.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Two_products_cannot_hold_the_same_address()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await ArrangeAsync("code_unique", ct);

        var first = await scope.SaveAsync(AShirt(), ct);

        // Forced, because it cannot be reached by accident: the generator would
        // have to draw the same fifty bits twice.
        var clash = AShirt();
        scope.ForceCode(clash, first.Code);

        var conflict = await Assert.ThrowsAsync<DbUpdateException>(
            () => scope.SaveAsync(clash, ct));

        Assert.Contains("ux_products_code", conflict.InnerException?.Message ?? string.Empty);
    }

    /// <summary>
    /// Renaming moves the slug and leaves the address alone. The unit tests
    /// assert the aggregate does this; this one asserts it survives a round trip
    /// through the column, which is where a stray regeneration would live.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Renaming_a_product_does_not_change_its_address()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var scope = await ArrangeAsync("code_rename", ct);
        var shirt = await scope.SaveAsync(AShirt(), ct);
        var code = shirt.Code;

        await scope.RenameAsync(shirt.Id, "Camisa de lino de verano", ct);

        var found = await scope.FindAsync(code, ct);

        Assert.NotNull(found);
        Assert.Equal("camisa-de-lino-de-verano", found.Slug.In("es"));
        Assert.Equal(code, found.Code);
    }

    // ---------- fixtures ----------

    private static Product AShirt()
    {
        var product = Product.Create(
            Clock,
            new LocalizedText(new Dictionary<string, string>
            {
                ["es"] = "Camisa de lino",
                ["en"] = "Linen shirt"
            }),
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

        /// <summary>
        /// Reaches past the aggregate on purpose. A code cannot be set from
        /// outside — that is the design — so provoking a collision means writing
        /// the property directly, and doing it here keeps the aggregate honest.
        /// </summary>
        public void ForceCode(Product product, string code) =>
            typeof(Product)
                .GetProperty(nameof(Product.Code))!
                .SetValue(product, code);

        public async Task<Product> SaveAsync(Product product, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TenderoDbContext>();

            context.Products.Add(product);
            await context.SaveChangesAsync(ct);

            return product;
        }

        public async Task RenameAsync(ProductId id, string name, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TenderoDbContext>();

            var product = await context.Products.FirstAsync(p => p.Id == id, ct);
            product.UpdateDetails(
                Clock, LocalizedText.From("es", name),
                description: null, brand: null, category: null);

            await context.SaveChangesAsync(ct);
        }

        public async Task<Product?> FindAsync(string code, CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();
            var products = scope.ServiceProvider.GetRequiredService<IProductCatalogReader>();

            return await products.FindByCodeAsync(code, ct);
        }

        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }
}
