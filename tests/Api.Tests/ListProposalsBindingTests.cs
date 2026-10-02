using System.Net;
using ElGuerre.Tendero.Assist.Features.ListProposals;
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
/// GET /api/steward/proposals (S-4) over HTTP: the filter by its name and never a
/// number, To review when none is named, a turn's ids in the order it sent them,
/// and the reader's language negotiated and declared.
/// </summary>
public sealed class ListProposalsBindingTests
{
    private sealed class RecordingDispatcher : IQueryDispatcher
    {
        public ListProposalsQuery? Asked { get; private set; }

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            Asked = (ListProposalsQuery)(object)query;
            return Task.FromResult((TResult)(object)new ProposalsPage([], 0, 1, 20, []));
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

    private static async Task<(HttpResponseMessage Response, ListProposalsQuery? Asked)> Get(string url, string? language = null)
    {
        var dispatcher = new RecordingDispatcher();
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
    [InlineData("/api/steward/proposals", ProposalFilter.ToReview)]
    [InlineData("/api/steward/proposals?filter=failed", ProposalFilter.Failed)]
    [InlineData("/api/steward/proposals?filter=All", ProposalFilter.All)]
    public async Task The_filter_is_read_by_its_name_and_defaults_to_the_work_waiting(string url, ProposalFilter filter)
    {
        var (response, asked) = await Get(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(filter, asked!.Filter);
    }

    [Fact]
    public async Task A_filter_by_its_number_is_refused()
    {
        var (response, asked) = await Get("/api/steward/proposals?filter=1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(asked);
    }

    [Fact]
    public async Task A_turns_ids_arrive_in_the_order_sent_with_the_readers_language()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        var (response, asked) = await Get($"/api/steward/proposals?ids={second}&ids={first}", language: "es-ES");

        Assert.Equal([second, first], asked!.Ids!.Select(id => id.Value));
        Assert.Equal("es", asked.Culture);
        Assert.Equal("es", response.Content.Headers.ContentLanguage.Single());
    }
}
