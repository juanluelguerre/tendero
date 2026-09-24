using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The browser loads the telemetry adapter the API names (ADR 0036), so what the
/// API names is the whole contract: OpenTelemetry when nothing is configured, the
/// configured adapter when it is one of the three, and no telemetry at all for
/// anything else — an unknown name is never a guess at the nearest adapter.
/// </summary>
public sealed class TelemetryConfigurationTests
{
    private sealed record TelemetryConfigurationResponse(string Adapter);

    [Theory]
    [InlineData(null, "otel")]
    [InlineData("otel", "otel")]
    [InlineData("FARO", "faro")]
    [InlineData(" noop ", "noop")]
    [InlineData("datadog", "noop")]
    public async Task The_browser_is_told_which_adapter_to_load(string? configured, string expected)
    {
        using var baseFactory = new TenderoApiFactory();
        using var factory = configured is null
            ? baseFactory
            : baseFactory.WithWebHostBuilder(host => host.UseSetting("Telemetry:Browser:Adapter", configured));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/telemetry/config", TestContext.Current.CancellationToken);

        // No token, and it must answer: a signed-out shopper's page loads telemetry too.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var configuration = await response.Content.ReadFromJsonAsync<TelemetryConfigurationResponse>(
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, configuration?.Adapter);
    }
}
