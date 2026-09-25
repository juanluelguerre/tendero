using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Ucp.Catalog;

/// <summary>
/// The catalogue capability's **REST transport binding**: three POSTs whose
/// bodies are the specification's own request schemas and whose answers are its
/// response schemas.
///
/// **Why POST for a search.** It is the specification's choice, not ours, and it
/// is the right one: the request carries filters, pagination, buyer context and
/// agent metadata, which is a document rather than a query string.
///
/// **Why 200 for a lookup that found nothing.** Also the specification's, and
/// worth saying because it goes against the reflex: an identifier nobody owns is
/// a business outcome, so it comes back as a 200 with a `messages` entry naming
/// it. A 404 there would tell the caller their REQUEST was wrong when it was
/// perfectly well formed.
///
/// `get_product` is the exception, and by construction rather than by taste: its
/// response schema requires a `product`, so there is no valid 200 shape for a
/// miss. That one is a 404.
/// </summary>
public static class UcpCatalogRestBinding
{
    /// <summary>
    /// The base the profile advertises. The three paths under it are the
    /// specification's; only this prefix is ours, and it exists so the binding
    /// never collides with the shop's own `/api` surface.
    /// </summary>
    public const string BasePath = "/ucp";

    public static IEndpointConventionBuilder MapUcpCatalogRest(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup(BasePath)
            // Anonymous for the same reason the profile is: the read half of UCP
            // serves a catalogue that is public on the web anyway, and an agent
            // reading it has nothing to prove yet. Phase 11 is where a principal
            // and a mandate arrive.
            .AllowAnonymous()
            // Out of the OpenAPI document, like `/mcp` and `/.well-known/ucp`.
            // These routes already have an authoritative machine-readable
            // description and it is not ours: the UCP profile names them, and
            // their payloads are defined by UCP's published JSON Schemas.
            // Describing them again here would be a second, non-authoritative
            // copy — and `docs/openapi/tendero.json` generates the storefront's
            // TypeScript, which has no use for a UcpProduct. See ADR 0030.
            .WithMetadata(new ExcludeFromDescriptionAttribute());

        group.MapPost("/catalog/search",
            async Task<Results<Ok<UcpCatalogPage>, BadRequest<string>>> (
                UcpSearchRequest request, HttpContext http,
                IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                if (UcpCatalogOperations.Check(request.Meta) is { } refusal)
                    return TypedResults.BadRequest(refusal.Reason);

                return TypedResults.Ok(await UcpCatalogOperations.SearchAsync(
                    dispatcher, request, Culture(http), BaseUrl(http), ct));
            })
            // The same names the MCP binding serves them under: one operation, two doors.
            .AddEndpointFilter(CountedAs("search_catalog"));

        group.MapPost("/catalog/lookup",
            async Task<Results<Ok<UcpCatalogPage>, BadRequest<string>>> (
                UcpLookupRequest request, HttpContext http,
                IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                if (UcpCatalogOperations.Check(request.Meta, request.Ids.Count) is { } refusal)
                    return TypedResults.BadRequest(refusal.Reason);

                return TypedResults.Ok(await UcpCatalogOperations.LookupAsync(
                    dispatcher, request, Culture(http), BaseUrl(http), ct));
            })
            // The same names the MCP binding serves them under: one operation, two doors.
            .AddEndpointFilter(CountedAs("lookup_catalog"));

        group.MapPost("/catalog/product",
            async Task<Results<Ok<UcpProductAnswer>, BadRequest<string>, NotFound<string>>> (
                UcpGetProductRequest request, HttpContext http,
                IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                if (UcpCatalogOperations.Check(request.Meta) is { } refusal)
                    return TypedResults.BadRequest(refusal.Reason);

                if (string.IsNullOrWhiteSpace(request.Id))
                    return TypedResults.BadRequest("`id` is required.");

                var answer = await UcpCatalogOperations.GetProductAsync(
                    dispatcher, request, Culture(http), BaseUrl(http), ct);

                return answer is null
                    ? TypedResults.NotFound($"There is no product with id '{request.Id}'.")
                    : TypedResults.Ok(answer);
            })
            // The same names the MCP binding serves them under: one operation, two doors.
            .AddEndpointFilter(CountedAs("get_product"));

        return group;
    }

    /// <summary>
    /// ADR 0013's chain, and over REST it is the whole chain: there is no tool
    /// parameter here, so the header decides and `es` is the floor. The same
    /// function every endpoint in this API calls.
    /// </summary>
    private static string Culture(HttpContext http) =>
        CultureNegotiation.Resolve(null, http.Request.Headers.AcceptLanguage);

    private static string BaseUrl(HttpContext http) =>
        $"{http.Request.Scheme}://{http.Request.Host}";

    /// <summary>
    /// Counts the call into `tendero.agent.tool_calls` by what came back: a 2xx
    /// answered, a 4xx refused — a missing profile, a missing id, an unknown
    /// product — and anything else, thrown or 5xx, failed. A validation failure
    /// is thrown by the dispatcher and answered as a 400 further out, so it is
    /// counted as the refusal it becomes.
    /// </summary>
    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> CountedAs(string tool) =>
        async (context, next) =>
        {
            try
            {
                var result = await next(context);
                var status = ((result as INestedHttpResult)?.Result ?? result) is IStatusCodeHttpResult { StatusCode: { } code }
                    ? code
                    : StatusCodes.Status200OK;

                AgentToolCalls.Record(Surfaces.Ucp, tool, status switch
                {
                    < 400 => AgentToolCalls.Answered,
                    < 500 => AgentToolCalls.Refused,
                    _ => AgentToolCalls.Failed
                });

                return result;
            }
            catch (FluentValidation.ValidationException)
            {
                AgentToolCalls.Record(Surfaces.Ucp, tool, AgentToolCalls.Refused);
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AgentToolCalls.Record(Surfaces.Ucp, tool, AgentToolCalls.Failed);
                throw;
            }
        };
}
