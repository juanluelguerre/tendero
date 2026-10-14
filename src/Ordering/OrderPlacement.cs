using System.Diagnostics;
using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Ordering.Contracts;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Ordering;

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
    PaymentUnavailable,

    /// <summary>
    /// The total is not what the agent's mandate approved, or the mandate ended
    /// (P11-5). Nothing charged: the person approves these terms or none.
    /// </summary>
    NotMandated,

    /// <summary>
    /// One of the shop's own rules for agents said no (P11-6): the detail names the
    /// rule and the numbers. Nothing charged, and the denial is audited.
    /// </summary>
    AgentRefused,

    /// <summary>
    /// The cart asks for more of something than there is, and the shop knew it
    /// at the till. Nothing charged and nothing placed; <see
    /// cref="PlaceOrderResult.Shortages"/> says which and by how many. The stock
    /// saga still holds stock after the order exists — that is for the race,
    /// two shoppers and the last unit, which no check made here can see.
    /// </summary>
    InsufficientStock
}

/// <summary>An article the cart asks for more of than can be bought now.</summary>
public sealed record StockShortage(string Sku, int Requested, int Available);

public sealed record PlaceOrderResult(
    PlaceOrderOutcome Outcome,
    Order? Order,
    CartPricing? Pricing,
    string? Detail,
    IReadOnlyList<StockShortage>? Shortages = null) : IAuditedResult
{
    /// <summary>A declined card is the shop saying no, which is a denial; a
    /// provider that did not answer is a fault. Each refusal carries its words,
    /// or its name when it has none.</summary>
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        PlaceOrderOutcome.Placed or PlaceOrderOutcome.AlreadyPlaced => AuditOutcome.Allowed,
        PlaceOrderOutcome.PaymentUnavailable => AuditOutcome.Failed,
        PlaceOrderOutcome.NothingToShip or PlaceOrderOutcome.UnknownShippingOption or
            PlaceOrderOutcome.UnknownSkus or PlaceOrderOutcome.PriceChanged or
            PlaceOrderOutcome.PaymentDeclined or PlaceOrderOutcome.NotMandated or
            PlaceOrderOutcome.AgentRefused or PlaceOrderOutcome.InsufficientStock => AuditOutcome.Denied
    };

    string? IAuditedResult.AuditReason =>
        Outcome is PlaceOrderOutcome.Placed or PlaceOrderOutcome.AlreadyPlaced ? null : Detail ?? Outcome.ToString();
}

/// <summary>Everything placing an order needs beyond the cart: where, how, at what price, paid how, and under which key.</summary>
public sealed record PlacementRequest(
    Address ShippingAddress,
    Address? BillingAddress,
    string ShippingOptionCode,
    string? ExpectedQuoteHash,
    string Instrument,
    string IdempotencyKey,
    IReadOnlyList<string> Coupons,
    string Culture,
    string? ContactEmail = null,
    MandateScope? Mandate = null);

