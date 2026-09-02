using Carter;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Catalog.Features.GetProductImage;

/// <summary>
/// Serves an image by its key. The id IS the content hash, so the response is
/// immutable by construction: a key never changes bytes and can be cached for a
/// year without revalidating. That is what makes cache invalidation unnecessary
/// when a photo changes — changing the photo changes the key.
///
/// No dispatcher: there is no command or query to dispatch, it is a stream read.
/// Putting CQRS here would be ceremony with no content.
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
            .AllowAnonymous()   // catalogue images are public too
            .WithTags("Catalog")
            .WithName("GetProductImage");
    }
}
