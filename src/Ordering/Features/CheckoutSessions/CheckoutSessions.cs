using System.Diagnostics;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Ordering.Features.CheckoutSessions;

// UCP's checkout (P11-1), in Ordering's own vocabulary. Ucp maps these views to
// the protocol's wire and back; nothing here knows a JSONPath or a status string.
// Only an agent opens a session, and to any other principal a session does not
// exist.

// ---------- What comes in ----------

public sealed record CheckoutLineInput(string Sku, int Quantity);

/// <summary>Who is buying, as the agent said — the slice's own contract, so a transport never touches the domain's.</summary>
public sealed record CheckoutBuyerDetails(string? FirstName, string? LastName, string? Email, string? Phone)
{
    public static readonly CheckoutBuyerDetails Nobody = new(null, null, null, null);

    internal CheckoutBuyer ToDomain() => new(FirstName, LastName, Email, Phone);

    internal static CheckoutBuyerDetails From(CheckoutBuyer buyer) => new(buyer.FirstName, buyer.LastName, buyer.Email, buyer.Phone);
}

/// <summary>What an agent says about the purchase beyond its lines — replaced whole on every update.</summary>
public sealed record CheckoutDetails(CheckoutBuyerDetails Buyer, Address? Destination, string? ShippingOptionCode, IReadOnlyList<string> Coupons)
{
    public static readonly CheckoutDetails None = new(CheckoutBuyerDetails.Nobody, null, null, []);
}

public sealed record OpenCheckoutSessionCommand(IReadOnlyList<CheckoutLineInput> Lines, CheckoutDetails Details, string Culture)
    : ICommand<CheckoutSessionResult>;

public sealed record UpdateCheckoutSessionCommand(CheckoutSessionId SessionId, IReadOnlyList<CheckoutLineInput> Lines, CheckoutDetails Details, string Culture)
    : ICommand<CheckoutSessionResult>;

public sealed record GetCheckoutSessionQuery(CheckoutSessionId SessionId, string Culture) : IQuery<CheckoutSessionResult>;

/// <param name="Instrument">What the payment handler's credential carries — for the sandbox handler, one of the fake provider's instruments.</param>
/// <param name="Mandate">What the agent's verified mandate approved (P11-5), when AP2 was negotiated; frozen onto the order.</param>
public sealed record CompleteCheckoutSessionCommand(CheckoutSessionId SessionId, string Instrument, string IdempotencyKey, string Culture, MandateScope? Mandate = null)
    : ICommand<CheckoutSessionResult>;

public sealed record CancelCheckoutSessionCommand(CheckoutSessionId SessionId, string Culture) : ICommand<CheckoutSessionResult>;

// ---------- What goes out ----------

/// <summary>UCP's six states, derived — only the last two are stored (<see cref="CheckoutSessionStatus"/>).</summary>
public enum CheckoutState
{
    Incomplete,
    ReadyForComplete,

    /// <summary>Everything is there, and the shop cannot settle this principal's payment: a person finishes it on the storefront.</summary>
    RequiresEscalation,

    Completed,
    Canceled,
}

/// <summary>
/// Something the agent can act on, named by what it is about rather than where it
/// sits on any wire: <c>buyer.email</c>, <c>line:SKU</c>, <c>coupon:CODE</c>,
/// <c>fulfillment.destination</c>, <c>fulfillment.option</c>, <c>payment</c>, <c>totals</c>.
/// </summary>
public sealed record CheckoutIssue(string Code, string Target, string? Detail = null);

public static class CheckoutIssueCodes
{
    public const string Missing = "missing";
    public const string ItemUnavailable = "item_unavailable";
    public const string QuantityAdjusted = "quantity_adjusted";
    public const string AddressUndeliverable = "address_undeliverable";
    public const string ShippingOptionUnavailable = "fulfillment_option_unavailable";
    public const string DiscountCodeInvalid = "discount_code_invalid";
    public const string PriceChanged = "price_changed";
    public const string PaymentFailed = "payment_failed";
    public const string PaymentUnavailable = "payment_unavailable";

