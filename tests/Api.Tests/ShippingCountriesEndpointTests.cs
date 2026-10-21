using System.Net;
using System.Net.Http.Json;
using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Ordering.Adapters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The countries an address form offers are the ones checkout quotes for, read
/// from the provider checkout is configured with — never a second list in the
/// browser that a new zone would leave behind.
/// </summary>
public sealed class ShippingCountriesEndpointTests
{
    private sealed record ShippingCountriesResponse(IReadOnlyList<string>? Countries);

    /// <summary>
    /// The flat rate reaches every country, and says so with no list: the form
    /// then offers every country there is, which only the browser can name.
    /// </summary>
    [Fact]
    public async Task A_shop_that_delivers_everywhere_names_no_list()
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/shipping/countries", TestContext.Current.CancellationToken);

        // No token: a guest fills in an address at checkout.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ShippingCountriesResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Null(body.Countries);
    }

    [Fact]
    public async Task A_shop_that_ships_by_zone_names_the_countries_its_zones_reach()
    {
        using var factory = new TenderoApiFactory();
        using var zoned = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<CheckoutOptions>(options => options.ShippingProvider = ZoneRateShipping.Key)));
        using var client = zoned.CreateClient();

        var body = await client.GetFromJsonAsync<ShippingCountriesResponse>(
            "/api/shipping/countries", TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(["BE", "DE", "ES", "FR", "IT", "NL", "PT"], body.Countries);
    }
}
