using System.Diagnostics;
using System.Diagnostics.Metrics;
using Carter;
using ElGuerre.Tendero.Ordering.Contracts;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.ManageCart;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Ordering.Features.Checkout;

// ---------- What the wire carries ----------

public sealed record AddressRequest(
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    string? Phone);

public sealed record ShippingOptionView(string Code, string Label, decimal Amount, int? EstimatedDays);

public sealed record ShippingOptionsResponse(
    string Currency, IReadOnlyList<ShippingOptionView> Options);

// ---------- Shipping options ----------

/// <summary>
/// What it costs to get this cart to that address.
///
/// It is a query and it takes the whole destination, because a rate is a
/// function of one — that is why shipping lives in `Ordering` and tax lives in
/// `Pricing`. Dragging an `Address` into `Pricing` would ruin the purity that
/// makes the promotion engine property-testable.
/// </summary>
public sealed record GetShippingOptionsQuery(string? Token, Address Destination, string Culture)
    : IQuery<ShippingOptionsResult>;

public enum ShippingOutcome
{
    Quoted,

    /// <summary>No cart, or an empty one. There is nothing to send.</summary>
    NothingToShip,

    /// <summary>Nowhere this shop delivers. A business fact, not an error — and
    /// the reason the port answers with an empty list rather than throwing.</summary>
    Unserved
}

public sealed record ShippingOptionsResult(
    ShippingOutcome Outcome, string Currency, IReadOnlyList<ShippingOption> Options);

public sealed class GetShippingOptionsHandler(
    ICartRepository carts,
    OrderPlacement placement,
    IOptions<CheckoutOptions> options)
    : IQueryHandler<GetShippingOptionsQuery, ShippingOptionsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<ShippingOptionsResult> HandleAsync(
        GetShippingOptionsQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("checkout.shipping_options");
        activity?.SetTag("shipping.country", query.Destination.CountryCode);

        var cart = query.Token is null
            ? null
            : await carts.FindOpenByTokenAsync(query.Token, cancellationToken);

        if (cart is null || cart.IsEmpty)
            return new ShippingOptionsResult(ShippingOutcome.NothingToShip, "EUR", []);

        // The same call placing the order makes, so a shopper is never charged
        // for an option the screen did not show.
        var rates = await placement.RateAsync(cart, query.Destination, cancellationToken);

        activity?.SetTag("shipping.provider", options.Value.ShippingProvider);
        activity?.SetTag("shipping.option_count", rates.Count);

        return new ShippingOptionsResult(
            rates.Count == 0 ? ShippingOutcome.Unserved : ShippingOutcome.Quoted,
            cart.Currency,
            rates);
    }
}

// ---------- Placing the order ----------

/// <summary>
/// The moment the shop commits.
///
/// Six things happen and the order matters. The quote is recomputed and its
/// fingerprint compared (ADR 0016); the money is authorised; the order is placed
/// with everything frozen onto it; the cart is closed; and all of it commits in
/// one transaction with the events that drive the rest.
///
/// **Payment is authorised before the order exists**, which is the one ordering
/// decision worth arguing about. The alternative — place, then authorise — would
/// leave a row and a held reservation behind every declined card, and nothing
/// sweeps those yet. Authorising first means a decline costs nothing: no order,
/// no stock, an untouched cart, and a sentence the shopper can act on.
///
/// What it costs is the mirror case: authorised, and then no stock. That is why
/// the port has <c>VoidAsync</c> and why the cancellation arm of the saga
/// releases the hold — the compensation is real rather than an authorisation
/// left to expire in a week.
/// </summary>
public sealed record PlaceOrderCommand(
    string? Token,
    Address ShippingAddress,
    Address? BillingAddress,
    string ShippingOptionCode,
    string? ExpectedQuoteHash,
    string Instrument,
    string IdempotencyKey,
    IReadOnlyList<string> Coupons,
    string Culture) : ICommand<PlaceOrderResult>;

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(command => command.IdempotencyKey).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Instrument).NotEmpty().MaximumLength(100);
        RuleFor(command => command.ShippingOptionCode).NotEmpty().MaximumLength(50);
        RuleFor(command => command.ShippingAddress).NotNull();
    }
}

