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
/// The products list's search (the redesign's R-4.6), where only Postgres can
/// answer: a name is jsonb, matched on its values in every culture and never on
/// its keys. The first version cast the whole object to text, and typing "es"
/// found every product in the catalogue — the author caught it reading the SQL.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProductListSearchTests(PostgresFixture postgres)
{
    private static readonly TestClock Clock = new();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Matches_a_name_in_any_culture_or_a_code_and_never_the_json_around_them()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("product_list_search");
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        var backpack = Product.Create(Clock,
            new LocalizedText(new Dictionary<string, string> { ["es"] = "Mochila urbana", ["en"] = "Urban backpack" }),
            new Money(49m, "EUR"));
        var kettle = Product.Create(Clock,
            new LocalizedText(new Dictionary<string, string> { ["es"] = "Hervidor 100% acero", ["en"] = "All-steel kettle" }),
            new Money(39m, "EUR"));
        var lamp = Product.Create(Clock, LocalizedText.From("es", "Flexo articulado"), new Money(74.9m, "EUR"));

        await using (var context = factory.Create())
        {
            context.Products.AddRange(backpack, kettle, lamp);
            await context.SaveChangesAsync(ct);
        }

        var services = new ServiceCollection().AddLogging().AddTenderoPersistence(factory.ConnectionString).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IProductCatalogReader>();

        async Task<string[]> Found(string text) =>
            [.. (await reader.ListAsync(null, text, 1, 20, ct)).Items.Select(product => product.Code).Order()];

        // The keys of {"es": …, "en": …}: a match on them would be every product.
        Assert.Empty(await Found("es"));
        Assert.Empty(await Found("\"en\""));

        Assert.Equal([backpack.Code], await Found("BACKPACK"));
        Assert.Equal([backpack.Code], await Found("mochila"));
        Assert.Equal([lamp.Code], await Found(lamp.Code[..5].ToLowerInvariant()));

        // A % typed is a character: as a wildcard, "0%a" would also find "100% acero"'s neighbours.
        Assert.Equal([kettle.Code], await Found("100%"));
        Assert.Empty(await Found("0%urban"));

        Assert.Equal(3, (await reader.ListAsync(null, "  ", 1, 20, ct)).Total);
    }

    /// <summary>
    /// The catalogue's half of a stock search (R-7.1): a name matched in any
    /// culture answers with every SKU of that product, and never with a product
    /// matched by the JSON's keys.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_name_answers_with_the_skus_of_every_product_it_matches()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("product_skus_by_name");
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        var backpack = Product.Create(Clock,
            new LocalizedText(new Dictionary<string, string> { ["es"] = "Mochila urbana", ["en"] = "Urban backpack" }),
            new Money(49m, "EUR"));
        backpack.DefineAxes(Clock, ["COLOR"]);
        backpack.AddVariant(Clock, "PACK-BLACK", new Money(49m, "EUR"), new Dictionary<string, string> { ["COLOR"] = "BLACK" });
        backpack.AddVariant(Clock, "PACK-GREY", new Money(49m, "EUR"), new Dictionary<string, string> { ["COLOR"] = "GREY" });
        var lamp = Product.Create(Clock, LocalizedText.From("es", "Flexo articulado"), new Money(74.9m, "EUR"));
        lamp.AddVariant(Clock, "LAMP-DEFAULT", new Money(74.9m, "EUR"));

        await using (var context = factory.Create())
        {
            context.Products.AddRange(backpack, lamp);
            await context.SaveChangesAsync(ct);
        }

        var services = new ServiceCollection().AddLogging().AddTenderoPersistence(factory.ConnectionString).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IProductCatalogReader>();

        Assert.Equal(["PACK-BLACK", "PACK-GREY"], (await reader.SkusOfProductsMatchingAsync("backpack", ct)).Order());
        Assert.Equal(["LAMP-DEFAULT"], await reader.SkusOfProductsMatchingAsync("FLEXO", ct));
        Assert.Empty(await reader.SkusOfProductsMatchingAsync("es", ct));
    }

    /// <summary>
    /// The count per status is the text's, before the status narrows it, so a
    /// filter chip says what it would show and keeps its size whichever is chosen.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Counts_each_status_for_the_text_before_narrowing_to_one()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("product_list_counts");
        await using (var context = factory.Create())
            await context.Database.MigrateAsync(ct);

        var onSale = Product.Create(Clock, LocalizedText.From("es", "Mochila urbana"), new Money(49m, "EUR"));
        onSale.Publish(Clock);
        var draft = Product.Create(Clock, LocalizedText.From("es", "Mochila de viaje"), new Money(89m, "EUR"));
        var other = Product.Create(Clock, LocalizedText.From("es", "Flexo articulado"), new Money(74.9m, "EUR"));

        await using (var context = factory.Create())
        {
            context.Products.AddRange(onSale, draft, other);
            await context.SaveChangesAsync(ct);
        }

        var services = new ServiceCollection().AddLogging().AddTenderoPersistence(factory.ConnectionString).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IProductCatalogReader>();

        var page = await reader.ListAsync(ProductStatus.Active, "mochila", 1, 20, ct);

        Assert.Equal(onSale.Code, Assert.Single(page.Items).Code);
        Assert.Equal(1, page.CountByStatus[ProductStatus.Active]);
        Assert.Equal(1, page.CountByStatus[ProductStatus.Draft]);
        Assert.False(page.CountByStatus.ContainsKey(ProductStatus.Archived));
    }
}
