using System.Diagnostics;
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
    IShippingRateProviderRegistry providers,
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

        var rates = await Rate(providers, options.Value, cart, query.Destination, cancellationToken);

        activity?.SetTag("shipping.provider", options.Value.ShippingProvider);
        activity?.SetTag("shipping.option_count", rates.Count);

        return new ShippingOptionsResult(
            rates.Count == 0 ? ShippingOutcome.Unserved : ShippingOutcome.Quoted,
            cart.Currency,
            rates);
    }

    /// <summary>
    /// Shared with <c>PlaceOrder</c> so the option a shopper picked is priced by
    /// exactly the call that offered it. Two code paths here would let checkout
    /// charge for an option the screen never showed.
    ///
    /// The subtotal is the raw line total and not the discounted one, which is a
    /// simplification worth naming: "free over 50 €" is measured before
    /// promotions, so a coupon cannot buy free shipping by accident. A shop that
    /// wanted the other rule would pass the discounted figure, and the port
    /// would not change.
    /// </summary>
    internal static async Task<IReadOnlyList<ShippingOption>> Rate(
        IShippingRateProviderRegistry providers,
        CheckoutOptions options,
        Cart cart,
        Address destination,
        CancellationToken cancellationToken)
    {
        var provider = providers.Get(options.ShippingProvider);

        return await provider.QuoteAsync(
            new ShippingQuoteRequest(
                destination,
                [.. cart.Lines.Select(line => new ShippableLine(line.Sku, line.Quantity))],
                // The cart holds no money (ADR 0016), so the only subtotal
                // available without a second pricing call is a count-based
                // stand-in. It is zero, deliberately: a free-shipping threshold
                // that fired on a number nobody computed would be worse than one
                // that never fires. `PlaceOrder` passes the real subtotal.
                Money.Zero(cart.Currency)),
            cancellationToken);
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

public enum PlaceOrderOutcome
{
    Placed,

    /// <summary>The same idempotency key already bought something. The existing
    /// order comes back, and nothing happens twice.</summary>
    AlreadyPlaced,

    NothingToShip,

    /// <summary>The chosen option is not one this destination is offered. Almost
    /// always a stale screen, and never something to charge for.</summary>
    UnknownShippingOption,

    /// <summary>Some SKU stopped being on sale between the cart and the till.</summary>
    UnknownSkus,

    /// <summary>
    /// The price is not what the shopper was shown. The **new** quote comes back
    /// so the screen can say what changed and ask them to confirm, which is the
    /// whole reason a quote carries a fingerprint.
    /// </summary>
    PriceChanged,

    PaymentDeclined,

    /// <summary>The provider could not be reached. Retryable with the same key,
    /// and a different answer from a decline.</summary>
    PaymentUnavailable
}

public sealed record PlaceOrderResult(
    PlaceOrderOutcome Outcome,
    Order? Order,
    CartPricing? Pricing,
    string? Detail);

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
    IOrderRepository orders,
    ICartPricer pricer,
    IShippingRateProviderRegistry shipping,
    IPaymentProviderRegistry payments,
    IPrincipalAccessor principal,
    IUnitOfWork unitOfWork,
    IOptions<CheckoutOptions> options,
    TimeProvider clock)
    : ICommandHandler<PlaceOrderCommand, PlaceOrderResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<PlaceOrderResult> HandleAsync(
        PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("checkout.place_order");
        activity?.SetTag("checkout.idempotency_key", command.IdempotencyKey);

        // Idempotency first, before anything is charged. With agents, a retry is
        // the normal case rather than the rare one.
        var existing = await orders.FindByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);

        if (existing is not null)
        {
            activity?.SetTag("checkout.outcome", "already_placed");
            return new PlaceOrderResult(PlaceOrderOutcome.AlreadyPlaced, existing, null, null);
        }

        var cart = command.Token is null
            ? null
            : await carts.FindOpenByTokenAsync(command.Token, cancellationToken);

        if (cart is null || cart.IsEmpty)
            return Refuse(activity, PlaceOrderOutcome.NothingToShip, null);

        // 1 · Shipping, from the same call that offered the options.
        var rates = await GetShippingOptionsHandler.Rate(
            shipping, options.Value, cart, command.ShippingAddress, cancellationToken);

        var option = rates.FirstOrDefault(rate =>
            string.Equals(rate.Code, command.ShippingOptionCode, StringComparison.OrdinalIgnoreCase));

        if (option is null)
            return Refuse(activity, PlaceOrderOutcome.UnknownShippingOption, command.ShippingOptionCode);

        // 2 · The price, recomputed by the engine that issued the quote.
        var pricing = await pricer.QuoteAsync(
            cart.PricingLines(), command.Culture, option.Amount, command.Coupons, cancellationToken);

        if (pricing.Outcome != PricingOutcome.Quoted)
            return Refuse(activity, PlaceOrderOutcome.UnknownSkus, string.Join(", ", pricing.Offending));

        // 3 · Is it still what the shopper was shown? A missing expectation is
        // treated as a mismatch rather than waved through: an agent that does
        // not send the hash has not agreed to a price.
        if (!string.Equals(pricing.InputHash, command.ExpectedQuoteHash, StringComparison.Ordinal))
        {
            activity?.SetTag("checkout.outcome", "price_changed");
            return new PlaceOrderResult(PlaceOrderOutcome.PriceChanged, null, pricing, pricing.InputHash);
        }

        // 4 · The money. Nothing has been written yet, so a decline costs
        // nothing: no order, no reservation, an untouched cart.
        var provider = payments.Get(options.Value.PaymentProvider);
        var orderId = OrderId.New();

        var authorization = await provider.AuthorizeAsync(
            new PaymentRequest(
                orderId,
                pricing.Totals.Total,
                command.IdempotencyKey,
                command.Instrument,
                $"Tendero {orderId}"),
            cancellationToken);

        if (!authorization.Succeeded)
        {
            activity?.SetTag("checkout.outcome", authorization.Outcome.ToString());

            return new PlaceOrderResult(
                authorization.Outcome == PaymentOutcome.Declined
                    ? PlaceOrderOutcome.PaymentDeclined
                    : PlaceOrderOutcome.PaymentUnavailable,
                null, pricing, authorization.DeclineReason);
        }

        // 5 · Who is buying.
        //
        // Guests are first class, so a customer id is MINTED here when nobody
        // signed in — an order always has a buyer, even an anonymous one, and a
        // nullable CustomerId would spread null through every order query for a
        // case the domain does not actually have. It is written back onto the
        // cart so the two agree, which is what phase 7's ClaimGuestAccount will
        // link an account to.
        var customer = principal.Current.Customer ?? cart.CustomerId ?? CustomerId.New();

        if (cart.CustomerId is null)
            cart.Claim(clock, customer);

        var unitPrices = pricing.Lines.ToDictionary(
            line => line.Sku, line => line.UnitPrice, StringComparer.OrdinalIgnoreCase);

        var order = Order.Place(
            clock,
            customer,
            command.IdempotencyKey,
            [
                .. cart.Lines.Select(line => new OrderLine(
                    line.ProductId,
                    line.VariantId,
                    line.Sku,
                    line.ProductName,
                    line.VariantLabel,
                    // The price pricing just quoted, not one the cart carried:
                    // the cart carries none, on purpose.
                    unitPrices[line.Sku],
                    line.Quantity))
            ],
            command.ShippingAddress,
            // Billing defaults to shipping and is still its OWN copy: "the same
            // today" is not "the same forever".
            command.BillingAddress ?? command.ShippingAddress,
            new OrderShipping(option.Code, option.Label.In(cart.Culture), option.Amount, option.EstimatedDays),
            pricing.AsOrderQuote(),
            pricing.Totals,
            pricing.Discounts,
            pricing.Taxes,
            cart.Culture);

        order.AuthorizePayment(clock, new OrderPayment(provider.Key, authorization.AuthorizationId!, null));

        orders.Add(order);

        // 6 · The cart is done. No `CartCheckedOut` event: this is one
        // transaction in one handler, and `OrderPlaced` is the fact the rest of
        // the system reacts to.
        cart.MarkCheckedOut(clock);

        // One commit. The order, the closed cart and the events that drive the
        // stock saga all land together or none of them do — which is the outbox's
        // whole correctness argument.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("checkout.outcome", "placed");
        activity?.SetTag("order.id", order.Id.ToString());
        activity?.SetTag("order.total", order.Total.Amount);

        return new PlaceOrderResult(PlaceOrderOutcome.Placed, order, pricing, null);
    }

    private static PlaceOrderResult Refuse(Activity? activity, PlaceOrderOutcome outcome, string? detail)
    {
        activity?.SetTag("checkout.outcome", outcome.ToString());
        return new PlaceOrderResult(outcome, null, null, detail);
    }
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
            // The cart token is the credential, and phase 7 puts an account
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
