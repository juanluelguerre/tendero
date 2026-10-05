using System.Text.Json;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.GetProduct;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>What the storefront's assistant may read of one product (phase 12b).</summary>
public sealed class GetProductToolTests
{
    /// <summary>The dispatcher, answering with a product — or with none — and keeping the query.</summary>
    private sealed class Shop(ProductDetail? product) : IQueryDispatcher
    {
        public GetProductQuery? Asked { get; private set; }

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            Asked = (GetProductQuery)(object)query;
            return Task.FromResult((TResult)(object?)product!);
        }
    }

    private static ProductDetail Pan(string? description) => new(
        "id-1", "PAN-IRON-26", "Sartén de hierro fundido de 26 cm", "sarten-de-hierro-fundido", description, "Vertex",
        [new CategoryStep("KITCHEN", "Cocina"), new CategoryStep("PANS", "Sartenes")],
        [], [], [], [], 42m, 42m, "EUR", [], [], ProductStatus.Active, DateTimeOffset.UnixEpoch);

    private static JsonElement Arguments(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task A_products_page_is_its_name_brand_leaf_category_and_its_description_sentence_by_sentence()
    {
        var shop = new Shop(Pan("Sartén precurada, apta para inducción, horno y fuego directo. Gana antiadherencia con el uso."));
        var tool = new GetProductTool(shop);

        var answer = await tool.ReadAsync(Arguments("""{"code":"PAN-IRON-26","culture":"es"}"""), TestContext.Current.CancellationToken);

        Assert.Equal((ToolAudience.Shopper, "get_product"), (tool.Audience, tool.Name));
        Assert.Equal(new GetProductQuery("PAN-IRON-26", "es"), shop.Asked);
        var page = Assert.IsType<ProductPageForShopper>(answer.Data);
        Assert.Equal(("PAN-IRON-26", "Sartén de hierro fundido de 26 cm", "Vertex", "Sartenes"), (page.Code, page.Name, page.Brand, page.Category));
        Assert.Equal(2, page.Description.Count);
        Assert.Equal((1, (int?)null), (answer.Rows, answer.Total));
    }

    /// <summary>Each sentence is citable on its own, as the description's and about this product.</summary>
    [Fact]
    public async Task Each_sentence_of_the_description_is_a_fact_of_that_product_from_its_description()
    {
        var answer = await new GetProductTool(new Shop(Pan("Apta para inducción y horno. Gana antiadherencia con el uso.")))
            .ReadAsync(Arguments("""{"code":"PAN-IRON-26"}"""), TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new Fact("PAN-IRON-26", FactSource.Description, "Apta para inducción y horno."),
                new Fact("PAN-IRON-26", FactSource.Description, "Gana antiadherencia con el uso."),
            ],
            answer.Facts);
    }

    [Fact]
    public async Task A_product_with_no_description_has_a_page_and_no_facts()
    {
        var answer = await new GetProductTool(new Shop(Pan(null)))
            .ReadAsync(Arguments("""{"code":"PAN-IRON-26"}"""), TestContext.Current.CancellationToken);

        Assert.Empty(((ProductPageForShopper)answer.Data).Description);
        Assert.Empty(answer.Facts!);
    }

    [Fact]
    public async Task A_long_description_is_its_first_sentences()
    {
        var description = string.Join(' ', Enumerable.Range(1, 20).Select(index => $"Frase {index}."));

        var answer = await new GetProductTool(new Shop(Pan(description)))
            .ReadAsync(Arguments("""{"code":"PAN-IRON-26"}"""), TestContext.Current.CancellationToken);

        Assert.Equal(GetProductTool.MaxSentences, answer.Facts!.Count);
        Assert.Equal("Frase 12.", answer.Facts[^1].Text);
    }

    /// <summary>A draft is not found by the query's own rule, and the model is told only that — and what to do.</summary>
    [Fact]
    public async Task A_code_no_product_on_sale_has_is_the_models_mistake_naming_the_code()
    {
        var failure = await Assert.ThrowsAsync<ArgumentException>(() =>
            new GetProductTool(new Shop(null)).ReadAsync(Arguments("""{"code":"NOPE-1"}"""), TestContext.Current.CancellationToken));

        Assert.Contains("NOPE-1", failure.Message);
        Assert.Contains("search_products", failure.Message);
    }

    [Fact]
    public async Task No_code_is_the_models_mistake_and_the_shop_is_not_asked()
    {
        var shop = new Shop(Pan("x"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new GetProductTool(shop).ReadAsync(Arguments("""{"code":" "}"""), TestContext.Current.CancellationToken));

        Assert.Null(shop.Asked);
    }
}
