using ElGuerre.Tendero.Catalog.Features.GetProduct;
using ElGuerre.Tendero.Catalog.Features.ListCategories;
using ElGuerre.Tendero.Mcp.Tools;
using ElGuerre.Tendero.Pricing.Features.ListCampaigns;
using ElGuerre.Tendero.Pricing.Features.ListOffers;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features.BuildKit;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// What the tools hand the dispatcher, with no server and no database.
///
/// The methods are static and take <see cref="IQueryDispatcher"/> as a
/// parameter, so a recording stub is the whole fixture. That is worth having
/// separately from the protocol tests: what broke in the first version was not
/// the transport, it was the QUERY the transport built.
/// </summary>
public sealed class McpToolsTests
{
    /// <summary>
    /// A request that carries a header, or no request at all — which is what a
    /// stdio deployment would look like, and what a client that sets no
    /// `Accept-Language` produces.
    /// </summary>
    private static IHttpContextAccessor Asking(string? acceptLanguage)
    {
        if (acceptLanguage is null)
            return new HttpContextAccessor { HttpContext = null };

        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = acceptLanguage;

        return new HttpContextAccessor { HttpContext = context };
    }

    private static readonly IHttpContextAccessor NoHeader = Asking(null);

    private sealed class RecordingDispatcher : IQueryDispatcher
    {
        public object? Query { get; private set; }

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            this.Query = query;
            return Task.FromResult(default(TResult)!);
        }
    }

    /// <summary>
    /// The regression this file exists for. The tools took the caller's string
    /// and handed it straight to the query, so <c>EN-GB</c> reached the handler
    /// as <c>EN-GB</c> and <c>fr</c> reached it as <c>fr</c>, while the same
    /// calls over HTTP arrive as <c>en</c> and <c>es</c> — an endpoint resolves
    /// before it dispatches, and these did not.
    ///
    /// Noticed while probing the running server and pinned down here, which is
    /// the right way round: the server showed the tools were not resolving, and
    /// the mutation shows this theory catches it. Reverting the <c>Resolve</c>
    /// call turns three of these five rows red.
    /// </summary>
    [Theory]
    [InlineData("en", "en")]
    [InlineData("es", "es")]
    [InlineData("EN-GB", "en")]      // normalised, as a header would be
    [InlineData("fr", "es")]         // a language the shop does not speak
    [InlineData(null, "es")]         // nothing asked for
    public async Task Every_tool_resolves_culture_the_way_an_endpoint_does(string? asked, string expected)
    {
        Assert.Equal(expected, await CultureOf(dispatcher =>
            TenderoTools.ListCategoriesAsync(dispatcher, NoHeader, asked, TestContext.Current.CancellationToken),
            query => ((ListCategoriesQuery)query).Culture));

        Assert.Equal(expected, await CultureOf(dispatcher =>
            TenderoTools.ListOffersAsync(dispatcher, NoHeader, asked, TestContext.Current.CancellationToken),
            query => ((ListOffersQuery)query).Culture));

        Assert.Equal(expected, await CultureOf(dispatcher =>
            TenderoTools.ListCampaignsAsync(dispatcher, NoHeader, asked, TestContext.Current.CancellationToken),
            query => ((ListCampaignsQuery)query).Culture));

        Assert.Equal(expected, await CultureOf(dispatcher =>
            TenderoTools.SearchProductsAsync(dispatcher, NoHeader, "zapatillas", asked,
                cancellationToken: TestContext.Current.CancellationToken),
            query => ((SearchProductsQuery)query).Culture));

        // get_product refuses a null answer and the stub answers null, so the
        // refusal is stepped over to read the query that DID reach the
        // dispatcher. Swallowing it and asserting nothing would leave this tool
        // untested for the very thing the theory is about.
        var product = new RecordingDispatcher();
        try
        {
            await TenderoTools.GetProductAsync(
                product, NoHeader, "B073WXYZ01", asked, TestContext.Current.CancellationToken);
        }
        catch (Exception)
        {
            // Expected: the stub has no product to return.
        }

        Assert.Equal(expected, Assert.IsType<GetProductQuery>(product.Query).Culture);
    }

    [Fact]
    public async Task Search_passes_the_browse_and_paging_arguments_through_untouched()
    {
        var dispatcher = new RecordingDispatcher();

        await TenderoTools.SearchProductsAsync(
            dispatcher, NoHeader, query: "cafetera", culture: "en", category: "KITCHEN", sort: ProductSort.Newest,
            page: 3, pageSize: 5, cancellationToken: TestContext.Current.CancellationToken);

        var query = Assert.IsType<SearchProductsQuery>(dispatcher.Query);

        Assert.Equal("cafetera", query.Q);
        Assert.Equal("KITCHEN", query.Category);
        Assert.Equal(ProductSort.Newest, query.Sort);
        Assert.Equal(3, query.Page);
        Assert.Equal(5, query.PageSize);
    }

    /// <summary>The storefront's filters, by the record's names (F-5); none is no filters at all.</summary>
    [Fact]
    public async Task Search_passes_the_filters_through_and_none_is_none()
    {
        var dispatcher = new RecordingDispatcher();

        await TenderoTools.SearchProductsAsync(
            dispatcher, NoHeader, category: "SHOES", brands: ["Vertex"], facets: ["COLOR:BLACK"],
            // In minor units, as every price the tool answers with is (ADR 0059).
            maxPrice: 8000, inStock: true, cancellationToken: TestContext.Current.CancellationToken);
        var filtered = Assert.IsType<SearchProductsQuery>(dispatcher.Query).Filters;

        await TenderoTools.SearchProductsAsync(
            dispatcher, NoHeader, category: "SHOES", cancellationToken: TestContext.Current.CancellationToken);
        var unfiltered = Assert.IsType<SearchProductsQuery>(dispatcher.Query).Filters;

        Assert.NotNull(filtered);
        Assert.Equal(["Vertex"], filtered.Brands);
        Assert.Equal(["COLOR:BLACK"], filtered.Facets);
        Assert.Equal(80m, filtered.MaxPrice);
        Assert.True(filtered.InStock);
        Assert.False(filtered.OnOffer);
        Assert.Null(unfiltered);
    }

    /// <summary>
    /// A code nobody owns is a refusal naming the code, not an empty answer. An
    /// agent handed null guesses; one that is told looks it up again.
    /// </summary>
    [Fact]
    public async Task An_unknown_product_code_is_refused_by_name()
    {
        var dispatcher = new RecordingDispatcher();

        var refusal = await Assert.ThrowsAnyAsync<Exception>(() =>
            TenderoTools.GetProductAsync(dispatcher, NoHeader, "NOPE", "es", TestContext.Current.CancellationToken));

        Assert.Contains("NOPE", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// **One refusal, two transports** (phase 13b, the wire's spec, W-6). What a
    /// tool refuses with is the problem a REST refusal is — its code, the shop's
    /// sentence in the language asked — as the text of the error an agent
    /// reads. It was a bare sentence in English: nothing to branch on, and the
    /// same words whoever asked.
    /// </summary>
    [Theory]
    [InlineData("es", "No hay ningún producto con el código «NOPE».")]
    [InlineData("en", "There is no product with code 'NOPE'.")]
    public async Task A_tools_refusal_is_a_problem_with_a_code_in_the_language_asked(string culture, string detail)
    {
        var refusal = await Assert.ThrowsAnyAsync<Exception>(() =>
            TenderoTools.GetProductAsync(new RecordingDispatcher(), NoHeader, "NOPE", culture, TestContext.Current.CancellationToken));

        var problem = System.Text.Json.Nodes.JsonNode.Parse(refusal.Message)!;
        Assert.Equal(("ProductNotFound", detail), (problem["code"]!.GetValue<string>(), problem["detail"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(problem["title"]!.GetValue<string>()));

        var tooFew = await Assert.ThrowsAnyAsync<Exception>(() =>
            TenderoTools.CompareProductsAsync(new RecordingDispatcher(), NoHeader, ["ONE"], culture, TestContext.Current.CancellationToken));
        Assert.Equal("TooFewToCompare", System.Text.Json.Nodes.JsonNode.Parse(tooFew.Message)!["code"]!.GetValue<string>());
    }

    /// <summary>
    /// The middle link of ADR 0013's chain, which the roadmap assumed this
    /// transport did not have.
    ///
    /// It assumed stdio. Streamable HTTP is HTTP, so the POST that carries the
    /// JSON-RPC carries whatever headers the client set, and the chain here is
    /// not an analogue of the endpoints' — it is the same function called with
    /// the same two arguments.
    /// </summary>
    [Theory]
    [InlineData(null, "en-GB,en;q=0.9", "en")]   // nothing asked, the header decides
    [InlineData(null, "fr-FR", "es")]            // a language the shop does not speak
    [InlineData(null, null, "es")]               // no header at all: stdio, or a quiet client
    [InlineData("es", "en-GB", "es")]            // the parameter wins, which is the whole ADR
    [InlineData("en", "fr", "en")]
    public async Task The_request_header_decides_when_the_tool_was_not_told(
        string? asked, string? header, string expected)
    {
        var dispatcher = new RecordingDispatcher();

        await TenderoTools.ListCategoriesAsync(
            dispatcher, Asking(header), asked, TestContext.Current.CancellationToken);

        Assert.Equal(expected, Assert.IsType<ListCategoriesQuery>(dispatcher.Query).Culture);
    }

    /// <summary>
    /// And the answer says which language it is in, because JSON-RPC has no
    /// `Content-Language` to say it with. The label and the query must agree:
    /// resolving twice is how they would stop agreeing.
    /// </summary>
    [Fact]
    public async Task The_answer_names_the_culture_it_was_answered_in()
    {
        var dispatcher = new RecordingDispatcher();

        var answer = await TenderoTools.ListOffersAsync(
            dispatcher, Asking("en-GB"), culture: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("en", answer.Culture);
        Assert.Equal(
            Assert.IsType<ListOffersQuery>(dispatcher.Query).Culture,
            answer.Culture);
    }

    /// <summary>
    /// <c>list_campaigns</c> is the endpoint's own query, not a second path to the
    /// same data (R-8, H-5): what an agent reads is what the home's card is drawn
    /// from, and the answer names the culture it was answered in.
    /// </summary>
    [Fact]
    public async Task Campaigns_are_the_endpoints_query_answered_in_the_callers_language()
    {
        var dispatcher = new RecordingDispatcher();

        var answer = await TenderoTools.ListCampaignsAsync(
            dispatcher, Asking("en-GB"), culture: null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new ListCampaignsQuery("en"), Assert.IsType<ListCampaignsQuery>(dispatcher.Query));
        Assert.Equal("en", answer.Culture);
    }

    /// <summary>
    /// <c>list_kits</c> (phase 13c, K-7) is the endpoint's own query: an agent
    /// learns which kits there are and what each can be asked to work with, in
    /// the language it asked.
    /// </summary>
    [Fact]
    public async Task Kits_are_the_endpoints_query_answered_in_the_callers_language()
    {
        var dispatcher = new RecordingDispatcher();

        var answer = await TenderoTools.ListKitsAsync(
            dispatcher, Asking("en-GB"), culture: null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new ListKitsQuery("en"), Assert.IsType<ListKitsQuery>(dispatcher.Query));
        Assert.Equal("en", answer.Culture);
    }

    /// <summary>Answers every kit asked for as one the shop does not have.</summary>
    private sealed class NoSuchKit : IQueryDispatcher
    {
        public BuildKitQuery? Asked { get; private set; }

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            Asked = (BuildKitQuery)(object)query;
            return Task.FromResult((TResult)(object)new BuildKitResult(BuildKitOutcome.UnknownTemplate, null));
        }
    }

    /// <summary>
    /// <c>build_kit</c> is the page's query (phase 13c, K-7): the budget in the
    /// currency's minor units, as the wire answers prices and as
    /// <c>search_products</c> takes them, and the requirements as given.
    /// </summary>
    [Fact]
    public async Task A_kit_is_asked_with_its_budget_in_minor_units_and_its_requirements_as_given()
    {
        var dispatcher = new NoSuchKit();

        await Assert.ThrowsAnyAsync<Exception>(() => TenderoTools.BuildKitAsync(
            dispatcher, NoHeader, "KITCHEN_STARTER", 5990, ["INDUCTION:true"], "en", TestContext.Current.CancellationToken));

        var asked = dispatcher.Asked!;
        Assert.Equal(("KITCHEN_STARTER", new Money(59.90m, "EUR"), "en"), (asked.Template, asked.Budget, asked.Culture));
        Assert.Equal(["INDUCTION:true"], asked.Requirements);
    }

    /// <summary>A kit the shop does not have is refused naming it, in the language asked: no model makes one up.</summary>
    [Theory]
    [InlineData("es", "La tienda no tiene ningún kit «GARDEN»; list_kits dice cuáles hay.")]
    [InlineData("en", "The shop has no kit 'GARDEN'; list_kits says which there are.")]
    public async Task A_kit_the_shop_does_not_have_is_refused_by_name(string culture, string detail)
    {
        var refusal = await Assert.ThrowsAnyAsync<Exception>(() => TenderoTools.BuildKitAsync(
            new NoSuchKit(), NoHeader, "GARDEN", 10000, null, culture, TestContext.Current.CancellationToken));

        Assert.Contains("KitNotFound", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(detail, refusal.Message, StringComparison.Ordinal);
    }

    private static async Task<string> CultureOf(
        Func<IQueryDispatcher, Task> call, Func<object, string> read)
    {
        var dispatcher = new RecordingDispatcher();
        await call(dispatcher);
        return read(dispatcher.Query!);
    }

    private enum Standing { Active, Suspended }

    private sealed record Row(Standing Status);

    /// <summary>
    /// A tool answers a closed set as the endpoint does, by its C# name, and never
    /// as a number (ADR 0043). The tools return the API's own records, and the SDK
    /// serialises them with its options rather than ASP.NET's, so the server is
    /// handed the API's rule instead of relying on the SDK's default.
    /// </summary>
    [Fact]
    public void A_tool_spells_an_enum_as_the_api_does()
    {
        var answer = System.Text.Json.JsonSerializer.SerializeToNode(new Row(Standing.Suspended), ElGuerre.Tendero.Mcp.TenderoMcpServer.ToolAnswers);

        Assert.Equal("Suspended", answer?["status"]?.GetValue<string>());
    }
}
