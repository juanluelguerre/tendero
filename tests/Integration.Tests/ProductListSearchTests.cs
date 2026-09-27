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
}