public sealed class PlaceOrderHandler(
    ICartRepository carts,
    OrderPlacement placement,
    IPrincipalAccessor principal,
    IActorAccessor actors)
    : ICommandHandler<PlaceOrderCommand, PlaceOrderResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    /// <summary>
    /// Every attempt is counted once, whichever of its exits it took: a counter
    /// in each return is a counter that one day misses one.
    /// </summary>
    public async Task<PlaceOrderResult> HandleAsync(
        PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        // Decided once, from the credential, before anything else: the payment,
        // the order and the metric all read the same answer (ADR 0038).
        var mode = OrderModes.Of(principal.Current);
        var result = await PlaceAsync(command, mode, cancellationToken);
        CheckoutAttempts.Record(result, actors.Current, mode, principal.Current.Standing);
        return result;
    }

    private async Task<PlaceOrderResult> PlaceAsync(
        PlaceOrderCommand command, OrderMode mode, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("checkout.place_order");
        activity?.SetTag("checkout.idempotency_key", command.IdempotencyKey);
        activity?.SetTag(CheckoutAttempts.ModeTag, MetricLabels.Of(mode));

        // Idempotency first, before anything is charged. With agents, a retry is
        // the normal case rather than the rare one.
        if (await placement.AlreadyPlacedAsync(command.IdempotencyKey, cancellationToken) is { } existing)
        {
            activity?.SetTag("checkout.outcome", "already_placed");
            return new PlaceOrderResult(PlaceOrderOutcome.AlreadyPlaced, existing, null, null);
        }

        var cart = command.Token is null
            ? null
            : await carts.FindOpenByTokenAsync(command.Token, cancellationToken);

        if (cart is null || cart.IsEmpty)
        {
            activity?.SetTag("checkout.outcome", PlaceOrderOutcome.NothingToShip.ToString());
            return new PlaceOrderResult(PlaceOrderOutcome.NothingToShip, null, null, null);
        }

        return await placement.PlaceAsync(
            cart,
            new PlacementRequest(
                command.ShippingAddress, command.BillingAddress, command.ShippingOptionCode,
                command.ExpectedQuoteHash, command.Instrument, command.IdempotencyKey, command.Coupons, command.Culture),
            mode,
            cancellationToken);
    }
}

/// <summary>
/// `tendero.checkout.attempts` (the tracking plan): orders placed are the
/// attempts whose outcome is <c>placed</c>, and every other outcome says which
/// fix a failed checkout points at — our screen, our price, the shopper's card
/// or the provider.
/// </summary>
internal static class CheckoutAttempts
{
    private static readonly Meter Meter = new(TelemetrySources.Ordering);

    private static readonly Counter<long> Attempts = Meter.CreateCounter<long>(
        "tendero.checkout.attempts", unit: "{attempt}",
        description: "Every place-order attempt by its outcome; orders placed are the ones whose outcome is placed.");

    /// <summary>
    /// On every attempt, so the *Shop pulse* can leave rehearsals out of the
    /// shop's numbers and a test run can be read on its own (ADR 0038).
    /// </summary>
    public const string ModeTag = "order.mode";

    /// <summary>
    /// How far the shop trusts the agent behind the attempt (P11-2): <c>none</c>
    /// for a person, <c>untrusted</c> for an agent nobody registered. What share of
    /// agent sales come from agents the shop does not know is the question the
    /// registry exists to answer.
    /// </summary>
    public const string TrustTag = "agent.trust";

    public static void Record(PlaceOrderResult result, Actor actor, OrderMode mode, AgentStanding? standing) =>
        Attempts.Add(1,
            actor.KindLabel,
            actor.SurfaceLabel,
            new(ModeTag, MetricLabels.Of(mode)),
            new(TrustTag, standing is null ? "none" : MetricLabels.Of(standing.Trust)),
            new("outcome", MetricLabels.Of(result.Outcome)),
            // Read off the priced cart when there was one; an attempt refused
            // before pricing had no discount to apply.
            new("discounted", result.Pricing?.Discounts.Count > 0 || result.Order?.Discounts.Count > 0));
}

// ---------- The wire ----------

public sealed record PlaceOrderRequest(
    AddressRequest ShippingAddress,
    string ShippingOptionCode,
    string QuoteHash,
    string Instrument,
    string IdempotencyKey,
    AddressRequest? BillingAddress = null,
    IReadOnlyList<string>? Coupons = null);

/// <summary>
/// What comes back when the price moved: the new fingerprint and the new total,
/// so the screen can say what changed rather than just refusing.
/// </summary>
public sealed record PriceChangedResponse(
    string QuoteHash, decimal Total, decimal Shipping, decimal TaxTotal, decimal DiscountTotal);

