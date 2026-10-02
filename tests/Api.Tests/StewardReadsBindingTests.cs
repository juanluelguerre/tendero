using System.Net;
using ElGuerre.Tendero.Catalog.Features.CatalogueGaps;
using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Inventory.Features.StockHealth;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The reads Steward wraps (S-3a), over HTTP: a gap kind that is missing or is not
/// one is a 400, the reader's language is negotiated and declared, and a warehouse
/// the shop does not have is a 400 naming the ones it has.
/// </summary>
public sealed class StewardReadsBindingTests
{
    private sealed class RecordingDispatcher : IQueryDispatcher
    {
        public object? Asked { get; private set; }

        public RestockList Restock { get; init; } = new(null, false, ["BCN", "MAD"], Restocking.At, 0, []);

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            Asked = query;
            object answer = query switch
            {
                CatalogueGapsQuery gaps => new CatalogueGaps(gaps.Kind, gaps.Culture, 0, []),
                GetRestockListQuery => Restock,
                _ => throw new NotSupportedException(query.GetType().Name),
            };
            return Task.FromResult((TResult)answer);
        }
    }

    private sealed class LetEveryoneThrough : IPolicyEvaluator
    {
        public Task<AuthenticateResult> AuthenticateAsync(AuthorizationPolicy policy, HttpContext context) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task<PolicyAuthorizationResult> AuthorizeAsync(
            AuthorizationPolicy policy, AuthenticateResult authenticationResult, HttpContext context, object? resource) =>
            Task.FromResult(PolicyAuthorizationResult.Success());
    }

    private static async Task<(HttpResponseMessage Response, object? Asked)> Get(string url, RecordingDispatcher? dispatcher = null, string? language = null)
    {
        dispatcher ??= new RecordingDispatcher();
        using var root = new TenderoApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IQueryDispatcher>(dispatcher);
            services.AddSingleton<IPolicyEvaluator, LetEveryoneThrough>();
        }));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (language is not null)
            request.Headers.AcceptLanguage.ParseAdd(language);

        return (await client.SendAsync(request, TestContext.Current.CancellationToken), dispatcher.Asked);
    }

    [Theory]
    [InlineData("/api/catalog/gaps")]
    [InlineData("/api/catalog/gaps?kind=Everything")]
    [InlineData("/api/catalog/gaps?kind=1")]
    public async Task A_gap_kind_that_is_missing_or_is_not_one_is_refused(string url)
    {
        var (response, asked) = await Get(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(asked);
    }

    [Fact]
    public async Task Gaps_are_named_in_the_readers_language_and_say_so()
    {
        var (response, asked) = await Get("/api/catalog/gaps?kind=missingimage", language: "en-GB");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("en", ((CatalogueGapsQuery)asked!).DisplayCulture);
        Assert.Equal("en", response.Content.Headers.ContentLanguage.Single());
    }

    [Fact]
    public async Task A_warehouse_the_shop_does_not_have_is_refused_naming_the_ones_it_has()
    {
        var dispatcher = new RecordingDispatcher { Restock = new RestockList("Madrid", true, ["BCN", "MAD"], Restocking.At, 0, []) };

        var (response, _) = await Get("/api/dashboard/restock?warehouse=Madrid", dispatcher);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("MAD", body);
        Assert.Contains("BCN", body);
    }
}
