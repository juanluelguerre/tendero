using System.Net;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The open <c>/health</c> answers for the process, and a private check — search's
/// stages (R-9, D-7) — answers only through the shopkeeper's route: a stage
/// switched off or down is a search that degrades to words, not an API that is
/// unhealthy, and what it says is not for anonymous callers.
/// </summary>
public sealed class PrivateHealthChecksTests
{
    [Fact]
    public async Task A_private_check_that_fails_does_not_make_the_open_health_endpoint_unhealthy()
    {
        using var root = new TenderoApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHealthChecks().AddCheck("failing-private", () => HealthCheckResult.Unhealthy("down"), [HealthTags.Private])));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.OK, "Healthy"), (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
    }
}
