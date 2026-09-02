using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// Ningún endpoint sin decidir quién puede llamarlo.
///
/// La regla no es "todo protegido": buscar y ver el catálogo son públicos y
/// deben seguir siéndolo. La regla es que **ser público sea una decisión escrita
/// en el código** — un `.AllowAnonymous()` explícito — y no la ausencia de una
/// línea. Antes de esto la API tenía seis endpoints sin autenticación, incluido
/// el que publica al catálogo público, y ninguno lo decía.
///
/// Este test es lo que impide que el siguiente slice se olvide.
/// </summary>
public sealed class EndpointAuthorizationTests
{
    /// <summary>
    /// La infraestructura de ASP.NET Core y del emisor de desarrollo no está
    /// sujeta a la regla: sanidad, y los tres endpoints OIDC, que son públicos
    /// por definición del protocolo.
    /// </summary>
    private static readonly string[] Exempt = ["/health", "/alive", "/openapi/"];

    [Fact]
    public void Every_endpoint_states_who_may_call_it()
    {
        using var factory = new TenderoApiFactory();

        // Forzar la construcción del host antes de leer las rutas.
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
    /// Lo contrario del anterior: que lo que escribe en el catálogo NO sea
    /// anónimo. Un `.AllowAnonymous()` puesto por error en publish pasaría el
    /// test de arriba tan campante.
    /// </summary>
    [Theory]
    [InlineData("/api/catalog/import")]
    [InlineData("/api/catalog/products/{id:guid}/publish")]
    [InlineData("/api/search/reindex")]
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
}