    /// <summary>The shop settles an agent's live payment nowhere yet: a person pays on the storefront (ADR 0038).</summary>
    public const string PaymentNotSettled = "payment_not_settled";

    /// <summary>
    /// The total is not what the mandate approved, or the mandate ended (P11-5).
    /// Spelled as UCP's AP2 code, because it is that failure seen from the till.
    /// </summary>
    public const string MandateScopeMismatch = "mandate_scope_mismatch";

    /// <summary>One of the shop's own rules for agents refused it (P11-6); the detail names the rule.</summary>
    public const string AgentRefused = "agent_refused";
}

public sealed record CheckoutLineView(string Sku, string Title, string? VariantLabel, string? ImageId, int Quantity, decimal UnitPrice, decimal Total);

public sealed record CheckoutTaxView(decimal Rate, decimal Base, decimal Amount);

public sealed record CheckoutTotalsView(decimal Subtotal, decimal DiscountTotal, decimal Shipping, decimal TaxTotal, decimal Total, IReadOnlyList<CheckoutTaxView> Taxes);

public sealed record CheckoutShippingOptionView(string Code, string Label, decimal Amount, int? EstimatedDays);

public sealed record CheckoutDiscountView(string Code, string Label, decimal Amount);

public sealed record CheckoutSessionView(
    string Id,
    CheckoutState State,
    string Mode,
    string Currency,
    string Culture,
    IReadOnlyList<CheckoutLineView> Lines,
    CheckoutTotalsView? Totals,
    CheckoutBuyerDetails Buyer,
    Address? Destination,
    IReadOnlyList<CheckoutShippingOptionView> ShippingOptions,
    string? SelectedShippingOption,
    IReadOnlyList<string> Coupons,
    IReadOnlyList<CheckoutDiscountView> Discounts,
    IReadOnlyList<CheckoutIssue> Issues,
    /// <summary>Whether the shop can take this principal's payment itself — the sandbox handler for a test credential.</summary>
    bool CanSettle,
    string? OrderId,
    DateTimeOffset ExpiresAt);

public enum CheckoutSessionOutcome
{
    Ok,

    /// <summary>Only an agent opens or reads a session.</summary>
    NotAnAgent,

    /// <summary>No session with that id belongs to this agent.</summary>
    NotFound,

    /// <summary>Completed, canceled or expired: nothing left to change.</summary>
    NotOpen,

    /// <summary>Not one line could be bought: there is no session to create.</summary>
    NothingPurchasable,

    /// <summary>Completion was asked of a session that is not ready; the view says why.</summary>
    NotReady,

    /// <summary>Completion was attempted and did not place an order — declined, price moved; the view says why.</summary>
    NotPlaced,
}

public sealed record CheckoutSessionResult(CheckoutSessionOutcome Outcome, CheckoutSessionView? Session, IReadOnlyList<CheckoutIssue>? Issues = null)
    : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        CheckoutSessionOutcome.Ok => AuditOutcome.Allowed,
        _ => AuditOutcome.Denied,
    };

    /// <summary>
    /// The issue that stopped it when there is one — which rule, which numbers — so
    /// the audit row of a refused completion says why, not only that (P11-6).
    /// </summary>
    string? IAuditedResult.AuditReason => Outcome == CheckoutSessionOutcome.Ok
        ? null
        : (Issues?.LastOrDefault() ?? Session?.Issues.LastOrDefault()) is { } issue
            ? $"{issue.Code}: {issue.Detail ?? issue.Target}"
            : Outcome.ToString();
}

public sealed class CheckoutLineInputValidator : AbstractValidator<CheckoutLineInput>
{
    public CheckoutLineInputValidator()
    {
        RuleFor(line => line.Sku).NotEmpty().MaximumLength(100);
        RuleFor(line => line.Quantity).GreaterThan(0);
    }
}

public sealed class OpenCheckoutSessionValidator : AbstractValidator<OpenCheckoutSessionCommand>
{
    public OpenCheckoutSessionValidator()
    {
        RuleFor(command => command.Lines).NotEmpty();
        RuleForEach(command => command.Lines).SetValidator(new CheckoutLineInputValidator());
    }
}

