using Carter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace ElGuerre.Tendero.Api;

/// <summary>
/// Which browser telemetry adapter the frontends load (ADR 0036).
///
/// Chosen at run time rather than at build time, for the reason
/// <see cref="AuthConfigurationEndpoint"/> gives about the issuer: a build-time
/// value lives in two apps and is wrong whenever one of them is rebuilt without
/// the other. The browser asks once, after the page is up, and loads only the
/// adapter it is told to.
///
/// It says which adapter and nothing about where it sends: every adapter posts
/// to the page's own origin (`/otlp`), and the dev server's proxy decides where
/// that goes. A browser that knew the collector's address would need CORS, would
/// be blocked by ad blockers, and would have to be told again when the
/// observability mode changes.
///
/// It lives in `src/Api` and not in a slice because it describes THIS PROCESS,
/// like `/api/auth/config`.
/// </summary>
public sealed record TelemetryConfiguration(
    /// <summary>`otel`, `faro` or `noop`. Anything the configuration says that is
    /// not one of those is `noop`: an unknown adapter is no telemetry, never a
    /// guess at the nearest one.</summary>
    string Adapter);

public sealed class TelemetryConfigurationEndpoint : ICarterModule
{
    private static readonly string[] Adapters = ["otel", "faro", "noop"];

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/telemetry/config",
            Ok<TelemetryConfiguration> (IConfiguration configuration) =>
                TypedResults.Ok(new TelemetryConfiguration(Adapter(configuration["Telemetry:Browser:Adapter"]))))
            // Anonymous by an explicit decision: a signed-out shopper's page is
            // exactly what it has to describe, and the name of a telemetry SDK is
            // nothing a token would protect.
            .AllowAnonymous()
            .WithTags("Telemetry")
            .WithName("TelemetryConfiguration");
    }

    /// <summary>OpenTelemetry when nothing is configured; `noop` for anything unrecognised.</summary>
    internal static string Adapter(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return "otel";

        var wanted = configured.Trim().ToLowerInvariant();
        return Adapters.Contains(wanted) ? wanted : "noop";
    }
}
