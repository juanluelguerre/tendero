using System.Diagnostics;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Ordering.Features.StockSaga;

/// <summary>
/// The order's stock saga: hold on placing, give back on cancelling, decrement
/// on shipping.
///
/// **There is no saga framework and there is not going to be one.** Each arrow
/// below is an ordinary <c>IDomainEventHandler&lt;T&gt;</c>, and the existing
/// outbox is the process manager: the event was written in the same transaction
/// as the state change that raised it, the worker drains it, the handler runs.
/// Everything a saga library sells — durable messages, at-least-once delivery,
/// retries, a dead letter after five attempts — this repository already had
/// before anybody called it a saga.
///
/// **It lives in Ordering, and that direction is the decision.** The process
/// being managed is the order's lifecycle, so Ordering orchestrates and calls
/// <see cref="IStockLedger"/> — a port that speaks in SKUs, quantities and an
/// <c>OrderId</c> from the SharedKernel. Inventory references nothing: stock
/// exists without orders, and a warehouse that had to know what an order is
/// would be the general thing depending on the specific one. An architecture
/// rule proves it by reflection.
///
/// The alternative — Inventory subscribing to <c>OrderPlaced</c> and publishing
/// <c>StockReserved</c> back — is the textbook choreography, and it was
/// rejected: it needs Inventory to reference Ordering's domain for the event
/// type (or a third place to keep integration contracts), and it spreads one
/// process across two contexts so that no single file says what happens when an
/// order is placed.
///
/// **At-least-once is the contract**, so every handler here is idempotent. The
/// ledger returns the existing hold instead of taking stock twice, and commit
/// and release do nothing when the reservation is no longer held.
/// </summary>
public sealed class ReserveStockOnOrderPlaced(
    IOrderRepository orders,
    IStockLedger stock,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ReserveStockOnOrderPlaced> logger) : IDomainEventHandler<OrderPlaced>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.reserve_stock");
        activity?.SetTag("ordering.order_id", domainEvent.OrderId.Value);

        var order = await orders.FindByIdAsync(domainEvent.OrderId, cancellationToken);

        if (order is null)
        {
            // The outbox delivers at least once and can deliver late. An order
            // that is not there any more is not a failure worth retrying five
            // times.
            logger.LogWarning("Order {OrderId} was placed and is gone", domainEvent.OrderId);
            return;
        }

        // Pending or PaymentAuthorized, and nothing further along.
        //
        // Both are legitimate because checkout authorises BEFORE it places, so
        // by the time this message drains the order is usually already
        // PaymentAuthorized — the two happened in one transaction. Pending is
        // what an order looks like when it was placed some other way, and the
        // saga should not care which.
        //
        // Anything past that has been through here already, and re-reserving on
        // a redelivery would hold the stock twice.
        if (order.Status is not (OrderStatus.Pending or OrderStatus.PaymentAuthorized))
            return;

        var requests = order.Lines
            .GroupBy(line => line.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(group => new StockRequest(group.Key, group.Sum(line => line.Quantity)))
            .ToArray();

        var outcome = await stock.ReserveAsync(order.Id, requests, cancellationToken);

        activity?.SetTag("ordering.stock_reserved", outcome.Reserved);

        if (outcome.Reserved)
        {
            logger.LogInformation("Stock held for order {OrderId}", order.Id);
            return;
        }

        // The refusal, in the customer's words rather than a code. It is the
        // most interesting row in the system, and it is why the ledger writes a
        // released reservation instead of failing silently.
        order.Cancel(clock, OrderStop.OutOfStock, outcome.Reason);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("ordering.cancelled_reason", outcome.Reason);
    }
}

