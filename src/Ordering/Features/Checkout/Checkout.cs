using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
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
using Microsoft.AspNetCore.Mvc;
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

public sealed record ShippingOptionView(string Code, string Label, Money Amount, int? EstimatedDays);

public sealed record ShippingOptionsResponse(
    IReadOnlyList<ShippingOptionView> Options);

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
    [property: AuditRedacted] string Instrument,
    string IdempotencyKey,
    IReadOnlyList<string> Coupons,
    string Culture,
    SavedMethodId? SavedMethod = null) : ICommand<PlaceOrderResult>;

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(command => command.IdempotencyKey).NotEmpty().MaximumLength(200);
        // A card typed now or one kept in the wallet (P15-4), never both and never neither.
        RuleFor(command => command.Instrument).NotEmpty().MaximumLength(100).When(command => command.SavedMethod is null);
        RuleFor(command => command.Instrument).Empty().When(command => command.SavedMethod is not null)
            .WithMessage("A saved way to pay is charged by its id alone.");
        RuleFor(command => command.ShippingOptionCode).NotEmpty().MaximumLength(50);
        RuleFor(command => command.ShippingAddress).NotNull();
    }
}

public sealed class PlaceOrderHandler(
    ICartRepository carts,
    IWalletRepository wallets,
    IOptions<CheckoutOptions> options,
    ICheckoutSessionRepository sessions,
    OrderPlacement placement,
    IUnitOfWork unitOfWork,
    IPrincipalAccessor principal,
    IActorAccessor actors,
    TimeProvider clock)
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

        // A saved way to pay is charged by its token, read here and nowhere else: the
        // browser named it by the id the shop minted (P15-4). Only the person's own
        // wallet, only through the provider that kept it, and never for an agent,
        // which uses none of the account in phase 15 (decision 8). Anything else is
        // a card the shop will not charge: declined, with nothing written.
        var instrument = command.Instrument;
        if (command.SavedMethod is { } saved)
        {
            var method = principal.Current is { Agent: null, Customer: { } owner }
                ? (await wallets.FindAsync(owner, cancellationToken))?.Find(saved)
                : null;

            if (method is null || method.ProviderKey != options.Value.ProviderFor(mode))
            {
                activity?.SetTag("checkout.outcome", "saved_method_unknown");
                return new PlaceOrderResult(PlaceOrderOutcome.PaymentDeclined, null, null, "That saved way to pay is not in this account's wallet.");
            }

            activity?.SetTag("checkout.saved_method", true);
            instrument = method.Token;
        }

        // A cart an agent's checkout handed to a person (requires_escalation,
        // P11-1): the email the agent gave is where the order is confirmed.
        var session = await sessions.FindByCartAsync(cart.Id, cancellationToken) is { Status: CheckoutSessionStatus.Open } open ? open : null;

        var result = await placement.PlaceAsync(
            cart,
            new PlacementRequest(
                command.ShippingAddress, command.BillingAddress, command.ShippingOptionCode,
                command.ExpectedQuoteHash, instrument, command.IdempotencyKey, command.Coupons, command.Culture,
                session?.Buyer.Email),
            mode,
            cancellationToken);

        if (result.Outcome == PlaceOrderOutcome.Placed)
        {
            // Paying for it here is what completes the agent's checkout, so the
            // agent reads the order it asked for.
            session?.Complete(clock, result.Order!.Id);

            // One commit: the order, the closed cart, the session and the events
            // that drive the stock saga land together or none of them do.
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

// ---------- The wire ----------

public sealed record PlaceOrderRequest(
    AddressRequest ShippingAddress,
    string ShippingOptionCode,
    string QuoteHash,
    string Instrument,
    AddressRequest? BillingAddress = null,
    IReadOnlyList<string>? Coupons = null,
    Guid? SavedMethodId = null);

/// <summary>
/// What comes back when the price moved: the new fingerprint and the new total,
/// so the screen can say what changed rather than just refusing.
/// </summary>
public sealed record PriceChangedResponse(
    string QuoteHash, Money Total, Money Shipping, Money TaxTotal, Money DiscountTotal);

/// <summary>
/// What the checkout refuses, by code. `PriceChanged` is the one that hands
/// something over: the new fingerprint and the new totals, so the screen can
/// say what changed rather than just refuse.
/// </summary>
public static class CheckoutRefusals
{
    public static readonly Refusal InvalidAddress = new(
        "InvalidAddress", 400, "La dirección no está completa o no es válida.", "The address is incomplete or not valid.");

    public static readonly Refusal NothingToShip = new(
        "NothingToShip", 400, "No hay nada en el carrito.", "There is nothing in the cart.");

    public static readonly Refusal Unserved = new(
        "Unserved", 400, "La tienda todavía no envía a ese país.", "This shop does not deliver to that country yet.");

    public static readonly Refusal UnknownShippingOption = new(
        "UnknownShippingOption", 400,
        "Esa opción de envío no es una de las de esta dirección.", "That is not one of the delivery options for this address.");

    public static readonly Refusal NoLongerOnSale = new(
        "NoLongerOnSale", 404, "Hay artículos del carrito que ya no se venden.", "Some of the cart is no longer on sale.");

    public static readonly Refusal PriceChanged = new(
        "PriceChanged", 409, "El precio ha cambiado mientras estabas aquí.", "The price changed while you were here.")
    {
        Carries = typeof(PriceChangedResponse),
    };

    /// <summary>402: the request was fine and the card said no.</summary>
    public static readonly Refusal PaymentDeclined = new(
        "PaymentDeclined", 402, "El pago no se ha aceptado.", "The payment was declined.");

    /// <summary>503, and retryable: the same key places the same order.</summary>
    public static readonly Refusal PaymentUnavailable = new(
        "PaymentUnavailable", 503,
        "No se ha podido contactar con el pago. Inténtalo de nuevo: no se cobra dos veces.",
        "The payment could not be reached. Try again: nothing is charged twice.");

    public static readonly Refusal NotMandated = new(
        "NotMandated", 409, "Ese pedido necesita un mandato que no lleva.", "That order needs a mandate it does not carry.");

    public static readonly Refusal AgentRefused = new(
        "AgentRefused", 403, "La tienda no admite este pedido de un agente.", "The shop does not take this order from an agent.");

    /// <summary>409: nothing charged, and which articles there is not enough of.</summary>
    public static readonly Refusal InsufficientStock = new(
        "InsufficientStock", 409,
        "No queda suficiente de algo que llevas en la cesta.", "There is not enough left of something in your basket.")
    {
        Carries = typeof(InsufficientStockResponse),
    };
}

/// <summary>What comes back when the shelf is short: each article with what was asked and what can be bought now.</summary>
public sealed record InsufficientStockResponse(IReadOnlyList<StockShortage> Shortages);

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
            async Task<Results<Ok<ShippingOptionsResponse>, ProblemHttpResult>> (
                   AddressRequest request, string? culture,
                   HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                if (!TryRead(request, out var destination, out var problem))
                    return TypedResults.Problem(statusCode: CheckoutRefusals.InvalidAddress.Status, extensions: CheckoutRefusals.InvalidAddress.Said(reason: problem));

                var result = await dispatcher.SendAsync(
                    new GetShippingOptionsQuery(Token(http), destination, resolved), ct);

                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return result.Outcome switch
                {
                    ShippingOutcome.NothingToShip => TypedResults.Problem(statusCode: CheckoutRefusals.NothingToShip.Status, extensions: CheckoutRefusals.NothingToShip.Said()),
                    ShippingOutcome.Unserved => TypedResults.Problem(
                        statusCode: CheckoutRefusals.Unserved.Status,
                        extensions: CheckoutRefusals.Unserved.Said(
                            es: $"Todavía no se envía a {destination.CountryCode}.",
                            en: $"Nothing is delivered to {destination.CountryCode} yet.",
                            path: "$.countryCode")),
                    _ => TypedResults.Ok(new ShippingOptionsResponse(
                        [
                            .. result.Options.Select(option => new ShippingOptionView(
                                option.Code, option.Label.In(resolved),
                                option.Amount, option.EstimatedDays))
                        ]))
                };
            })
            // POST and not GET, because it takes an address in the body. An
            // address in a query string would be personal data in every log and
            // every cache key.
            .WithMetadata(new Refuses(CheckoutRefusals.InvalidAddress, CheckoutRefusals.NothingToShip, CheckoutRefusals.Unserved))
            .WithName("GetShippingOptions");

        checkout.MapPost("/",
            async Task<Results<Created<OrderView>, ProblemHttpResult>> (
                   PlaceOrderRequest request, string? culture,
                   [FromHeader(Name = Idempotency.Header)] string? idempotencyKey,
                   HttpContext http, ICommandDispatcher dispatcher, IIdempotencyStore keys, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                // The key is the request's, in the header every API that has one
                // reads it from (phase 13b). Required: a retry must not buy twice,
                // and a checkout with no key cannot be told from its own retry.
                if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > Idempotency.MaximumKeyLength)
                    return TypedResults.Problem(
                        statusCode: CommonRefusals.IdempotencyKeyRequired.Status,
                        extensions: CommonRefusals.IdempotencyKeyRequired.Said(
                            es: $"De {Idempotency.MaximumKeyLength} caracteres como mucho.",
                            en: $"Of {Idempotency.MaximumKeyLength} characters at most."));

                if (!TryRead(request.ShippingAddress, out var shipTo, out var problem))
                    return TypedResults.Problem(statusCode: CheckoutRefusals.InvalidAddress.Status, extensions: CheckoutRefusals.InvalidAddress.Said(path: "$.shippingAddress", reason: problem));

                Address? billTo = null;
                if (request.BillingAddress is not null && !TryRead(request.BillingAddress, out billTo, out problem))
                    return TypedResults.Problem(statusCode: CheckoutRefusals.InvalidAddress.Status, extensions: CheckoutRefusals.InvalidAddress.Said(path: "$.billingAddress", reason: problem));

                // One key, one request: the cart's key with another basket's body
                // is a conflict and never an order. The order's own unique key is
                // still what makes a retry the same order (below, AlreadyPlaced);
                // this is what tells a retry from a key used twice.
                var scope = $"cart:{Token(http)}";
                switch (await keys.ClaimAsync(scope, idempotencyKey, Idempotency.Fingerprint("place_order", JsonSerializer.Serialize(request)), ct))
                {
                    case IdempotencyClaim.Mismatched:
                        return TypedResults.Problem(statusCode: CommonRefusals.IdempotencyKeyReused.Status, extensions: CommonRefusals.IdempotencyKeyReused.Said());
                    case IdempotencyClaim.InFlight:
                        return TypedResults.Problem(statusCode: CommonRefusals.IdempotencyInProgress.Status, extensions: CommonRefusals.IdempotencyInProgress.Said());
                }

                PlaceOrderResult result;
                try
                {
                    result = await dispatcher.SendAsync(
                        new PlaceOrderCommand(
                            Token(http),
                            shipTo,
                            billTo,
                            request.ShippingOptionCode,
                            request.QuoteHash,
                            request.Instrument,
                            idempotencyKey,
                            request.Coupons ?? [],
                            resolved,
                            request.SavedMethodId is { } savedMethod ? new SavedMethodId(savedMethod) : null),
                        ct);
                }
                catch
                {
                    // Not the request's token: a cancelled request still gives its key back.
                    await keys.ReleaseAsync(scope, idempotencyKey, CancellationToken.None);
                    throw;
                }

                // What is kept is what placed an order. A refusal changed nothing,
                // so the key is free: a declined card is retried with the key it had.
                if (result is { Outcome: PlaceOrderOutcome.Placed or PlaceOrderOutcome.AlreadyPlaced, Order: { } placed })
                    await keys.CompleteAsync(scope, idempotencyKey, StatusCodes.Status201Created, placed.Id.ToString(), ct);
                else
                    await keys.ReleaseAsync(scope, idempotencyKey, ct);

                http.Response.Headers.ContentLanguage = resolved;

                return result.Outcome switch
                {
                    PlaceOrderOutcome.NothingToShip => TypedResults.Problem(statusCode: CheckoutRefusals.NothingToShip.Status, extensions: CheckoutRefusals.NothingToShip.Said()),

                    PlaceOrderOutcome.UnknownShippingOption => TypedResults.Problem(
                        statusCode: CheckoutRefusals.UnknownShippingOption.Status,
                        extensions: CheckoutRefusals.UnknownShippingOption.Said(
                            es: $"«{result.Detail}» no se ofrece para esta dirección.",
                            en: $"'{result.Detail}' is not offered for this address.",
                            path: "$.shippingOptionCode")),

                    PlaceOrderOutcome.UnknownSkus => TypedResults.Problem(
                        statusCode: CheckoutRefusals.NoLongerOnSale.Status,
                        extensions: CheckoutRefusals.NoLongerOnSale.Said(
                            es: $"Ya no se venden: {result.Detail}.", en: $"No longer on sale: {result.Detail}.")),

                    // 409 and the NEW quote. The shopper is not wrong and the
                    // shop is not wrong; the price moved between the two, and
                    // the only honest answer is to show the difference.
                    PlaceOrderOutcome.PriceChanged => TypedResults.Problem(
                        statusCode: CheckoutRefusals.PriceChanged.Status,
                        extensions: CheckoutRefusals.PriceChanged.Said(
                            path: "$.quoteHash",
                            carrying: new PriceChangedResponse(
                                result.Pricing!.InputHash,
                                result.Pricing.Totals.Total,
                                result.Pricing.Totals.Shipping,
                                result.Pricing.Totals.TaxTotal,
                                result.Pricing.Totals.DiscountTotal))),

                    // 402: the request was fine and the card said no. A 400
                    // would tell the client to fix its request, which is exactly
                    // the wrong advice.
                    // The provider's own words are the reason: they go where the
                    // shopkeeper reads them, and the shopper is told in the shop's.
                    PlaceOrderOutcome.PaymentDeclined => TypedResults.Problem(statusCode: CheckoutRefusals.PaymentDeclined.Status, extensions: CheckoutRefusals.PaymentDeclined.Said(path: "$.instrument", reason: result.Detail)),

                    // 503 and retryable: same idempotency key, same order.
                    PlaceOrderOutcome.PaymentUnavailable => TypedResults.Problem(statusCode: CheckoutRefusals.PaymentUnavailable.Status, extensions: CheckoutRefusals.PaymentUnavailable.Said(reason: result.Detail)),

                    // Only an agent's checkout carries a mandate (P11-5); here it
                    // cannot happen, and if it ever did it is a refusal, not an order.
                    PlaceOrderOutcome.NotMandated => TypedResults.Problem(statusCode: CheckoutRefusals.NotMandated.Status, extensions: CheckoutRefusals.NotMandated.Said(reason: result.Detail)),

                    // An agent's token on the storefront's checkout is held to the
                    // shop's rules for agents too (P11-6).
                    PlaceOrderOutcome.AgentRefused => TypedResults.Problem(statusCode: CheckoutRefusals.AgentRefused.Status, extensions: CheckoutRefusals.AgentRefused.Said(reason: result.Detail)),

                    // 409, and what there is: the cart is fine and the shelf is
                    // short, so the answer names each article with what was asked
                    // and what can be had — a screen offers to buy that many.
                    PlaceOrderOutcome.InsufficientStock => TypedResults.Problem(
                        statusCode: CheckoutRefusals.InsufficientStock.Status,
                        extensions: CheckoutRefusals.InsufficientStock.Said(
                            carrying: new InsufficientStockResponse(result.Shortages!))),

                    // 201, and where the order is: it has an address of its own
                    // from now on, and a retry with the same key is told the same.
                    _ => TypedResults.Created($"/api/orders/{result.Order!.Id}", OrderView.From(result.Order!))
                };
            })
            .WithMetadata(new Refuses(
                CommonRefusals.IdempotencyKeyRequired, CommonRefusals.IdempotencyKeyReused, CommonRefusals.IdempotencyInProgress,
                CheckoutRefusals.InvalidAddress, CheckoutRefusals.NothingToShip, CheckoutRefusals.UnknownShippingOption,
                CheckoutRefusals.NoLongerOnSale, CheckoutRefusals.PriceChanged, CheckoutRefusals.PaymentDeclined,
                CheckoutRefusals.PaymentUnavailable, CheckoutRefusals.NotMandated, CheckoutRefusals.AgentRefused,
                CheckoutRefusals.InsufficientStock))
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