public sealed class CheckoutEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var checkout = app.MapGroup("/api/checkout")
            // Anonymous, like the cart it closes: a guest has to be able to buy.
            // The cart token is the credential, and phase 7 put an account
            // behind it without changing this line.
            .AllowAnonymous()
            .WithTags("Checkout");

        checkout.MapPost("/shipping-options",
            async Task<Results<Ok<ShippingOptionsResponse>, BadRequest<string>>> (
                   AddressRequest request, string? culture,
                   HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                if (!TryRead(request, out var destination, out var problem))
                    return TypedResults.BadRequest(problem);

                var result = await dispatcher.SendAsync(
                    new GetShippingOptionsQuery(Token(http), destination, resolved), ct);

                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return result.Outcome switch
                {
                    ShippingOutcome.NothingToShip => TypedResults.BadRequest("There is nothing in the cart."),
                    ShippingOutcome.Unserved => TypedResults.BadRequest(
                        $"This shop does not deliver to {destination.CountryCode} yet."),
                    _ => TypedResults.Ok(new ShippingOptionsResponse(
                        result.Currency,
                        [
                            .. result.Options.Select(option => new ShippingOptionView(
                                option.Code, option.Label.In(resolved),
                                option.Amount.Amount, option.EstimatedDays))
                        ]))
                };
            })
            // POST and not GET, because it takes an address in the body. An
            // address in a query string would be personal data in every log and
            // every cache key.
            .WithName("GetShippingOptions");

        checkout.MapPost("/",
            async Task<Results<Ok<OrderView>, BadRequest<string>, Conflict<PriceChangedResponse>,
                       NotFound<string>, ProblemHttpResult>> (
                   PlaceOrderRequest request, string? culture,
                   HttpContext http, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                if (!TryRead(request.ShippingAddress, out var shipTo, out var problem))
                    return TypedResults.BadRequest(problem);

                Address? billTo = null;
                if (request.BillingAddress is not null && !TryRead(request.BillingAddress, out billTo, out problem))
                    return TypedResults.BadRequest(problem);

                var result = await dispatcher.SendAsync(
                    new PlaceOrderCommand(
                        Token(http),
                        shipTo,
                        billTo,
                        request.ShippingOptionCode,
                        request.QuoteHash,
                        request.Instrument,
                        request.IdempotencyKey,
                        request.Coupons ?? [],
                        resolved),
                    ct);

                http.Response.Headers.ContentLanguage = resolved;

                return result.Outcome switch
                {
                    PlaceOrderOutcome.NothingToShip => TypedResults.BadRequest("There is nothing in the cart."),

                    PlaceOrderOutcome.UnknownShippingOption => TypedResults.BadRequest(
                        $"'{result.Detail}' is not one of the delivery options for this address."),

                    PlaceOrderOutcome.UnknownSkus => TypedResults.NotFound(
                        $"These are no longer on sale: {result.Detail}."),

                    // 409 and the NEW quote. The shopper is not wrong and the
                    // shop is not wrong; the price moved between the two, and
                    // the only honest answer is to show the difference.
                    PlaceOrderOutcome.PriceChanged => TypedResults.Conflict(new PriceChangedResponse(
                        result.Pricing!.InputHash,
                        result.Pricing.Totals.Total.Amount,
                        result.Pricing.Totals.Shipping.Amount,
                        result.Pricing.Totals.TaxTotal.Amount,
                        result.Pricing.Totals.DiscountTotal.Amount)),

                    // 402: the request was fine and the card said no. A 400
                    // would tell the client to fix its request, which is exactly
                    // the wrong advice.
                    PlaceOrderOutcome.PaymentDeclined => TypedResults.Problem(
                        detail: result.Detail, statusCode: StatusCodes.Status402PaymentRequired),

                    // 503 and retryable: same idempotency key, same order.
                    PlaceOrderOutcome.PaymentUnavailable => TypedResults.Problem(
                        detail: result.Detail, statusCode: StatusCodes.Status503ServiceUnavailable),

                    _ => TypedResults.Ok(OrderView.From(result.Order!))
                };
            })
            .WithName("PlaceOrder");
    }

    private static string? Token(HttpContext http) =>
        http.Request.Headers.TryGetValue(CartHeaders.Token, out var values) &&
        !string.IsNullOrWhiteSpace(values.ToString())
            ? values.ToString()
            : null;

    /// <summary>
    /// The address's own validation lives on the value object, so it is the same
    /// wherever one is built — an agent over UCP will not go through this
    /// endpoint. What belongs here is turning the refusal into a status code.
    /// </summary>
    private static bool TryRead(AddressRequest request, out Address address, out string problem)
    {
        try
        {
            address = Address.Create(
                request.RecipientName, request.Line1, request.Line2, request.City,
                request.Region, request.PostalCode, request.CountryCode, request.Phone);

            problem = string.Empty;
            return true;
        }
        catch (ArgumentException exception)
        {
            address = null!;
            problem = exception.Message;
            return false;
        }
    }
}
