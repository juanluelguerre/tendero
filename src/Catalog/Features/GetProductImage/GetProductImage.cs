using Carter;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Catalog.Features.GetProductImage;

/// <summary>
/// Sirve una imagen por su clave. El id ES el hash del contenido, así que la
/// respuesta es inmutable por construcción: una clave nunca cambia de bytes y
/// puede cachearse un año sin revalidar. Es lo que hace innecesario invalidar
/// caché al cambiar una foto — cambiar la foto cambia la clave.
///
/// Sin dispatcher: no hay comando ni consulta que despachar, es una lectura de
/// stream. Meter CQRS aquí sería ceremonia sin contenido.
/// </summary>
public sealed class GetProductImageEndpoint : ICarterModule
{
    private const int OneYearInSeconds = 31_536_000;

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/images/{id}",
            async Task<Results<FileStreamHttpResult, NotFound>> (
                   string id, IImageStore store, HttpContext http, CancellationToken ct) =>
            {
                var stored = await store.OpenAsync(new ImageId(id), ct);
                if (stored is null)
                    return TypedResults.NotFound();

                http.Response.Headers.CacheControl = $"public, max-age={OneYearInSeconds}, immutable";
                http.Response.Headers.ETag = $"\"{id}\"";

                return TypedResults.Stream(stored.Content, stored.ContentType);
            })
            .WithTags("Catalog")
            .WithName("GetProductImage");
    }
}
