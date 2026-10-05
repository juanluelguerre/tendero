using System.Text.Json;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>What the storefront's assistant may read of the shop's search (phase 12b).</summary>
public sealed class SearchProductsToolTests
{
    /// <summary>The dispatcher, answering with a page and keeping the query it was sent.</summary>
    private sealed class Shop(SearchResultPage page) : IQueryDispatcher
    {
        public SearchProductsQuery? Asked { get; private set; }

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            Asked = (SearchProductsQuery)(object)query;
            return Task.FromResult((TResult)(object)page);
        }
    }

    private static SearchHit Hit(string code, string name, decimal price = 29.90m, bool inStock = true) =>
        new("id-" + code, code, name, name.ToLowerInvariant().Replace(' ', '-'), "Vertex", "Sartenes", "v-" + code, "SKU-" + code,
            price, price, price, "EUR", null, inStock, 1.0);

    private static JsonElement Arguments(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task It_is_a_shoppers_read_under_the_name_the_mcp_server_gives_the_same_search()
    {
        var tool = new SearchProductsTool(new Shop(new SearchResultPage([], 0, 1, 12, 1)));

        Assert.Equal((ToolAudience.Shopper, "search_products"), (tool.Audience, tool.Name));
    }

    [Fact]
    public async Task What_the_shopper_wants_is_the_shops_own_search_first_page_by_relevance_with_its_filters()
    {
        var shop = new Shop(new SearchResultPage([Hit("PAN-1", "Sartén de aluminio")], 14, 1, 12, 3));

        var answer = await new SearchProductsTool(shop).ReadAsync(
            Arguments("""{"text":"sartén inducción","culture":"es","category":"KITCHEN","brand":"Vertex","maxPrice":40,"inStock":true}"""),
            TestContext.Current.CancellationToken);

        var asked = shop.Asked!;
        Assert.Equal(("sartén inducción", "es", 1, SearchProductsTool.MaxProducts, "KITCHEN", ProductSort.Relevance),
            (asked.Q, asked.Culture, asked.Page, asked.PageSize, asked.Category, asked.Sort));
        Assert.Equal(["Vertex"], asked.Filters!.Brands);
        Assert.Equal(((decimal?)null, (decimal?)40m, false, true),
            (asked.Filters.MinPrice, asked.Filters.MaxPrice, asked.Filters.OnOffer, asked.Filters.InStock));

        var found = Assert.IsType<ProductsForShopper>(answer.Data);
        Assert.Equal(new ProductForShopper("PAN-1", "Sartén de aluminio", "Vertex", "Sartenes", 29.90m, "EUR", true), Assert.Single(found.Products));
        Assert.Equal((1, (int?)14, 14), (answer.Rows, answer.Total, found.Total));
        // The search as run travels with the answer: it is where "see all 14" leads.
        Assert.Equal(new SearchAsRun("sartén inducción", "KITCHEN", "Vertex", null, 40m, false, true), found.Search);
    }

    /// <summary>What it finds is which products there are; what is true of each is another read's.</summary>
    [Fact]
    public async Task A_search_hands_an_answer_nothing_to_cite()
    {
        var answer = await new SearchProductsTool(new Shop(new SearchResultPage([Hit("PAN-1", "Sartén")], 1, 1, 12, 1)))
            .ReadAsync(Arguments("""{"text":"sartén"}"""), TestContext.Current.CancellationToken);

        Assert.Empty(answer.Facts!);
    }

    [Fact]
    public async Task No_language_is_the_shops_default_and_no_filter_is_none()
    {
        var shop = new Shop(new SearchResultPage([], 0, 1, 12, 1));

        await new SearchProductsTool(shop).ReadAsync(Arguments("""{"text":"sartén"}"""), TestContext.Current.CancellationToken);

        Assert.Equal((CultureNegotiation.Default, (string?)null), (shop.Asked!.Culture, shop.Asked.Category));
        Assert.Empty(shop.Asked.Filters!.Brands);
        Assert.False(shop.Asked.Filters.OnOffer || shop.Asked.Filters.InStock);
    }

    [Fact]
    public async Task No_more_products_than_the_tools_limit_whatever_the_search_returns()
    {
        var page = new SearchResultPage([.. Enumerable.Range(1, 20).Select(index => Hit($"PAN-{index}", $"Sartén {index}"))], 20, 1, 12, 1);

        var answer = await new SearchProductsTool(new Shop(page)).ReadAsync(Arguments("""{"text":"sartén"}"""), TestContext.Current.CancellationToken);

        Assert.Equal((SearchProductsTool.MaxProducts, (int?)20), (answer.Rows, answer.Total));
        Assert.Equal("PAN-12", ((ProductsForShopper)answer.Data).Products[^1].Code);
    }

    [Theory]
    [InlineData("""{"text":""}""")]
    [InlineData("""{"text":"   "}""")]
    public async Task Nothing_to_look_for_is_the_models_mistake_and_the_shop_is_not_asked(string arguments)
    {
        var shop = new Shop(new SearchResultPage([], 0, 1, 12, 1));

        var failure = await Assert.ThrowsAsync<ArgumentException>(() =>
            new SearchProductsTool(shop).ReadAsync(Arguments(arguments), TestContext.Current.CancellationToken));

        Assert.Contains("search_products", failure.Message);
        Assert.Null(shop.Asked);
    }
}
