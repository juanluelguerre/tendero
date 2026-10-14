using System.Diagnostics;
using System.Diagnostics.Metrics;
using Carter;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Ordering.Features.ManageCart;

/// <summary>
/// The cart, as it travels.
///
/// **There is not one figure of money on it**, and that is the design rather
/// than an omission. Prices are quoted live and frozen at order time (ADR 0016),
/// so everything monetary comes from `POST /api/pricing/quote`, recomputed after
/// every change. A cart endpoint that returned a total would be a second pricing
/// engine, and the day the two disagreed the shopper would be right and both
/// would be wrong.
///
/// It costs the storefront a second round trip per change. At six products that
/// is free, and it is what keeps `Ordering` from referencing `Pricing`.
/// </summary>
public sealed record CartLineView(
    string ProductId,
    string VariantId,
    string Sku,
    string ProductName,
    string? VariantLabel,
    string? ImageId,
    int Quantity,
    /// <summary>
    /// How many of it can be bought now — not held for this cart, and the till
    /// asks again: what a page needs to say *only one left* before paying does.
    /// </summary>
    int Available);

public sealed record CartView(
    string CartId,
    string Token,
    string Culture,
    string Currency,
    int ItemCount,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<CartLineView> Lines)
{
    /// <summary>
    /// What a browser with no token gets: a shape the interface can render
    /// without branching, rather than a 404 it has to special-case. There is no
    /// row behind it — asking to look at a basket must not create one, or every
    /// crawler leaves a cart behind.
    /// </summary>
    public static CartView Empty(string culture, string currency) =>
        new(string.Empty, string.Empty, culture, currency, 0, DateTimeOffset.MinValue, []);
}

public static class CartHeaders
{
    /// <summary>
    /// The cart token travels in a HEADER and never in the URL.
    ///
    /// It is a bearer credential: whoever holds it sees the basket. A path
    /// segment would put it in access logs, in `Referer` on every outbound link,
    /// and in whatever the shopper pastes into a chat window.
    /// </summary>
    public const string Token = "X-Cart-Token";
}

// ---------- Reading ----------

public sealed record GetCartQuery(string? Token, string Culture) : IQuery<CartView>;

public sealed class GetCartHandler(ICartRepository carts, IAvailabilityReader availability) : IQueryHandler<GetCartQuery, CartView>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<CartView> HandleAsync(GetCartQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("cart.get");

        var cart = query.Token is null ? null : await carts.FindOpenByTokenAsync(query.Token, cancellationToken);

        activity?.SetTag("cart.found", cart is not null);

        // A token whose cart was checked out yesterday is not an error: it is a
        // browser holding a stale string, and the answer is an empty basket
        // rather than the old order's contents.
        return cart is null
            ? CartView.Empty(query.Culture, "EUR")
            : await ViewAsync(cart, query.Culture, availability, cancellationToken);
    }

    /// <summary>
    /// The cart as it is read, with what the shelf has of each line now. Read
    /// at the moment of answering and promised to nobody: the hold is the
    /// order's, later (the stock saga), and the till asks again.
    /// </summary>
    internal static async Task<CartView> ViewAsync(
        Cart cart, string culture, IAvailabilityReader availability, CancellationToken cancellationToken) =>
        View(cart, culture, await availability.AvailableAsync([.. cart.Lines.Select(line => line.Sku)], cancellationToken));

    /// <summary>
    /// The cart in the language it is being read in. The lines keep every
    /// translation, so a basket filled in Spanish reads in English the moment
    /// the shopper asks in English — the same chain `LocalizedText.In` applies
    /// everywhere else: that culture, then English, then whatever there is.
    /// </summary>
    internal static CartView View(Cart cart, string culture, IReadOnlyDictionary<string, int> available) => new(
        cart.Id.ToString(),
        cart.Token,
        cart.Culture,
        cart.Currency,
        cart.ItemCount,
        cart.ExpiresAt,
        [
            .. cart.Lines.Select(line => new CartLineView(
                // Flat strings. ProductId and VariantId are record structs and
                // would serialise as {"value":"…"} — the mistake CLAUDE.md warns
                // no unit test will catch.
                line.ProductId.ToString(),
                line.VariantId.ToString(),
                line.Sku,
                line.ProductName.In(culture),
                line.VariantLabel?.In(culture),
                line.ImageId,
                line.Quantity,
                // An article the shelf does not know is one there is none of.
                Math.Max(0, available.GetValueOrDefault(line.Sku))))
        ]);
}