/// <summary>
/// The moment the shop commits, shared by every way into it: the storefront's
/// checkout and UCP's (P11-1). Not a slice — two slices place orders, and shared
/// behaviour goes down or out rather than across (invariant 2), the same call
/// <see cref="OrderAccess"/> makes.
///
/// Seven things happen and the order matters. The shipping option is rated by the
/// call that offered it; the quote is recomputed and its fingerprint compared
/// (ADR 0016); the shelf is asked whether there is that much; the money is
/// authorised **before the order exists** (ADR 0025); the order is placed with
/// everything frozen onto it; and the cart is closed.
///
/// **The caller commits**, so whatever else it changes lands in the same
/// transaction as the order and the events that drive the rest — UCP completes a
/// checkout session beside the order it placed, and a session marked completed
/// without its order, or an order without its session, cannot happen.
/// </summary>
public sealed class OrderPlacement(
    IOrderRepository orders,
    ICartPricer pricer,
    IShippingRateProviderRegistry shipping,
    IPaymentProviderRegistry payments,
    IPrincipalAccessor principal,
    IActorAccessor actor,
    IAgentPolicy agentPolicy,
    IAgentActivity agentActivity,
    IOptions<CheckoutOptions> options,
    TimeProvider clock,
    IAvailabilityReader availability)
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    /// <summary>
    /// What it costs to get this cart to that address — shared with placing, so
    /// the option a shopper picked is priced by exactly the call that offered it.
    ///
    /// The subtotal is the raw line total and not the discounted one, which is a
    /// simplification worth naming: "free over 50 €" is measured before
    /// promotions, so a coupon cannot buy free shipping by accident.
    /// </summary>
    public async Task<IReadOnlyList<ShippingOption>> RateAsync(Cart cart, Address destination, CancellationToken cancellationToken)
    {
        var provider = shipping.Get(options.Value.ShippingProvider);

        return await provider.QuoteAsync(
            new ShippingQuoteRequest(
                destination,
                [.. cart.Lines.Select(line => new ShippableLine(line.Sku, line.Quantity))],
                // The cart holds no money (ADR 0016), so the only subtotal
                // available without a second pricing call is a count-based
                // stand-in. It is zero, deliberately: a free-shipping threshold
                // that fired on a number nobody computed would be worse than one
                // that never fires.
                Money.Zero(cart.Currency)),
            cancellationToken);
    }

    /// <summary>An order already placed under this key: with agents, a retry is the normal case rather than the rare one.</summary>
    public Task<Order?> AlreadyPlacedAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        orders.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);

    /// <summary>Places the order for an open, non-empty cart. The caller finds the cart, checks the key first, and saves.</summary>
    public async Task<PlaceOrderResult> PlaceAsync(
        Cart cart, PlacementRequest request, OrderMode mode, CancellationToken cancellationToken)
    {
        var activity = Activity.Current;

        // 1 · Shipping, from the same call that offered the options.
        var rates = await RateAsync(cart, request.ShippingAddress, cancellationToken);

        var option = rates.FirstOrDefault(rate =>
            string.Equals(rate.Code, request.ShippingOptionCode, StringComparison.OrdinalIgnoreCase));

        if (option is null)
            return Refuse(activity, PlaceOrderOutcome.UnknownShippingOption, request.ShippingOptionCode);

        // 2 · The price, recomputed by the engine that issued the quote.
        var pricing = await pricer.QuoteAsync(
            cart.PricingLines(), request.Culture, option.Amount, request.Coupons, cancellationToken);

        if (pricing.Outcome != PricingOutcome.Quoted)
            return Refuse(activity, PlaceOrderOutcome.UnknownSkus, string.Join(", ", pricing.Offending));

        // 3 · Is it still what the shopper was shown? A missing expectation is
        // treated as a mismatch rather than waved through: an agent that does
        // not send the hash has not agreed to a price.
        if (!string.Equals(pricing.InputHash, request.ExpectedQuoteHash, StringComparison.Ordinal))
        {
            activity?.SetTag("checkout.outcome", "price_changed");
            return new PlaceOrderResult(PlaceOrderOutcome.PriceChanged, null, pricing, pricing.InputHash);
        }

        // 3b · Within what the person approved, when an agent carries a mandate
        // (P11-5). Checked here, after the final price and before any money, in
        // the transaction that places the order — so an update that slipped in
        // after the protocol layer verified the mandate is caught all the same.
        if (request.Mandate is { } mandate && !mandate.Covers(pricing.Totals.Total, clock.GetUtcNow()))
        {
            activity?.SetTag("checkout.outcome", "not_mandated");
            return new PlaceOrderResult(PlaceOrderOutcome.NotMandated, null, pricing,
                $"approved {mandate.ApprovedTotal.Amount} {mandate.ApprovedTotal.Currency}, now {pricing.Totals.Total.Amount} {pricing.Totals.Total.Currency}");
        }

        // 3c · The shop's own rules for agents (P11-6), on the final total and after
        // the person's mandate: whatever the person allows, the shop decides whom it
        // serves and how much. A person's order never reaches them.
        if (principal.Current is { Agent: { } agent } buyer)
        {
            var verdict = agentPolicy.Evaluate(new AgentAct(
                agent,
                buyer.Standing ?? AgentStanding.Unknown,
                pricing.Totals.Total,
                request.Mandate,
                await agentActivity.OrdersPlacedSinceAsync(agent, clock.GetUtcNow().AddHours(-1), cancellationToken)));

            if (!verdict.Allowed)
            {
                activity?.SetTag("checkout.outcome", "agent_refused");
                activity?.SetTag("agent.rule", verdict.Rule);
                return new PlaceOrderResult(PlaceOrderOutcome.AgentRefused, null, pricing, $"{verdict.Rule}: {verdict.Detail}");
            }
        }

        // 3d · Is there that much of it? Asked last of the refusals and before
        // any money, so the shortage the shop can already see costs what a
        // declined card costs — nothing. It holds nothing: the hold is the
        // saga's, after the order exists, and that one settles the race.
        var available = await availability.AvailableAsync([.. cart.Lines.Select(line => line.Sku)], cancellationToken);

        StockShortage[] shortages =
        [
            .. cart.Lines
                .Select(line => new StockShortage(line.Sku, line.Quantity, Math.Max(0, available.GetValueOrDefault(line.Sku))))
                .Where(line => line.Requested > line.Available)
        ];

        if (shortages.Length > 0)
        {
            activity?.SetTag("checkout.outcome", "insufficient_stock");
            return new PlaceOrderResult(
                PlaceOrderOutcome.InsufficientStock, null, pricing,
                string.Join("; ", shortages.Select(line => $"{line.Sku}: {line.Requested} asked for, {line.Available} available")),
                shortages);
        }

        // 4 · The money. Nothing has been written yet, so a decline costs
        // nothing: no order, no reservation, an untouched cart. The id minted here
        // is the order's own, so the provider's record and the order name the
        // same order (ADR 0038's consequence).
        var provider = payments.Get(options.Value.ProviderFor(mode));
        var orderId = OrderId.New();

        var authorization = await provider.AuthorizeAsync(
            new PaymentRequest(
                orderId,
                pricing.Totals.Total,
                request.IdempotencyKey,
                request.Instrument,
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

        // 5 · Who is buying. Guests are first class, so a customer id is MINTED
        // here when nobody signed in — an order always has a buyer, even an
        // anonymous one. It is written back onto the cart so the two agree.
        var customer = principal.Current.Customer ?? cart.CustomerId ?? CustomerId.New();

        if (cart.CustomerId is null)
            cart.Claim(clock, customer);

        var unitPrices = pricing.Lines.ToDictionary(
            line => line.Sku, line => line.UnitPrice, StringComparer.OrdinalIgnoreCase);

        // The order speaks the language the shopper checked out in, not the one
        // the cart happened to be started in.
        cart.UseCulture(clock, request.Culture);

        var order = Order.Place(
            clock,
            customer,
            request.IdempotencyKey,
            [
                .. cart.Lines.Select(line => new OrderLine(
                    line.ProductId,
                    line.VariantId,
                    line.Sku,
                    // Resolved once, here, and frozen: the order keeps one
                    // language because it is a record of one purchase.
                    line.ProductName.In(cart.Culture),
                    line.VariantLabel?.In(cart.Culture),
                    line.ImageId,
                    // The price pricing just quoted, not one the cart carried:
                    // the cart carries none, on purpose.
                    unitPrices[line.Sku],
                    line.Quantity,
                    line.CategoryCode,
                    line.CategoryPath))
            ],
            request.ShippingAddress,
            // Billing defaults to shipping and is still its OWN copy: "the same
            // today" is not "the same forever".
            request.BillingAddress ?? request.ShippingAddress,
            new OrderShipping(option.Code, option.Label.In(cart.Culture), option.Amount, option.EstimatedDays),
            pricing.AsOrderQuote(),
            pricing.Totals,
            pricing.Discounts,
            pricing.Taxes,
            cart.Culture,
            mode,
            orderId,
            request.ContactEmail,
            request.Mandate is { } approved ? OrderMandate.From(approved) : null,
            principal.Current.Agent,
            // The storefront, MCP or UCP — known now and never again (R-9, D-7).
            OrderChannels.From(actor.Current.Surface));

        order.AuthorizePayment(clock, new OrderPayment(provider.Key, authorization.AuthorizationId!, null));
        orders.Add(order);

        // 6 · The cart is done. `OrderPlaced` is the fact the rest of the system
        // reacts to, and the caller commits everything together — the outbox's
        // whole correctness argument.
        cart.MarkCheckedOut(clock);

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

/// <summary>
/// `tendero.checkout.attempts` (the tracking plan), from every way into checkout —
/// the storefront's and UCP's: orders placed are the
/// attempts whose outcome is <c>placed</c>, and every other outcome says which
/// fix a failed checkout points at — our screen, our price, the shopper's card
/// or the provider.
/// </summary>
public static class CheckoutAttempts
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