/// <summary>
/// The order is paid for and the shelf is holding it. Confirm it.
///
/// It hangs off the PAYMENT and not off the stock, and the two are not
/// interchangeable. Checkout authorises before it places, so the payment event
/// is the later of the two facts in every ordinary order — and an order that
/// reached `PaymentAuthorized` without stock being held is one this handler must
/// leave alone, which is why it asks the ledger rather than assuming.
///
/// The roadmap drew this arrow as "on StockReserved -> Confirm()", and the
/// order's own transition table is what says that was wrong: `Pending` goes to
/// `PaymentAuthorized` before `Confirmed`, and stock says nothing about whether
/// anybody paid.
/// </summary>
public sealed class ConfirmOrderWhenPaidAndHeld(
    IOrderRepository orders,
    IStockLedger stock,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IDomainEventHandler<OrderPaymentAuthorized>
{
    public async Task HandleAsync(
        OrderPaymentAuthorized domainEvent, CancellationToken cancellationToken)
    {
        var order = await orders.FindByIdAsync(domainEvent.OrderId, cancellationToken);

        // Idempotent by state: a redelivery finds it Confirmed and stops.
        if (order?.Status != OrderStatus.PaymentAuthorized)
            return;

        // Both messages are drained in the order they were written, so this one
        // arrives AFTER the reservation was attempted. If there is no hold, the
        // stock arm has either refused (and cancelled the order, which this
        // status check has already excluded) or has not run yet — and the
        // redelivery will find the hold next time.
        if (!await stock.IsHeldAsync(order.Id, cancellationToken))
            return;

        order.Confirm(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Compensation. It runs when an order is cancelled for ANY reason — no stock,
/// a failed payment, a customer changing their mind — which is exactly why it
/// hangs off <c>OrderCancelled</c> and not off the reservation's failure.
///
/// This is the arm that never runs on the happy path, and therefore the one
/// that is easy to have never written and not notice. Its test exists (see
/// <c>StockSagaTests</c>), and it is separate from the happy one on purpose.
/// </summary>
public sealed class ReleaseStockOnOrderCancelled(IStockLedger stock)
    : IDomainEventHandler<OrderCancelled>
{
    public Task HandleAsync(OrderCancelled domainEvent, CancellationToken cancellationToken) =>
        stock.ReleaseAsync(domainEvent.OrderId, domainEvent.Reason, cancellationToken);
}

/// <summary>
/// The other half of the same compensation, and the one that costs a customer
/// real money if it is missing.
///
/// Checkout authorises before it places, so an order that cannot be filled has
/// somebody's card holding funds against a parcel that will not ship. Releasing
/// it is <c>VoidAsync</c> and never <c>RefundAsync</c>: nothing was ever taken,
/// and crediting money that was not charged is a different conversation with the
/// customer AND with the bank.
///
/// It is a separate handler from the stock release rather than two calls in one,
/// because they fail independently: a provider being down must not leave stock
/// held, and the outbox retries each message on its own.
/// </summary>
public sealed class VoidPaymentOnOrderCancelled(
    IOrderRepository orders,
    IPaymentProviderRegistry payments,
    ILogger<VoidPaymentOnOrderCancelled> logger) : IDomainEventHandler<OrderCancelled>
{
    public async Task HandleAsync(OrderCancelled domainEvent, CancellationToken cancellationToken)
    {
        var order = await orders.FindByIdAsync(domainEvent.OrderId, cancellationToken);

        // Nothing authorised, or already captured. A captured payment is a
        // REFUND, which belongs to the returns flow and not here.
        if (order?.Payment is not { CaptureId: null } payment)
            return;

        var released = await payments.Get(payment.Provider)
            .VoidAsync(payment.AuthorizationId, cancellationToken);

        if (released.Succeeded)
            logger.LogInformation("Released the hold on cancelled order {OrderId}", order.Id);
        else
            // Loudly, and without throwing: a hold that could not be released
            // expires on its own in about a week, and dead-lettering the message
            // would hide it behind a retry count.
            logger.LogError(
                "Could not release the hold on cancelled order {OrderId}: {Reason}",
                order.Id, released.FailureReason);
    }
}

/// <summary>
/// The goods left the building: the hold becomes a decrement. On shipping and
/// not on confirming, because confirming is a promise and shipping is a fact —
/// and stock that left the shelf on a promise is stock a cancellation cannot
/// give back.
/// </summary>
public sealed class CommitStockOnOrderShipped(IStockLedger stock)
    : IDomainEventHandler<OrderShipped>
{
    public Task HandleAsync(OrderShipped domainEvent, CancellationToken cancellationToken) =>
        stock.CommitAsync(domainEvent.OrderId, cancellationToken);
}

/// <summary>
/// The parcel is out; take the money.
///
/// Capture on SHIPPING and not on confirming, for the same reason stock is
/// committed there: confirming is a promise and shipping is a fact. Charging for
/// a promise is how a shop ends up refunding things it never sent.
///
/// The order records the capture itself, so a redelivery finds it already
/// captured and raises nothing — the aggregate holds that check rather than this
/// handler, because the webhook path reaches it too.
/// </summary>
public sealed class CapturePaymentOnOrderShipped(
    IOrderRepository orders,
    IPaymentProviderRegistry payments,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<CapturePaymentOnOrderShipped> logger) : IDomainEventHandler<OrderShipped>
{
    public async Task HandleAsync(OrderShipped domainEvent, CancellationToken cancellationToken)
    {
        var order = await orders.FindByIdAsync(domainEvent.OrderId, cancellationToken);

        if (order?.Payment is not { CaptureId: null } payment)
            return;

        var capture = await payments.Get(payment.Provider)
            .CaptureAsync(payment.AuthorizationId, order.Total, cancellationToken);

        if (!capture.Succeeded)
        {
            // The compensation path nobody tests, and the reason authorise and
            // capture are separate operations at all: the goods have shipped and
            // the money did not move. It is a human problem, so it is logged
            // rather than swallowed — and the order stays uncaptured, which is
            // exactly what a report of unpaid shipments would look for.
            logger.LogError(
                "Shipped order {OrderId} could not be captured: {Reason}",
                order.Id, capture.FailureReason);
            return;
        }

        order.CapturePayment(clock, capture.CaptureId!);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