public sealed class UpdateCheckoutSessionValidator : AbstractValidator<UpdateCheckoutSessionCommand>
{
    public UpdateCheckoutSessionValidator() =>
        RuleForEach(command => command.Lines).SetValidator(new CheckoutLineInputValidator());
}

public sealed class CompleteCheckoutSessionValidator : AbstractValidator<CompleteCheckoutSessionCommand>
{
    public CompleteCheckoutSessionValidator()
    {
        RuleFor(command => command.Instrument).NotEmpty().MaximumLength(100);
        RuleFor(command => command.IdempotencyKey).NotEmpty().MaximumLength(200);
    }
}

// ---------- The handlers ----------

/// <summary>
/// The five operations over one session. One handler because they share the
/// whole of what matters — finding the session for its owner, turning lines into
/// cart lines, pricing, deriving the state — and differ in a few lines each.
/// </summary>
public sealed class CheckoutSessionsHandler(
    ICheckoutSessionRepository sessions,
    ICartRepository carts,
    IOrderRepository orders,
    IPurchasableReader purchasables,
    ICartPricer pricer,
    OrderPlacement placement,
    IPrincipalAccessor principals,
    IActorAccessor actors,
    IUnitOfWork unitOfWork,
    IOptions<CheckoutOptions> options,
    TimeProvider clock) :
    ICommandHandler<OpenCheckoutSessionCommand, CheckoutSessionResult>,
    ICommandHandler<UpdateCheckoutSessionCommand, CheckoutSessionResult>,
    IQueryHandler<GetCheckoutSessionQuery, CheckoutSessionResult>,
    ICommandHandler<CompleteCheckoutSessionCommand, CheckoutSessionResult>,
    ICommandHandler<CancelCheckoutSessionCommand, CheckoutSessionResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<CheckoutSessionResult> HandleAsync(OpenCheckoutSessionCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ucp.checkout.create");
        var principal = principals.Current;
        if (principal.Agent is not { } agent)
            return new CheckoutSessionResult(CheckoutSessionOutcome.NotAnAgent, null);

        var (lines, currency, issues) = await CartLinesAsync(command.Lines, cancellationToken);
        if (lines.Count == 0)
            return new CheckoutSessionResult(CheckoutSessionOutcome.NothingPurchasable, null, issues);

        var cart = Cart.Start(clock, command.Culture, currency!, principal.Customer);
        cart.ReplaceLines(clock, lines);
        carts.Add(cart);

        // The mode is the credential's, fixed now, like the order's (ADR 0038).
        var session = CheckoutSession.Open(clock, agent, cart.Id, OrderModes.Of(principal), command.Culture);
        session.Describe(clock, command.Details.Buyer.ToDomain(), command.Details.Destination, command.Details.ShippingOptionCode, command.Details.Coupons, command.Culture);
        sessions.Add(session);

        var view = await ViewAsync(session, cart, issues, show: true, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("ucp.checkout.id", session.Id.ToString());
        activity?.SetTag("ucp.checkout.state", view.State.ToString());
        return new CheckoutSessionResult(CheckoutSessionOutcome.Ok, view);
    }

    public async Task<CheckoutSessionResult> HandleAsync(UpdateCheckoutSessionCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ucp.checkout.update");
        var (outcome, session, cart) = await OpenSessionAsync(command.SessionId, cancellationToken);
        if (outcome is not null)
            return outcome;

        var (lines, _, issues) = await CartLinesAsync(command.Lines, cancellationToken);
        cart!.ReplaceLines(clock, lines);
        session!.Describe(clock, command.Details.Buyer.ToDomain(), command.Details.Destination, command.Details.ShippingOptionCode, command.Details.Coupons, command.Culture);

        var view = await ViewAsync(session, cart, issues, show: true, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("ucp.checkout.state", view.State.ToString());
        return new CheckoutSessionResult(CheckoutSessionOutcome.Ok, view);
    }

    /// <summary>A read, and nothing else: it prices what it shows and remembers none of it.</summary>
    public async Task<CheckoutSessionResult> HandleAsync(GetCheckoutSessionQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ucp.checkout.get");
        var (session, cart) = await FindAsync(query.SessionId, cancellationToken);
        if (session is null || cart is null)
            return new CheckoutSessionResult(principals.Current.IsAgent ? CheckoutSessionOutcome.NotFound : CheckoutSessionOutcome.NotAnAgent, null);

        return new CheckoutSessionResult(CheckoutSessionOutcome.Ok, await ViewAsync(session, cart, [], show: false, cancellationToken));
    }

    public async Task<CheckoutSessionResult> HandleAsync(CompleteCheckoutSessionCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ucp.checkout.complete");
        var (session, cart) = await FindAsync(command.SessionId, cancellationToken);
        if (session is null || cart is null)
            return new CheckoutSessionResult(principals.Current.IsAgent ? CheckoutSessionOutcome.NotFound : CheckoutSessionOutcome.NotAnAgent, null);

        // A retry of a completion that went through: the same answer, nothing twice.
        if (session.Status == CheckoutSessionStatus.Completed)
            return new CheckoutSessionResult(CheckoutSessionOutcome.Ok, await ViewAsync(session, cart, [], show: false, cancellationToken));

        if (session.Status != CheckoutSessionStatus.Open || session.HasExpiredAt(clock.GetUtcNow()))
            return new CheckoutSessionResult(CheckoutSessionOutcome.NotOpen, await ViewAsync(session, cart, [], show: false, cancellationToken));

        var ready = await ViewAsync(session, cart, [], show: false, cancellationToken);
        if (ready.State != CheckoutState.ReadyForComplete)
            return new CheckoutSessionResult(CheckoutSessionOutcome.NotReady, ready);

        var result = await placement.PlaceAsync(
            cart,
            new PlacementRequest(
                session.Destination!,
                null,
                session.ShippingOptionCode!,
                // Held to what was last shown (ADR 0016): UCP's complete carries no
                // price, so the session remembers the fingerprint for the agent.
                session.ShownQuoteHash,
                command.Instrument,
                command.IdempotencyKey,
                session.Coupons,
                session.Culture,
                session.Buyer.Email,
                command.Mandate),
            session.Mode,
            cancellationToken);
        CheckoutAttempts.Record(result, actors.Current, session.Mode, principals.Current.Standing);

        switch (result.Outcome)
        {
            case PlaceOrderOutcome.Placed:
                session.Complete(clock, result.Order!.Id);
                // One commit: the order, the closed cart, the completed session.
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return new CheckoutSessionResult(CheckoutSessionOutcome.Ok, await ViewAsync(session, cart, [], show: false, cancellationToken));

            case PlaceOrderOutcome.PriceChanged:
                // Nothing charged. The new price is what the agent is shown now, and
                // what the next completion is held to.
                var moved = await ViewAsync(session, cart, [new(CheckoutIssueCodes.PriceChanged, "totals")], show: true, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return new CheckoutSessionResult(CheckoutSessionOutcome.NotPlaced, moved);

            default:
                var issue = result.Outcome switch
                {
                    PlaceOrderOutcome.PaymentDeclined => new CheckoutIssue(CheckoutIssueCodes.PaymentFailed, "payment", result.Detail),
                    PlaceOrderOutcome.PaymentUnavailable => new CheckoutIssue(CheckoutIssueCodes.PaymentUnavailable, "payment", result.Detail),
                    PlaceOrderOutcome.UnknownShippingOption => new CheckoutIssue(CheckoutIssueCodes.ShippingOptionUnavailable, "fulfillment.option", result.Detail),
                    PlaceOrderOutcome.NotMandated => new CheckoutIssue(CheckoutIssueCodes.MandateScopeMismatch, "payment", result.Detail),
                    PlaceOrderOutcome.AgentRefused => new CheckoutIssue(CheckoutIssueCodes.AgentRefused, "payment", result.Detail),
                    _ => new CheckoutIssue(CheckoutIssueCodes.ItemUnavailable, "lines", result.Detail),
                };
                return new CheckoutSessionResult(CheckoutSessionOutcome.NotPlaced, await ViewAsync(session, cart, [issue], show: false, cancellationToken));
        }
    }

    public async Task<CheckoutSessionResult> HandleAsync(CancelCheckoutSessionCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ucp.checkout.cancel");
        var (outcome, session, cart) = await OpenSessionAsync(command.SessionId, cancellationToken);
        if (outcome is not null)
            return outcome;

        session!.Cancel(clock);
        cart!.Abandon(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CheckoutSessionResult(CheckoutSessionOutcome.Ok, await ViewAsync(session, cart, [], show: false, cancellationToken));
    }

    // ---------- Shared ----------

    /// <summary>The session and its cart, if both exist and the session is this agent's; nothing otherwise, so an id reveals nothing.</summary>
    private async Task<(CheckoutSession? Session, Cart? Cart)> FindAsync(CheckoutSessionId id, CancellationToken cancellationToken)
    {
        var principal = principals.Current;
        if (!principal.IsAgent)
            return (null, null);

        var session = await sessions.FindByIdAsync(id, cancellationToken);
        if (session is null || !session.BelongsTo(principal))
            return (null, null);

        return (session, await carts.FindByIdAsync(session.CartId, cancellationToken));
    }

    /// <summary>A session that can still change, or the answer that says why not.</summary>
    private async Task<(CheckoutSessionResult? Refusal, CheckoutSession? Session, Cart? Cart)> OpenSessionAsync(CheckoutSessionId id, CancellationToken cancellationToken)
    {
        var (session, cart) = await FindAsync(id, cancellationToken);
        if (session is null || cart is null)
            return (new CheckoutSessionResult(principals.Current.IsAgent ? CheckoutSessionOutcome.NotFound : CheckoutSessionOutcome.NotAnAgent, null), null, null);

        if (session.Status != CheckoutSessionStatus.Open || session.HasExpiredAt(clock.GetUtcNow()))
            return (new CheckoutSessionResult(CheckoutSessionOutcome.NotOpen, await ViewAsync(session, cart, [], show: false, cancellationToken)), null, null);

        return (null, session, cart);
    }

    /// <summary>
    /// The lines an agent asked for, as cart lines: an unknown SKU is dropped and
    /// said, a quantity over the cart's limit is lowered and said — never silently
    /// either way.
    /// </summary>
    private async Task<(IReadOnlyList<CartLine> Lines, string? Currency, List<CheckoutIssue> Issues)> CartLinesAsync(
        IReadOnlyList<CheckoutLineInput> requested, CancellationToken cancellationToken)
    {
        List<CheckoutIssue> issues = [];
        var known = await purchasables.FindBySkusAsync([.. requested.Select(line => line.Sku)], cancellationToken);
        List<CartLine> lines = [];

        foreach (var line in requested)
        {
            if (!known.TryGetValue(line.Sku, out var purchasable))
            {
                issues.Add(new CheckoutIssue(CheckoutIssueCodes.ItemUnavailable, $"line:{line.Sku}"));
                continue;
            }

            var quantity = Math.Min(line.Quantity, Cart.MaximumQuantity);
            if (quantity != line.Quantity)
                issues.Add(new CheckoutIssue(CheckoutIssueCodes.QuantityAdjusted, $"line:{line.Sku}", $"{Cart.MaximumQuantity}"));

            lines.Add(new CartLine(
                purchasable.ProductId, purchasable.VariantId, purchasable.Sku,
                purchasable.ProductName, purchasable.VariantLabel, purchasable.ImageId, quantity));
        }

        return (lines, known.Values.FirstOrDefault()?.Currency, issues);
    }

    /// <summary>
    /// The session as the agent sees it now: priced, rated, and its state derived
    /// from what it lacks and what the shop can settle. With <paramref name="show"/>,
    /// the quote's fingerprint is remembered — only a create, an update or a moved
    /// price shows a price; a read remembers nothing.
    /// </summary>
    private async Task<CheckoutSessionView> ViewAsync(
        CheckoutSession session, Cart cart, IReadOnlyList<CheckoutIssue> issues, bool show, CancellationToken cancellationToken)
    {
        // A completed checkout shows what its order froze (ADR 0016), not a fresh
        // quote of a cart that is no longer being bought — found on the stack: the
        // first version re-priced it, with no shipping, after the order existed.
        if (session is { Status: CheckoutSessionStatus.Completed, OrderId: { } orderId }
            && await orders.FindByIdAsync(orderId, cancellationToken) is { } order)
            return Completed(session, order);

        List<CheckoutIssue> found = [.. issues];

        IReadOnlyList<ShippingOption> rates = [];
        ShippingOption? chosen = null;
        if (session.Destination is { } destination && !cart.IsEmpty && session.Status == CheckoutSessionStatus.Open)
        {
            rates = await placement.RateAsync(cart, destination, cancellationToken);
            if (rates.Count == 0)
                found.Add(new CheckoutIssue(CheckoutIssueCodes.AddressUndeliverable, "fulfillment.destination"));

            chosen = rates.FirstOrDefault(rate => string.Equals(rate.Code, session.ShippingOptionCode, StringComparison.OrdinalIgnoreCase));
            if (session.ShippingOptionCode is { } asked && chosen is null && rates.Count > 0)
                found.Add(new CheckoutIssue(CheckoutIssueCodes.ShippingOptionUnavailable, "fulfillment.option", asked));
        }

        CheckoutTotalsView? totals = null;
        IReadOnlyList<CheckoutLineView> lines = [.. cart.Lines.Select(line => new CheckoutLineView(
            line.Sku, line.ProductName.In(session.Culture), line.VariantLabel?.In(session.Culture), line.ImageId, line.Quantity, 0m, 0m))];
        IReadOnlyList<CheckoutDiscountView> discounts = [];

        if (!cart.IsEmpty)
        {
            var pricing = await pricer.QuoteAsync(
                cart.PricingLines(), session.Culture, chosen?.Amount ?? Money.Zero(cart.Currency), session.Coupons, cancellationToken);

            if (pricing.Outcome == PricingOutcome.Quoted)
            {
                var prices = pricing.Lines.ToDictionary(line => line.Sku, StringComparer.OrdinalIgnoreCase);
                lines = [.. lines.Select(line => prices.TryGetValue(line.Sku, out var priced)
                    ? line with { UnitPrice = priced.UnitPrice.Amount, Total = priced.UnitPrice.Amount * line.Quantity }
                    : line)];
                totals = new CheckoutTotalsView(
                    pricing.Totals.Subtotal.Amount, pricing.Totals.DiscountTotal.Amount, pricing.Totals.Shipping.Amount,
                    pricing.Totals.TaxTotal.Amount, pricing.Totals.Total.Amount,
                    [.. pricing.Taxes.Select(tax => new CheckoutTaxView(tax.Rate, tax.Base.Amount, tax.Amount.Amount))]);
                discounts = [.. pricing.Discounts.Select(discount => new CheckoutDiscountView(discount.PromotionCode, discount.Label, discount.Amount.Amount))];

                // A submitted code that nothing applied is said, without saying why:
                // the engine has no coupon finder, and a rejection that named the rule
                // would be one (the shop's own choice, kept).
                foreach (var code in session.Coupons.Where(code => !pricing.Discounts.Any(discount =>
                             string.Equals(discount.PromotionCode, code, StringComparison.OrdinalIgnoreCase))))
                    found.Add(new CheckoutIssue(CheckoutIssueCodes.DiscountCodeInvalid, $"coupon:{code}"));

                if (show && session.Status == CheckoutSessionStatus.Open)
                    session.Showed(pricing.InputHash);
            }
            else
            {
                found.AddRange(pricing.Offending.Select(sku => new CheckoutIssue(CheckoutIssueCodes.ItemUnavailable, $"line:{sku}")));
            }
        }

        // What is missing, as issues the agent can fill.
        foreach (var gap in session.Gaps(!cart.IsEmpty))
        {
            found.Add(gap switch
            {
                CheckoutGap.Lines => new CheckoutIssue(CheckoutIssueCodes.Missing, "lines"),
                CheckoutGap.BuyerEmail => new CheckoutIssue(CheckoutIssueCodes.Missing, "buyer.email"),
                CheckoutGap.Destination => new CheckoutIssue(CheckoutIssueCodes.Missing, "fulfillment.destination"),
                _ => new CheckoutIssue(CheckoutIssueCodes.Missing, "fulfillment.option"),
            });
        }

        // The shop takes a payment itself only when it is a rehearsal, until an
        // agent's live payment has somewhere real to go (ADR 0038, P11-1).
        var canSettle = session.Mode == OrderMode.Test || options.Value.AgentsPayLive;

        var state = session.Status switch
        {
            CheckoutSessionStatus.Completed => CheckoutState.Completed,
            CheckoutSessionStatus.Canceled => CheckoutState.Canceled,
            // A cart that cannot be priced cannot be bought, whatever else it lacks.
            _ when found.Any(Blocks) || (!cart.IsEmpty && totals is null) => CheckoutState.Incomplete,
            _ when !canSettle => CheckoutState.RequiresEscalation,
            _ => CheckoutState.ReadyForComplete,
        };
        if (state == CheckoutState.RequiresEscalation)
            found.Add(new CheckoutIssue(CheckoutIssueCodes.PaymentNotSettled, "payment"));

        return new CheckoutSessionView(
            session.Id.ToString(),
            state,
            MetricLabels.Of(session.Mode),
            cart.Currency,
            session.Culture,
            lines,
            totals,
            CheckoutBuyerDetails.From(session.Buyer),
            session.Destination,
            [.. rates.Select(rate => new CheckoutShippingOptionView(rate.Code, rate.Label.In(session.Culture), rate.Amount.Amount, rate.EstimatedDays))],
            chosen?.Code,
            session.Coupons,
            discounts,
            found,
            canSettle,
            session.OrderId?.ToString(),
            session.ExpiresAt);
    }

    private static CheckoutSessionView Completed(CheckoutSession session, Order order) => new(
        session.Id.ToString(),
        CheckoutState.Completed,
        MetricLabels.Of(session.Mode),
        order.Currency,
        session.Culture,
        [.. order.Lines.Select(line => new CheckoutLineView(line.Sku, line.ProductName, line.VariantLabel, line.ImageId, line.Quantity, line.UnitPrice.Amount, line.Total.Amount))],
        new CheckoutTotalsView(
            order.Totals.Subtotal.Amount, order.Totals.DiscountTotal.Amount, order.Totals.Shipping.Amount,
            order.Totals.TaxTotal.Amount, order.Totals.Total.Amount,
            [.. order.Taxes.Select(tax => new CheckoutTaxView(tax.Rate, tax.Base.Amount, tax.Amount.Amount))]),
        CheckoutBuyerDetails.From(session.Buyer),
        order.ShippingAddress,
        [new CheckoutShippingOptionView(order.Shipping.OptionCode, order.Shipping.Label, order.Shipping.Amount.Amount, order.Shipping.EstimatedDays)],
        order.Shipping.OptionCode,
        session.Coupons,
        [.. order.Discounts.Select(discount => new CheckoutDiscountView(discount.PromotionCode, discount.Label, discount.Amount.Amount))],
        [],
        true,
        order.Id.ToString(),
        session.ExpiresAt);

    /// <summary>
    /// Whether an issue keeps the session from completing. A rejected discount
    /// code, a lowered quantity or a SKU dropped from the request is said, and
    /// does not: the session goes on without it.
    /// </summary>
    private static bool Blocks(CheckoutIssue issue) =>
        issue.Code is not (CheckoutIssueCodes.DiscountCodeInvalid or CheckoutIssueCodes.QuantityAdjusted or CheckoutIssueCodes.ItemUnavailable);
}
