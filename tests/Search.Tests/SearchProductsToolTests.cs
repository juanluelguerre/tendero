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
            new Money(price, "EUR"), new Money(price, "EUR"), new Money(price, "EUR"), null, inStock, 1.0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
        // But it says which products there are: an answer may name these, and no other.
        Assert.Equal([new Subject("PAN-1", "Sartén")], answer.Subjects);
    }

    /// <summary>
    /// Asked for products on offer, the filter is what makes each one true: so each
    /// is on offer now, and a card can say so citing it. Without the fact, "muéstrame
    /// menaje con esos 5 € de descuento" found seven pans and the answer was dropped
    /// whole — every reason cited a product nothing had been said of (2026-10-14).
    /// </summary>
    [Theory]
    [InlineData("es", "Sartén de aluminio está en oferta ahora.")]
    [InlineData("en", "Sartén de aluminio is on offer now.")]
    public async Task Products_found_on_offer_are_each_said_to_be_on_offer(string culture, string said)
    {
        var page = new SearchResultPage([Hit("PAN-1", "Sartén de aluminio"), Hit("PAN-2", "Cazo")], 2, 1, 12, 1);

        var answer = await new SearchProductsTool(new Shop(page)).ReadAsync(
            Arguments($$"""{"text":"menaje","onOffer":true,"culture":"{{culture}}"}"""), TestContext.Current.CancellationToken);

        Assert.Equal(2, answer.Facts!.Count);
        var first = answer.Facts[0];
        Assert.Equal(("PAN-1", FactSource.Campaign, said), (first.Subject, first.Source, first.Text));
        Assert.Equal("PAN-2", answer.Facts[1].Subject);
    }

    /// <summary>
    /// When no product carries the words, the search answers with the nearest, and
    /// the model is told so: "these are like it", not "these are it".
    /// </summary>
    [Fact]
    public async Task Nothing_matching_the_words_is_said_and_the_nearest_are_still_shown()
    {
        var page = new SearchResultPage([Hit("PAN-1", "Sartén")], 9, 1, 12, 1, new SearchAnswer(SearchMatch.Related));

        var answer = await new SearchProductsTool(new Shop(page)).ReadAsync(Arguments("""{"text":"regalo abuela"}"""), Ct);

        var found = (ProductsForShopper)answer.Data;
        Assert.True(found.OnlyNearest);
        Assert.Equal("PAN-1", Assert.Single(found.Products).Code);
    }

    /// <summary>
    /// Asked for kitchenware on offer when none is, the nearest on offer were a
    /// sweatshirt and gloves, and the assistant said the gloves were on offer to a
    /// shopper who asked for kitchenware (2026-10-14). A filter the shopper chose
    /// that nothing of what they asked for passes is an answer: none.
    /// </summary>
    [Fact]
    public async Task Nothing_on_offer_matching_the_words_is_none_on_offer_not_the_nearest_on_offer()
    {
        var page = new SearchResultPage([Hit("GLOVES", "Guantes técnicos táctiles")], 7, 1, 12, 1, new SearchAnswer(SearchMatch.Related));

        var answer = await new SearchProductsTool(new Shop(page)).ReadAsync(
            Arguments("""{"text":"menaje","onOffer":true,"culture":"es"}"""), Ct);

        var found = (ProductsForShopper)answer.Data;
        Assert.True(found.OnlyNearest);
        Assert.Empty(found.Products);
        Assert.Equal((0, (int?)0, 0), (answer.Rows, answer.Total, found.Total));
        Assert.Empty(answer.Subjects!);
        // And it is said, so an answer can rest on it: "no hay menaje en oferta ahora".
        var none = Assert.Single(answer.Facts!);
        Assert.Equal((Fact.TheShop, FactSource.Campaign, "No hay nada en oferta ahora que coincida con «menaje»."),
            (none.Subject, none.Source, none.Text));
    }

    /// <summary>
    /// A product in a promotion is said to be, with the promotion's terms, so a card
    /// can say why it is shown: asked "muéstrame menaje con esos 5 €", Tino wanted to
    /// show a baking dish *because it is in the 5 € promotion* and nothing said so
    /// (2026-10-14).
    /// </summary>
    [Theory]
    [InlineData("es", "Bandeja de horno entra en «5 € en menaje»: 5,00 € menos en el pedido si suma al menos 40,00 €.")]
    [InlineData("en", "Bandeja de horno is in “5 € en menaje”: €5.00 off the order when it comes to at least €40.00.")]
    public async Task A_product_in_a_promotion_is_said_to_be_with_its_terms(string culture, string said)
    {
        var terms = new DealTerms(DealKind.AmountOffOrder, AmountOff: new Money(5, "EUR"), MinimumSubtotal: new Money(40, "EUR"), ReachesSome: true);
        var dish = Hit("DISH", "Bandeja de horno") with { Deals = [new ProductDeal("KITCHEN5", "5 € en menaje", terms)] };

        var answer = await new SearchProductsTool(new Shop(new SearchResultPage([dish, Hit("PAN-1", "Sartén")], 2, 1, 12, 1)))
            .ReadAsync(Arguments($$"""{"text":"menaje","culture":"{{culture}}"}"""), Ct);

        var fact = Assert.Single(answer.Facts!);
        Assert.Equal(("DISH", FactSource.Campaign, said), (fact.Subject, fact.Source, fact.Text));
    }

    /// <summary>
    /// Nothing kitchenware has its own price cut, but all of it is in the 5 € off the
    /// order. Asked for kitchenware on offer, the live Tino answered only *no hay
    /// nada en oferta ahora que coincida con «menaje»*: true, and no help to a
    /// shopper looking at a 5 € promotion (2026-10-14). So code asks again without
    /// the filter and answers with what is in a promotion, each with its terms, and
    /// "see all" leads to that search.
    /// </summary>
    [Fact]
    public async Task Nothing_on_offer_but_products_in_a_promotion_answers_with_those()
    {
        var terms = new DealTerms(DealKind.AmountOffOrder, AmountOff: new Money(5, "EUR"), MinimumSubtotal: new Money(40, "EUR"), ReachesSome: true);
        var pan = Hit("PAN-1", "Sartén") with { Deals = [new ProductDeal("KITCHEN5", "5 € en menaje", terms)] };
        var shop = new ByFilter(
            onOffer: new SearchResultPage([Hit("GLOVES", "Guantes")], 7, 1, 12, 1, new SearchAnswer(SearchMatch.Related)),
            plain: new SearchResultPage([pan, Hit("BOARD", "Tabla")], 28, 1, 12, 1, new SearchAnswer(SearchMatch.Exact)));

        var answer = await new SearchProductsTool(shop).ReadAsync(Arguments("""{"text":"menaje","onOffer":true,"culture":"es"}"""), Ct);

        var found = (ProductsForShopper)answer.Data;
        Assert.Equal(["PAN-1"], found.Products.Select(product => product.Code));
        Assert.False(found.OnlyNearest);
        Assert.False(found.Search.OnOffer);
        var fact = Assert.Single(answer.Facts!);
        Assert.Equal("PAN-1", fact.Subject);
        Assert.StartsWith("Sartén entra en «5 € en menaje»", fact.Text);
    }

    /// <summary>The search, answering by whether "on offer" was asked.</summary>
    private sealed class ByFilter(SearchResultPage onOffer, SearchResultPage plain) : IQueryDispatcher
    {
        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
            Task.FromResult((TResult)(object)(((SearchProductsQuery)(object)query).Filters?.OnOffer == true ? onOffer : plain));
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
