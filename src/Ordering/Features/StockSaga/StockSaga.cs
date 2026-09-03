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

        // Only from Pending. Anything further along has already been through
        // here, and re-reserving on a redelivery would hold the stock twice.
        if (order.Status != OrderStatus.Pending)
            return;

        var requests = order.Lines
            .GroupBy(line => line.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(group => new StockRequest(group.Key, group.Sum(line => line.Quantity)))
            .ToArray();

        var outcome = await stock.ReserveAsync(order.Id, requests, cancellationToken);

        activity?.SetTag("ordering.stock_reserved", outcome.Reserved);

        if (outcome.Reserved)
        {
            // Held, and that is all. Reserving is NOT confirming: the order's own
            // table says Pending goes to PaymentAuthorized before Confirmed, and
            // stock says nothing about whether anybody paid. The roadmap drew
            // this arrow as "on StockReserved -> Confirm()", and the state
            // machine is what says it is wrong.
            logger.LogInformation("Stock held for order {OrderId}", order.Id);
            return;
        }

        // The refusal, in the customer's words rather than a code. It is the
        // most interesting row in the system, and it is why the ledger writes a
        // released reservation instead of failing silently.
        order.Cancel(clock, outcome.Reason ?? "There was not enough stock.");
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("ordering.cancelled_reason", outcome.Reason);
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
