using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Ucp.Negotiation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Ucp.Checkout;

/// <summary>
/// Checkout's **REST binding** (P11-1): the specification's five paths under the
/// `/ucp` prefix the profile already advertises. Unlike the catalogue it is not
/// anonymous — a checkout belongs to the agent that opened it, and only an agent
/// opens one (ADR 0022: an agent is a principal, not a customer).
/// </summary>
public static class UcpCheckoutRestBinding
{
    public static RouteGroupBuilder MapUcpCheckoutRest(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup($"{Catalog.UcpCatalogRestBinding.BasePath}/checkout-sessions")
            .RequireAuthorization(TenderoPolicyNames.Agent)
            // Out of the OpenAPI document, like every route whose protocol
            // publishes its own description (ADR 0030).
            .WithMetadata(new ExcludeFromDescriptionAttribute());

        group.MapPost("/", (UcpCheckoutRequest request, HttpContext http, ICommandDispatcher commands, CancellationToken ct) =>
                WithNegotiation(http, ct, (active, links, culture) =>
                    UcpCheckoutOperations.CreateAsync(commands, request, culture(request.Context?.Language), active, links, ct)))
            .AddEndpointFilter(UcpToolCounting.CountedAs("create_checkout"));

        group.MapGet("/{id:guid}", (Guid id, HttpContext http, IQueryDispatcher queries, CancellationToken ct) =>
                WithNegotiation(http, ct, (active, links, culture) =>
                    UcpCheckoutOperations.GetAsync(queries, id, culture(null), active, links, ct)))
            .AddEndpointFilter(UcpToolCounting.CountedAs("get_checkout"));

        group.MapPut("/{id:guid}", (Guid id, UcpCheckoutRequest request, HttpContext http, ICommandDispatcher commands, CancellationToken ct) =>
                WithNegotiation(http, ct, (active, links, culture) =>
                    UcpCheckoutOperations.UpdateAsync(commands, id, request, culture(request.Context?.Language), active, links, ct)))
            .AddEndpointFilter(UcpToolCounting.CountedAs("update_checkout"));

        group.MapPost("/{id:guid}/complete", (Guid id, UcpCompleteRequest request, HttpContext http, ICommandDispatcher commands, CancellationToken ct) =>
                WithNegotiation(http, ct, (active, links, culture) =>
                    UcpCheckoutOperations.CompleteAsync(commands, id, request, http.Request.Headers["Idempotency-Key"].FirstOrDefault(), culture(null), active, links, ct)))
            .AddEndpointFilter(UcpToolCounting.CountedAs("complete_checkout"));

        group.MapPost("/{id:guid}/cancel", (Guid id, HttpContext http, ICommandDispatcher commands, CancellationToken ct) =>
                WithNegotiation(http, ct, (active, links, culture) =>
                    UcpCheckoutOperations.CancelAsync(commands, id, culture(null), active, links, ct)))
            .AddEndpointFilter(UcpToolCounting.CountedAs("cancel_checkout"));

        return group;
    }

    /// <summary>
    /// Every operation negotiates first: the `UCP-Agent` profile, fetched from a
    /// trusted host, decides which extensions this answer may carry. The culture is
    /// ADR 0013's chain with the checkout's own <c>context.language</c> first — an
    /// agent has no browser locale.
    /// </summary>
    private static async Task<IResult> WithNegotiation(
        HttpContext http, CancellationToken ct,
        Func<IReadOnlySet<string>, UcpLinks, Func<string?, string>, Task<UcpAnswer>> operation)
    {
        var profiles = http.RequestServices.GetRequiredService<IPlatformProfiles>();
        var (active, refusal) = await UcpCheckoutOperations.NegotiateAsync(
            UcpAgentHeader.ProfileOf(http.Request.Headers[UcpAgentHeader.Name].FirstOrDefault()), profiles, ct);

        var answer = refusal ?? await operation(
            active!,
            new UcpLinks($"{http.Request.Scheme}://{http.Request.Host}", http.RequestServices.GetRequiredService<IOptions<UcpOptions>>().Value.StorefrontUrl),
            language => CultureNegotiation.Resolve(language, http.Request.Headers.AcceptLanguage));

        // Declared as object, so the serializer writes the runtime type: a checkout,
        // an error response or a protocol error, each with its own members.
        return Results.Json(answer.Body, statusCode: answer.Status);
    }
}