// ---------- Adding ----------

public sealed record AddToCartCommand(string? Token, string Sku, int Quantity, string Culture)
    : ICommand<CartOutcome>;

public enum CartResult
{
    Changed,

    /// <summary>The SKU is not on sale. A draft product, an archived one, or a
    /// typo — all the same answer to the caller, because "we found it but you
    /// may not buy it" tells an enumerator more than it tells a shopper.</summary>
    UnknownSku,

    /// <summary>The line is not in this cart.</summary>
    UnknownLine,

    /// <summary>A cart holds one currency, the same rule an order states. It is
    /// checked here so a shopper hears it while they can still act on it.</summary>
    MixedCurrencies,

    /// <summary>A cap was hit — too many lines, or too many of one thing.</summary>
    Refused
}

public sealed record CartOutcome(CartResult Result, CartView? Cart, string? Detail);

public sealed class AddToCartValidator : AbstractValidator<AddToCartCommand>
{
    public AddToCartValidator()
    {
        RuleFor(command => command.Sku).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Quantity)
            .InclusiveBetween(1, Cart.MaximumQuantity)
            .WithMessage($"A line takes between 1 and {Cart.MaximumQuantity} units.");
    }
}

public sealed class AddToCartHandler(
    ICartRepository carts, IPurchasableReader purchasables, IUnitOfWork unitOfWork, TimeProvider clock, IActorAccessor actors,
    IAvailabilityReader availability)
    : ICommandHandler<AddToCartCommand, CartOutcome>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<CartOutcome> HandleAsync(AddToCartCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("cart.add");
        activity?.SetTag("cart.sku", command.Sku);

        var purchasable = (await purchasables.FindBySkusAsync([command.Sku], cancellationToken))
            .GetValueOrDefault(command.Sku);

        if (purchasable is null)
            return new CartOutcome(CartResult.UnknownSku, null, command.Sku);

        var cart = command.Token is null
            ? null
            : await carts.FindOpenByTokenAsync(command.Token, cancellationToken);

        var started = cart is null;

        if (cart is null)
        {
            // The currency comes from what is being bought, not from the
            // request: a body that named its own currency would be a body that
            // chose the price list.
            cart = Cart.Start(clock, command.Culture, purchasable.Currency);
            carts.Add(cart);
        }
        else if (!string.Equals(cart.Currency, purchasable.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return new CartOutcome(
                CartResult.MixedCurrencies, await GetCartHandler.ViewAsync(cart, command.Culture, availability, cancellationToken),
                $"{cart.Currency} and {purchasable.Currency}");
        }

        try
        {
            cart.Add(clock, new CartLine(
                purchasable.ProductId,
                purchasable.VariantId,
                purchasable.Sku,
                purchasable.ProductName,
                purchasable.VariantLabel,
                purchasable.ImageId,
                command.Quantity,
                purchasable.CategoryCode,
                purchasable.CategoryPath));
        }
        catch (InvalidOperationException exception)
        {
            // The caps live on the aggregate so a second caller — an agent over
            // UCP — cannot forget them. Turning the refusal into an answer is
            // this handler's job.
            return new CartOutcome(CartResult.Refused, await GetCartHandler.ViewAsync(cart, command.Culture, availability, cancellationToken), exception.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Counted once it exists: a cart refused on its first line was never
        // saved, and is not a cart anybody abandoned.
        if (started)
            CartsCreated.Record(actors.Current);

        return new CartOutcome(CartResult.Changed, await GetCartHandler.ViewAsync(cart, command.Culture, availability, cancellationToken), null);
    }
}

/// <summary>
/// `tendero.carts.created` (the tracking plan). Against the orders placed it is
/// abandonment: nothing sets a cart to <see cref="CartStatus.Abandoned"/> yet —
/// the state and its transition exist, the sweep does not — so the difference
/// between the two counts is the measure.
/// </summary>
internal static class CartsCreated
{
    private static readonly Meter Meter = new(TelemetrySources.Ordering);

    private static readonly Counter<long> Created = Meter.CreateCounter<long>(
        "tendero.carts.created", unit: "{cart}", description: "Carts opened, by who opened them and through what.");

    public static void Record(Actor actor) => Created.Add(1, actor.KindLabel, actor.SurfaceLabel);
}

// ---------- Changing and removing ----------

/// <summary>
/// Sets a line's quantity. **Zero removes it**, which is what pressing "−" at
/// one unit means — asking the interface to call a different endpoint for the
/// last unit is how off-by-one bugs get written.
/// </summary>
public sealed record SetCartLineCommand(string? Token, string Sku, int Quantity, string Culture)
    : ICommand<CartOutcome>;

public sealed class SetCartLineValidator : AbstractValidator<SetCartLineCommand>
{
    public SetCartLineValidator()
    {
        RuleFor(command => command.Sku).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Quantity).InclusiveBetween(0, Cart.MaximumQuantity);
    }
}

public sealed class SetCartLineHandler(
    ICartRepository carts, IUnitOfWork unitOfWork, TimeProvider clock, IAvailabilityReader availability)
    : ICommandHandler<SetCartLineCommand, CartOutcome>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<CartOutcome> HandleAsync(SetCartLineCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("cart.set_line");
        activity?.SetTag("cart.sku", command.Sku);
        activity?.SetTag("cart.quantity", command.Quantity);

        var cart = command.Token is null
            ? null
            : await carts.FindOpenByTokenAsync(command.Token, cancellationToken);

        if (cart is null)
            return new CartOutcome(CartResult.UnknownLine, null, command.Sku);

        try
        {
            cart.SetQuantity(clock, command.Sku, command.Quantity);
        }
        catch (InvalidOperationException exception)
        {
            return new CartOutcome(CartResult.UnknownLine, await GetCartHandler.ViewAsync(cart, command.Culture, availability, cancellationToken), exception.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CartOutcome(CartResult.Changed, await GetCartHandler.ViewAsync(cart, command.Culture, availability, cancellationToken), null);
    }
}

// ---------- The wire ----------

public sealed record AddToCartRequest(string Sku, int Quantity = 1);

public sealed record SetCartLineRequest(int Quantity);

/// <summary>What the cart refuses, by code.</summary>
public static class CartRefusals
{
    public static readonly Refusal UnknownSku = new(
        "UnknownSku", 404, "Ese artículo no está a la venta.", "That item is not on sale.");

    public static readonly Refusal UnknownLine = new(
        "UnknownLine", 404, "Ese artículo no está en el carrito.", "That item is not in the cart.");

    public static readonly Refusal MixedCurrencies = new(
        "MixedCurrencies", 400, "Un carrito lleva una sola moneda.", "A cart carries one currency.");

    public static readonly Refusal Refused = new(
        "CartRefused", 400, "El carrito no admite ese cambio.", "The cart does not take that change.");

    public static readonly Refusal BelongsToSomebodyElse = new(
        "CartBelongsToSomebodyElse", 409, "Ese carrito es de otra cuenta.", "That basket belongs to another account.");
}

public sealed class CartEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var cart = app.MapGroup("/api/cart")
            // Anonymous by explicit decision, and it is the only shape a shop
            // can have: a basket exists before anybody says who they are, and
            // requiring a login to add to it is how a storefront loses the sale.
            // The token is the credential; phase 7 added the account behind it.
            .AllowAnonymous()
            .WithTags("Cart");

        cart.MapGet("/",
            async Task<Ok<CartView>> (
                   string? culture, HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                var view = await dispatcher.SendAsync(new GetCartQuery(Token(http), resolved), ct);

                Respond(http, resolved, view);

                return TypedResults.Ok(view);
            })
            .WithName("GetCart");

        cart.MapPost("/lines",
            async Task<Results<Ok<CartView>, ProblemHttpResult>> (
                   AddToCartRequest request, string? culture,
                   HttpContext http, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                var outcome = await dispatcher.SendAsync(
                    new AddToCartCommand(Token(http), request.Sku, request.Quantity, resolved), ct);

                return Answer(http, resolved, outcome);
            })
            .WithMetadata(Refusals)
            .WithName("AddToCart");

        cart.MapPut("/lines/{sku}",
            async Task<Results<Ok<CartView>, ProblemHttpResult>> (
                   string sku, SetCartLineRequest request, string? culture,
                   HttpContext http, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                var outcome = await dispatcher.SendAsync(
                    new SetCartLineCommand(Token(http), sku, request.Quantity, resolved), ct);

                return Answer(http, resolved, outcome);
            })
            .WithMetadata(Refusals)
            .WithName("SetCartLine");

        // DELETE is setting the quantity to zero, and it exists because that is
        // the verb a client reaches for. One handler, two spellings.
        cart.MapDelete("/lines/{sku}",
            async Task<Results<Ok<CartView>, ProblemHttpResult>> (
                   string sku, string? culture,
                   HttpContext http, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                var outcome = await dispatcher.SendAsync(
                    new SetCartLineCommand(Token(http), sku, 0, resolved), ct);

                return Answer(http, resolved, outcome);
            })
            .WithMetadata(Refusals)
            .WithName("RemoveCartLine");
    }

    /// <summary>What a change to the cart is refused for, by code: every one of the three operations can answer each.</summary>
    private static readonly Refuses Refusals = new(
        CartRefusals.UnknownSku, CartRefusals.UnknownLine, CartRefusals.MixedCurrencies, CartRefusals.Refused);

    private static string? Token(HttpContext http) =>
        http.Request.Headers.TryGetValue(CartHeaders.Token, out var values) &&
        !string.IsNullOrWhiteSpace(values.ToString())
            ? values.ToString()
            : null;

    private static Results<Ok<CartView>, ProblemHttpResult> Answer(
        HttpContext http, string culture, CartOutcome outcome)
    {
        if (outcome.Cart is not null)
            Respond(http, culture, outcome.Cart);

        return outcome.Result switch
        {
            CartResult.UnknownSku => TypedResults.Problem(
                statusCode: CartRefusals.UnknownSku.Status,
                extensions: CartRefusals.UnknownSku.Said(
                    es: $"No hay nada a la venta con el SKU «{outcome.Detail}».",
                    en: $"There is nothing on sale with SKU '{outcome.Detail}'.")),
            CartResult.UnknownLine => TypedResults.Problem(
                statusCode: CartRefusals.UnknownLine.Status,
                extensions: CartRefusals.UnknownLine.Said(
                    es: $"No hay ninguna línea de «{outcome.Detail}» en este carrito.",
                    en: $"There is no line for '{outcome.Detail}' in this cart.")),
            CartResult.MixedCurrencies => TypedResults.Problem(
                statusCode: CartRefusals.MixedCurrencies.Status,
                extensions: CartRefusals.MixedCurrencies.Said(
                    es: $"Este llevaría {outcome.Detail}.", en: $"This one would carry {outcome.Detail}.")),
            // The cart's own caps, said by the aggregate: its sentence is the
            // reason, and the refusal's the one a screen shows.
            CartResult.Refused => TypedResults.Problem(
                statusCode: CartRefusals.Refused.Status, extensions: CartRefusals.Refused.Said(reason: outcome.Detail)),
            _ => TypedResults.Ok(outcome.Cart!)
        };
    }

    /// <summary>
    /// The token comes back in the SAME header it goes out in, so a client
    /// stores whatever the last response said rather than having to know when a
    /// cart was created. It is echoed on every answer and not only on the first,
    /// because "the response that created it" is a distinction a retry destroys.
    /// </summary>
    private static void Respond(HttpContext http, string culture, CartView cart)
    {
        if (!string.IsNullOrEmpty(cart.Token))
            http.Response.Headers[CartHeaders.Token] = cart.Token;

        http.Response.Headers.ContentLanguage = culture;
        http.Response.Headers.Vary = "Accept-Language";
    }
}
