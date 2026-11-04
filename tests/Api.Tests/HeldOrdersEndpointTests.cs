using System.Net;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The orders a channel took that could not be received (P16-29) are the shop's business:
/// who bought what, and what went wrong with it. Nobody reads them without signing in.
/// </summary>
public sealed class HeldOrdersEndpointTests(TenderoApiFactory factory) : IClassFixture<TenderoApiFactory>
{
    [Fact]
    public async Task The_held_orders_are_not_read_without_signing_in()
    {
        var response = await factory.CreateClient().GetAsync("/api/channels/held-orders", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
