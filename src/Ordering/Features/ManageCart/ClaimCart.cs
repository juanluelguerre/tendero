using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Ordering.Features.ManageCart;

/// <summary>
/// A guest signed in, and the basket they were carrying becomes theirs.
///
/// **It lives in Ordering, not in Accounts**, and that is the decision. Accounts
/// answers "who is this"; the cart belongs here. Putting the whole sign-in in
/// one context would mean one of them reaching into the other — and Ordering
/// will need Accounts later, for order history, so a dependency the other way
/// would close a cycle.
///
/// So signing in is two requests: `POST /api/accounts/me` establishes the
/// customer, and this attaches the basket. Each context owns its half and
/// neither imports the other.
///
/// The CUSTOMER comes from the principal and never from the body. A body that
/// could name one would let anybody attach their basket to somebody else's
/// account — which is the same reason `LinkIdentity` takes its subject from the
/// token.
/// </summary>
public sealed record ClaimCartCommand(string Token) : ICommand<ClaimCartResult>;

public enum ClaimCartOutcome
{
    Claimed,

    /// <summary>The cart is already this customer's. Signing in twice, or a
    /// browser retrying, and neither is an error.</summary>
    AlreadyTheirs,

    NotFound,

    /// <summary>Somebody else's basket. It is refused rather than merged: two
    /// carts becoming one is a decision a person makes, not one a sign-in makes
    /// for them.</summary>
    BelongsToSomebodyElse
}

public sealed record ClaimCartResult(ClaimCartOutcome Outcome, CartView? Cart);

public sealed class ClaimCartValidator : AbstractValidator<ClaimCartCommand>
{
    public ClaimCartValidator() => RuleFor(command => command.Token).NotEmpty();
}

public sealed class ClaimCartEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/cart/claim
        app.MapPost("/api/cart/claim",
            async Task<Results<Ok<CartView>, NotFound, Conflict<string>>> (
                   HttpContext http, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var token = http.Request.Headers[CartHeaders.Token].ToString();

                if (string.IsNullOrWhiteSpace(token))
                    return TypedResults.NotFound();

                var result = await dispatcher.SendAsync(new ClaimCartCommand(token), ct);

                return result.Outcome switch
                {
                    ClaimCartOutcome.NotFound => TypedResults.NotFound(),
                    ClaimCartOutcome.BelongsToSomebodyElse => TypedResults.Conflict(
                        "That basket belongs to another account."),
                    _ => TypedResults.Ok(result.Cart!)
                };
            })
            .RequireAuthorization()
            .WithTags("Ordering")
            .WithName("ClaimCart");
    }
}

public sealed class ClaimCartHandler(
    ICartRepository carts,
    IPrincipalAccessor principals,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<ClaimCartCommand, ClaimCartResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<ClaimCartResult> HandleAsync(
        ClaimCartCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.claim_cart");

        var customer = principals.Current.Customer
            ?? throw new InvalidOperationException(
                "Claiming a cart needs a customer. Call POST /api/accounts/me first.");

        activity?.SetTag("ordering.customer_id", customer.Value);

        var cart = await carts.FindOpenByTokenAsync(command.Token, cancellationToken);

        if (cart is null)
            return new ClaimCartResult(ClaimCartOutcome.NotFound, null);

        if (cart.CustomerId == customer)
        {
            // Idempotent, like everything a browser can send twice. It answers
            // with the cart rather than with a refusal, because from the
            // caller's side nothing is wrong.
            activity?.SetTag("ordering.outcome", nameof(ClaimCartOutcome.AlreadyTheirs));
            return new ClaimCartResult(ClaimCartOutcome.AlreadyTheirs, GetCartHandler.View(cart));
        }

        if (cart.CustomerId is not null)
        {
            activity?.SetTag("ordering.outcome", nameof(ClaimCartOutcome.BelongsToSomebodyElse));
            return new ClaimCartResult(ClaimCartOutcome.BelongsToSomebodyElse, null);
        }

        cart.Claim(clock, customer);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("ordering.outcome", nameof(ClaimCartOutcome.Claimed));

        return new ClaimCartResult(ClaimCartOutcome.Claimed, GetCartHandler.View(cart));
    }
}
