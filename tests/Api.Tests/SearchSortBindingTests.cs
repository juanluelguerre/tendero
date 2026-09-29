using System.Net;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// How <c>?sort=</c> reaches the query (ADR 0043): by the member's name, in any
/// case, and never as a number — the rule the JSON converter already keeps for
/// the same set in a body. Binding is the framework's, so what it accepts is
/// pinned here rather than assumed.
/// </summary>
public sealed class SearchSortBindingTests
{
    private sealed class RecordingDispatcher : IQueryDispatcher
    {
        public SearchProductsQuery? Asked { get; private set; }

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            Asked = query as SearchProductsQuery;
            return Task.FromResult((TResult)(object)new SearchResultPage([], 0, 1, 20, 0));
        }
    }

    [Theory]
    [InlineData("PriceAsc", ProductSort.PriceAsc)]
    [InlineData("priceasc", ProductSort.PriceAsc)]
    [InlineData("NEWEST", ProductSort.Newest)]
    [InlineData(null, ProductSort.Relevance)]
    public async Task An_order_is_read_by_its_name_in_any_case(string? sort, ProductSort expected)
    {
        var (status, asked) = await Search(sort);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(expected, asked?.Sort);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("price-asc")]
    [InlineData("cheapest")]
    public async Task A_number_or_a_name_it_does_not_have_is_refused(string sort)
    {
        var (status, asked) = await Search(sort);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Null(asked);
    }

    /// <summary>The refusal is problem details, titled as the caller's mistake, not a bare status.</summary>
    [Fact]
    public async Task A_refused_order_is_answered_with_problem_details()
    {
        using var root = new TenderoApiFactory();
        using var client = root.CreateClient();

        var response = await client.GetAsync("/api/search?sort=1", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"title\":\"The request is not valid.\"", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The binding's handler answers only a binding's failure: any other exception
    /// goes on to the handler that owns it — here the search engine being down,
    /// which is a 503 and never a swallowed 500.
    /// </summary>
    [Fact]
    public async Task Another_failure_is_left_to_the_handler_that_owns_it()
    {
        using var root = new TenderoApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IQueryDispatcher>(new FailingDispatcher())));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/search", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private sealed class FailingDispatcher : IQueryDispatcher
    {
        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
            throw new SearchUnavailableException("lexical", "the test's engine is down");
    }

    private static async Task<(HttpStatusCode Status, SearchProductsQuery? Asked)> Search(string? sort)
    {
        var dispatcher = new RecordingDispatcher();
        using var root = new TenderoApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IQueryDispatcher>(dispatcher)));
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            sort is null ? "/api/search" : $"/api/search?sort={Uri.EscapeDataString(sort)}",
            TestContext.Current.CancellationToken);

        return (response.StatusCode, dispatcher.Asked);
    }
}
