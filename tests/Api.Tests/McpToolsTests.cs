using ElGuerre.Tendero.Catalog.Features.GetProduct;
using ElGuerre.Tendero.Catalog.Features.ListCategories;
using ElGuerre.Tendero.Mcp.Tools;
using ElGuerre.Tendero.Pricing.Features.ListOffers;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using ElGuerre.Tendero.SharedKernel;
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
            TenderoTools.ListCategoriesAsync(dispatcher, asked!, TestContext.Current.CancellationToken),
            query => ((ListCategoriesQuery)query).Culture));

        Assert.Equal(expected, await CultureOf(dispatcher =>
            TenderoTools.ListOffersAsync(dispatcher, asked!, TestContext.Current.CancellationToken),
            query => ((ListOffersQuery)query).Culture));

        Assert.Equal(expected, await CultureOf(dispatcher =>
            TenderoTools.SearchProductsAsync(dispatcher, "zapatillas", asked!,
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
                product, "B073WXYZ01", asked!, TestContext.Current.CancellationToken);
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
            dispatcher, query: "cafetera", culture: "en", category: "KITCHEN", sort: "newest",
            page: 3, pageSize: 5, cancellationToken: TestContext.Current.CancellationToken);

        var query = Assert.IsType<SearchProductsQuery>(dispatcher.Query);

        Assert.Equal("cafetera", query.Q);
        Assert.Equal("KITCHEN", query.Category);
        Assert.Equal("newest", query.Sort);
        Assert.Equal(3, query.Page);
        Assert.Equal(5, query.PageSize);
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
            TenderoTools.GetProductAsync(dispatcher, "NOPE", "es", TestContext.Current.CancellationToken));

        Assert.Contains("NOPE", refusal.Message, StringComparison.Ordinal);
    }

    private static async Task<string> CultureOf(
        Func<IQueryDispatcher, Task> call, Func<object, string> read)
    {
        var dispatcher = new RecordingDispatcher();
        await call(dispatcher);
        return read(dispatcher.Query!);
    }
}
