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
    /// The dashboard's reads added with R-9's D-7 are the shopkeeper's: what sells,
    /// through which channel, what runs out and which system is down are commercial
    /// information, and search's status is never the open /health's.
    /// </summary>
    [Theory]
    [InlineData("/api/dashboard/mix")]
    [InlineData("/api/dashboard/stock")]
    [InlineData("/api/dashboard/uplift")]
    [InlineData("/api/dashboard/search-status")]
    public void The_dashboards_new_reads_are_the_shopkeepers(string route) => Writes_to_the_catalogue_require_the_shopkeeper(route);

    /// <summary>
    /// The opposite of the one above: that what writes to the catalogue is NOT
    /// anonymous. An `.AllowAnonymous()` put on publish by mistake would sail
    /// through the test above.
    /// </summary>
    [Theory]
    [InlineData("/api/catalog/import")]
    [InlineData("/api/catalog/products/{id:guid}/publish")]
    [InlineData("/api/catalog/products/{id:guid}/withdraw")]
    [InlineData("/api/catalog/products/{id:guid}/translations/{culture}")]
    // Not a write, and here because it shows drafts, as the list does.
    [InlineData("/api/catalog/products/{code}/review")]
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
    /// Pricing's first writer (ADR 0044): every campaign route of the backoffice
    /// — the calendar's reads included, because they show drafts — and the image
    /// upload are the shopkeeper's. Several methods share a pattern, so each
    /// pattern is checked on every endpoint it has.
    /// </summary>
    [Theory]
    [InlineData("/api/pricing/campaign-calendar", 2)]
    [InlineData("/api/pricing/campaign-calendar/{code}", 2)]
    [InlineData("/api/pricing/campaign-calendar/{code}/schedule", 1)]
    [InlineData("/api/pricing/campaign-calendar/{code}/unschedule", 1)]
    [InlineData("/api/pricing/campaign-calendar/{code}/end", 1)]
    [InlineData("/api/pricing/campaign-images", 1)]
    public void Campaigns_are_managed_by_the_shopkeeper_only(string route, int endpoints)
    {
        using var factory = new TenderoApiFactory();
        _ = factory.CreateClient();

        var matching = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(candidate => candidate.RoutePattern.RawText?.TrimEnd('/') == route)
            .ToArray();

        Assert.Equal(endpoints, matching.Length);
        Assert.All(matching, endpoint =>
        {
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Contains(SharedKernel.TenderoPolicyNames.Shopkeeper,
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy));
        });
    }

    /// <summary>The shop's list of campaigns is its window display: public, as the offers are.</summary>
    [Fact]
    public async Task Anybody_reads_the_campaigns_near_now()
    {
        using var factory = new TenderoApiFactory();
        _ = factory.CreateClient();

        var endpoint = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "/api/pricing/campaigns");

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());

        var response = await factory.CreateClient().PostAsync(
            "/api/pricing/campaign-calendar/TENDERO_DAYS/end", null, TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The home's rows (R-8) are the shop window: what sells, what is rising, what
    /// is like what the shopper viewed, and the cards for all three — public, as
    /// search is. None of them answers a figure or reads anything about the caller.
    /// </summary>
    [Theory]
    [InlineData("/api/ordering/best-sellers")]
    [InlineData("/api/ordering/trending")]
    [InlineData("/api/search/similar")]
    [InlineData("/api/search/cards")]
    public void The_homes_rows_are_public_by_decision(string route)
    {
        using var factory = new TenderoApiFactory();
        _ = factory.CreateClient();

        var endpoint = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == route);

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    /// <summary>
    /// A customer's trail (R-8) is personal data, read and changed only by their
    /// own session: every route is the shopper's — never anonymous, never an
    /// agent's — and the handlers scope it to the caller. Several methods share a
    /// pattern, so each pattern is checked on every endpoint it has.
    /// </summary>
    [Theory]
    [InlineData("/api/accounts/me/history", 2)]
    [InlineData("/api/accounts/me/history/views", 1)]
    [InlineData("/api/accounts/me/history/searches", 1)]
    [InlineData("/api/accounts/me/history/merge", 1)]
    [InlineData("/api/accounts/me/history/{kind}/{key}", 1)]
    [InlineData("/api/accounts/me/history/settings", 1)]
    public void The_trail_is_the_shoppers_own(string route, int endpoints)
    {
        using var factory = new TenderoApiFactory();
        _ = factory.CreateClient();

        var matching = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(candidate => candidate.RoutePattern.RawText?.TrimEnd('/') == route)
            .ToArray();

        Assert.Equal(endpoints, matching.Length);
        Assert.All(matching, endpoint =>
        {
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Contains(SharedKernel.TenderoPolicyNames.Shopper,
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy));
        });
    }

    [Fact]
    public async Task A_stranger_cannot_read_anybodys_trail()
    {
        using var factory = new TenderoApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/accounts/me/history", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
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
