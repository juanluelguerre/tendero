using System.Text.Json;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.CatalogueGaps;
using ElGuerre.Tendero.Catalog.Features.ListProducts;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>What Steward may read of the catalogue (S-3b): two tools Catalog owns.</summary>
public sealed class CatalogReadToolsTests
{
    private static readonly TestClock Clock = new();

    private static readonly Product Lamp = Product.Create(Clock, LocalizedText.From("es", "Flexo articulado"), new Money(10m, "EUR"));

    private static readonly Product Backpack = Product.Create(Clock,
        new LocalizedText(new Dictionary<string, string> { ["es"] = "Mochila", ["en"] = "Backpack" }), new Money(49m, "EUR"));

    private static IReadOnlyList<IReadTool> Tools()
    {
        var services = new ServiceCollection();
        services.AddCatalog(new ConfigurationBuilder().Build());
        services.AddTenderoCqrs(typeof(ListProductsQuery).Assembly);
        services.AddSingleton<IProductCatalogReader>(new InMemoryProductCatalogReader(Lamp, Backpack));
        return [.. services.BuildServiceProvider().CreateScope().ServiceProvider.GetServices<IReadTool>()];
    }

    private static JsonElement Arguments(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Catalog_registers_its_two_tools()
    {
        Assert.Equal(["catalogue_gaps", "find_products"], Tools().Select(tool => tool.Name).Order());
    }

    [Fact]
    public async Task Find_products_answers_ids_codes_names_and_the_languages_missing()
    {
        var answer = await Tools().Single(tool => tool.Name == "find_products")
            .ReadAsync(Arguments("""{"text":"flexo"}"""), TestContext.Current.CancellationToken);

        var found = Assert.Single(((FoundProducts)answer.Data).Products);
        Assert.Equal((Lamp.Code, "Flexo articulado", "Draft"), (found.Code, found.Name, found.Status));
        Assert.Equal(["en"], found.MissingCultures);
        Assert.Equal((1, (int?)1), (answer.Rows, answer.Total));
    }

    [Fact]
    public async Task Catalogue_gaps_answers_the_product_ids_a_proposal_needs()
    {
        var answer = await Tools().Single(tool => tool.Name == "catalogue_gaps")
            .ReadAsync(Arguments("""{"kind":"MissingName","culture":"en"}"""), TestContext.Current.CancellationToken);

        var gap = Assert.Single(((Gaps)answer.Data).Items);
        Assert.Equal(Lamp.Id.Value.ToString(), gap.ProductId);
    }

    [Fact]
    public async Task A_gap_kind_the_model_made_up_is_an_error_it_can_read()
    {
        var failure = await Assert.ThrowsAsync<ArgumentException>(() => Tools().Single(tool => tool.Name == "catalogue_gaps")
            .ReadAsync(Arguments("""{"kind":"MissingEverything"}"""), TestContext.Current.CancellationToken));

        Assert.Contains("catalogue_gaps", failure.Message);
    }

    /// <summary>Finding nothing in particular is the whole catalogue a page at a time,
    /// which is a gap list's job: the model is told to say what it looks for.</summary>
    [Theory]
    [InlineData("""{"text":""}""")]
    [InlineData("""{"text":"   "}""")]
    public async Task Find_products_without_words_is_an_error_the_model_can_read(string arguments)
    {
        var failure = await Assert.ThrowsAsync<ArgumentException>(() => Tools().Single(tool => tool.Name == "find_products")
            .ReadAsync(Arguments(arguments), TestContext.Current.CancellationToken));

        Assert.Contains("catalogue_gaps", failure.Message);
    }
}
