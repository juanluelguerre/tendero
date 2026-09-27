using Carter;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Ordering.Features.CheckoutSessions;

/// <summary>
/// UCP's escalation, the shop's side (P11-1): a checkout that needs a person —
/// because the shop does not take an agent's live payment — sends them to the
/// storefront with its id, and this hands the storefront the session's cart so
/// they pay for exactly what the agent chose. Paying completes the session.
///
/// **The session id is the credential**, the rule a guest's order already follows:
/// a GUID v7 whose random bits nobody enumerates, carried in the link the agent
/// gives its person. It hands over a basket — lines and a token — and nothing
/// about who asked; only an open, unexpired session hands anything over.
/// </summary>
public sealed record HandOffCheckoutSessionQuery(CheckoutSessionId SessionId) : IQuery<CheckoutHandOff?>;

/// <summary>What the storefront needs to take over: the cart's token, and the language the checkout was in.</summary>
public sealed record CheckoutHandOff(string CartToken, string Culture);

public sealed class HandOffCheckoutSessionHandler(ICheckoutSessionRepository sessions, ICartRepository carts, TimeProvider clock)
    : IQueryHandler<HandOffCheckoutSessionQuery, CheckoutHandOff?>
{
    public async Task<CheckoutHandOff?> HandleAsync(HandOffCheckoutSessionQuery query, CancellationToken cancellationToken)
    {
        var session = await sessions.FindByIdAsync(query.SessionId, cancellationToken);
        if (session is not { Status: CheckoutSessionStatus.Open } || session.HasExpiredAt(clock.GetUtcNow()))
            return null;

        return await carts.FindByIdAsync(session.CartId, cancellationToken) is { Status: CartStatus.Open } cart
            ? new CheckoutHandOff(cart.Token, session.Culture)
            : null;
    }
}

public sealed class HandOffCheckoutSessionEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/checkout-sessions/{id}/handoff — the storefront's continue page.
        app.MapGet("/api/checkout-sessions/{id:guid}/handoff",
                async Task<Results<Ok<CheckoutHandOff>, NotFound>> (Guid id, IQueryDispatcher dispatcher, CancellationToken ct) =>
                    await dispatcher.SendAsync(new HandOffCheckoutSessionQuery(new CheckoutSessionId(id)), ct) is { } handOff
                        ? TypedResults.Ok(handOff)
                        : TypedResults.NotFound())
            // Anonymous by decision: the person the agent sent here has no account
            // with the shop yet, and the id in the link is the credential.
            .AllowAnonymous()
            .WithTags("Checkout")
            .WithName("HandOffCheckoutSession");
    }
}
