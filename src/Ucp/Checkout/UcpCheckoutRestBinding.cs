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

        group.MapPost("/", (UcpCheckoutRequest request, HttpContext http, UcpCheckoutOperations checkout, CancellationToken ct) =>
                WithNegotiation(http, ct, (call, culture) =>
                    checkout.CreateAsync(request, Key(http), culture(request.Context?.Language), call, ct)))
            .AddEndpointFilter(UcpToolCounting.CountedAs("create_checkout"));

        group.MapGet("/{id:guid}", (Guid id, HttpContext http, UcpCheckoutOperations checkout, CancellationToken ct) =>
                WithNegotiation(http, ct, (call, culture) =>
                    checkout.GetAsync(id, culture(null), call, ct)))
            .AddEndpointFilter(UcpToolCounting.CountedAs("get_checkout"));

        group.MapPut("/{id:guid}", (Guid id, UcpCheckoutRequest request, HttpContext http, UcpCheckoutOperations checkout, CancellationToken ct) =>
                WithNegotiation(http, ct, (call, culture) =>
                    checkout.UpdateAsync(id, request, Key(http), culture(request.Context?.Language), call, ct)))
            .AddEndpointFilter(UcpToolCounting.CountedAs("update_checkout"));

        group.MapPost("/{id:guid}/complete", (Guid id, UcpCompleteRequest request, HttpContext http, UcpCheckoutOperations checkout, CancellationToken ct) =>
                WithNegotiation(http, ct, (call, culture) =>
                    checkout.CompleteAsync(id, request, Key(http), culture(null), call, ct)))
            .AddEndpointFilter(UcpToolCounting.CountedAs("complete_checkout"));

        group.MapPost("/{id:guid}/cancel", (Guid id, HttpContext http, UcpCheckoutOperations checkout, CancellationToken ct) =>
                WithNegotiation(http, ct, (call, culture) =>
                    checkout.CancelAsync(id, Key(http), culture(null), call, ct)))
            .AddEndpointFilter(UcpToolCounting.CountedAs("cancel_checkout"));

        return group;
    }

    /// <summary>The caller's `Idempotency-Key`, on every operation that changes something: when one is sent, it is honoured.</summary>
    private static string? Key(HttpContext http) => http.Request.Headers[Idempotency.Header].FirstOrDefault();

    /// <summary>
    /// Every operation negotiates first: the `UCP-Agent` profile, fetched from a
    /// trusted host, decides which extensions this answer may carry. The culture is
    /// ADR 0013's chain with the checkout's own <c>context.language</c> first — an
    /// agent has no browser locale.
    /// </summary>
    private static async Task<IResult> WithNegotiation(
        HttpContext http, CancellationToken ct,
        Func<UcpCheckoutCall, Func<string?, string>, Task<UcpAnswer>> operation)
    {
        var profiles = http.RequestServices.GetRequiredService<IPlatformProfiles>();
        var (negotiated, refusal) = await UcpCheckoutOperations.NegotiateAsync(
            UcpAgentHeader.ProfileOf(http.Request.Headers[UcpAgentHeader.Name].FirstOrDefault()), profiles, ct);

        var answer = refusal ?? await operation(
            new UcpCheckoutCall(negotiated!, new UcpLinks($"{http.Request.Scheme}://{http.Request.Host}", http.RequestServices.GetRequiredService<IOptions<UcpOptions>>().Value.StorefrontUrl)),
            language => CultureNegotiation.Resolve(language, http.Request.Headers.AcceptLanguage));

        // Declared as object, so the serializer writes the runtime type: a checkout,
        // an error response or a protocol error, each with its own members.
        return Results.Json(answer.Body, statusCode: answer.Status);
    }
}
