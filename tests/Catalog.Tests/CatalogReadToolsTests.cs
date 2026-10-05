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

    /// <summary>A table as the panel reads it: its key, and each column's key and kind. The
    /// panel's headings are translations of these names, so a name changed here alone is a
    /// heading that reads as a key.</summary>
    private static string Shape(ResultTable table) =>
        $"{table.Key}: {string.Join(", ", table.Columns.Select(column => $"{column.Key} {column.Kind}"))}";

    [Fact]
    public async Task Each_tools_tables_are_named_as_the_panel_reads_them()
    {
        var found = await Tools().Single(tool => tool.Name == "find_products")
            .ReadAsync(Arguments("""{"text":"flexo"}"""), TestContext.Current.CancellationToken);
        var gaps = await Tools().Single(tool => tool.Name == "catalogue_gaps")
            .ReadAsync(Arguments("""{"kind":"MissingName","culture":"en"}"""), TestContext.Current.CancellationToken);

        Assert.Equal("foundProducts: code Text, name Text, price Money, status Term, missingCultures Text", Shape(Assert.Single(found.Tables!)));
        Assert.Equal("catalogueGaps: code Text, name Text", Shape(Assert.Single(gaps.Tables!)));

        // And the rows are the products': the code, the name, the price, the status as a term, what is missing.
        var row = Assert.Single(found.Tables![0].Rows);
        Assert.Equal((Lamp.Code, "Flexo articulado", 10m, "Draft", "en"), (row[0].Text, row[1].Text, row[2].Number, row[3].Text, row[4].Text));
        Assert.Equal((Lamp.Code, "Flexo articulado"), (gaps.Tables![0].Rows.Single()[0].Text, gaps.Tables[0].Rows.Single()[1].Text));
    }

    /// <summary>A search that finds nothing has a table with no rows and no currency to name, and does not fail.</summary>
    [Fact]
    public async Task Finding_nothing_is_an_empty_table()
    {
        var answer = await Tools().Single(tool => tool.Name == "find_products")
            .ReadAsync(Arguments("""{"text":"no such thing"}"""), TestContext.Current.CancellationToken);

        var table = Assert.Single(answer.Tables!);
        Assert.Empty(table.Rows);
        Assert.Null(table.Currency);
    }

    /// <summary>With no language asked for the products are named in the shop's own, Spanish.</summary>
    [Fact]
    public async Task With_no_language_asked_for_the_names_are_in_the_shops()
    {
        var found = await Tools().Single(tool => tool.Name == "find_products")
            .ReadAsync(Arguments("""{"text":"mochila"}"""), TestContext.Current.CancellationToken);
        var gaps = await Tools().Single(tool => tool.Name == "catalogue_gaps")
            .ReadAsync(Arguments("""{"kind":"DraftWaiting"}"""), TestContext.Current.CancellationToken);

        Assert.Equal("Mochila", ((FoundProducts)found.Data).Products.Single().Name);
        Assert.Contains("Mochila", ((Gaps)gaps.Data).Items.Select(item => item.Name));
    }

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
        // Its own price, so "the cheapest" has an answer; the table's currency is the rows'.
        Assert.Equal((Lamp.Price.Amount, Lamp.Price.Currency), (found.Price, found.Currency));
        Assert.Equal(Lamp.Price.Currency, Assert.Single(answer.Tables!).Currency);
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
