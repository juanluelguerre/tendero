using System.Net;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// **A price is asked for in the unit it is answered in** (ADR 0059). Every
/// amount this API returns is a whole number of the currency's minor units —
/// a price facet's ends too — and the filter still took major units: an agent
/// that read a facet's `3000` and sent it back as `minPrice` was asking for
/// what costs three thousand euros or more, and was answered an empty page
/// with no error. Decided with the author on 2026-10-13: one unit, both ways.
/// </summary>
public sealed class SearchPriceFilterTests
{
    [Fact]
    public async Task The_price_filter_is_in_minor_units_as_every_price_it_filters_is()
    {
        var (status, asked, _) = await Search("minPrice=2750&maxPrice=2990");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((27.50m, 29.90m), (asked!.Filters!.MinPrice, asked.Filters.MaxPrice));
    }

    /// <summary>
    /// A fraction is the old unit arriving: refused by name, never rounded into
    /// a price nobody asked for — `maxPrice=29.9` would otherwise have become
    /// thirty cents.
    /// </summary>
    [Theory]
    [InlineData("maxPrice=29.9", "maxPrice")]
    [InlineData("minPrice=27,5", "minPrice")]
    public async Task A_price_with_a_fraction_is_refused_as_a_problem_that_names_it(string query, string member)
    {
        var (status, asked, body) = await Search(query);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Null(asked);
        Assert.Equal("Invalid", body["code"]!.GetValue<string>());
        Assert.Contains(member, body.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(HttpStatusCode Status, SearchProductsQuery? Asked, JsonNode Body)> Search(string query)
    {
        var dispatcher = new RecordingDispatcher();
        using var root = new TenderoApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IQueryDispatcher>(dispatcher)));
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/search?{query}", TestContext.Current.CancellationToken);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        return (response.StatusCode, dispatcher.Asked, body);
    }

    private sealed class RecordingDispatcher : IQueryDispatcher
    {
        public SearchProductsQuery? Asked { get; private set; }

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            Asked = query as SearchProductsQuery;
            return Task.FromResult((TResult)(object)new SearchResultPage([], 0, 1, 20, 0));
        }
    }
}
