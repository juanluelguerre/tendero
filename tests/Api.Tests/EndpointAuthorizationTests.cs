using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// No endpoint without a decision about who may call it.
///
/// The rule is not "everything protected": searching and browsing the catalogue
/// are public and must stay that way. The rule is that **being public is a
/// decision written in the code** — an explicit `.AllowAnonymous()` — and not
/// the absence of a line. Before this the API had six endpoints with no
/// authentication, including the one that publishes to the public catalogue, and
/// none of them said so.
///
/// This test is what stops the next slice from forgetting.
/// </summary>
public sealed class EndpointAuthorizationTests
{
    /// <summary>
    /// ASP.NET Core's and the development issuer's own infrastructure is not
    /// bound by the rule: health, and the three OIDC endpoints, which are public
    /// by definition of the protocol.
    /// </summary>
    private static readonly string[] Exempt = ["/health", "/alive", "/openapi/"];

    [Fact]
    public void Every_endpoint_states_who_may_call_it()
    {
        using var factory = new TenderoApiFactory();

        // Force the host to be built before reading the routes.
        _ = factory.CreateClient();

        var endpoints = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => !Exempt.Any(prefix =>
                endpoint.RoutePattern.RawText?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true))
            .ToArray();

        Assert.NotEmpty(endpoints);

        var undecided = endpoints
            .Where(endpoint =>
                endpoint.Metadata.GetMetadata<IAuthorizeData>() is null &&
                endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Select(endpoint => $"  {endpoint.RoutePattern.RawText}")
            .ToArray();

        Assert.True(
            undecided.Length == 0,
            $"""
             These endpoints neither require authorization nor allow anonymous:

             {string.Join(Environment.NewLine, undecided)}

             Add .RequireAuthorization(TenderoPolicyNames.…) or an explicit
             .AllowAnonymous(). Public is a decision, not an omission.
             """);
    }

    /// <summary>
    /// The opposite of the one above: that what writes to the catalogue is NOT
    /// anonymous. An `.AllowAnonymous()` put on publish by mistake would sail
    /// through the test above.
    /// </summary>
    [Theory]
    [InlineData("/api/catalog/import")]
    [InlineData("/api/catalog/products/{id:guid}/publish")]
    [InlineData("/api/search/reindex")]
    // Not a write, and here for the same reason: it reads drafts.
    [InlineData("/api/catalog/products")]
    public void Writes_to_the_catalogue_require_the_shopkeeper(string route)
    {
        using var factory = new TenderoApiFactory();
        _ = factory.CreateClient();

        var endpoint = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == route);

        Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());

        var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(data => data.Policy)
            .ToArray();

        Assert.Contains(SharedKernel.TenderoPolicyNames.Shopkeeper, policies);
    }

    /// <summary>
    /// The catalogue list reads the TABLE, drafts included — it is the review
    /// queue's reader — and it was anonymous under a comment saying "the
    /// catalogue is public". The public catalogue is what is Active, and it is
    /// served by search and by the product page, both of which only ever return
    /// Active. A draft is an imported or AI-proposed product nobody has approved
    /// yet (ADR 0012), and the UI review of 2026-09-11 found any stranger could
    /// list them with <c>?status=draft</c>.
    /// </summary>
    [Fact]
    public async Task A_stranger_cannot_read_the_catalogue_under_review()
    {
        using var factory = new TenderoApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/catalog/products?status=draft", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
