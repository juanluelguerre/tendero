using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Features.TestOrders;

/// <summary>
/// A test order that reached <c>Confirmed</c> has done everything a real one
/// does before it leaves the warehouse — stock held, payment authorised, every
/// event raised and every command audited — and ends here (ADR 0038, P11-0). It
/// is cancelled, and the compensation any cancellation runs gives its stock back
/// and voids its hold: nothing new to keep in step, and the rehearsal exercises
/// the one path a live order rarely takes.
///
/// Why not end it later: a timer would need a scheduler this system does not
/// have, and a rehearsal left holding stock is stock a real shopper cannot buy.
/// </summary>
public sealed class EndTestOrderWhenConfirmed(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IDomainEventHandler<OrderConfirmed>
{
    private static readonly Meter Meter = new(TelemetrySources.Ordering);

    private static readonly Counter<long> Ended = Meter.CreateCounter<long>(
        "tendero.orders.test_ended", unit: "{order}",
        description: "Test orders that went all the way to confirmed and ended there, their stock given back.");

    public async Task HandleAsync(OrderConfirmed domainEvent, CancellationToken cancellationToken)
    {
        var order = await orders.FindByIdAsync(domainEvent.OrderId, cancellationToken);

        // Idempotent by state: a live order is not this handler's, and a
        // redelivery finds the test order already cancelled.
        if (order is not { Mode: OrderMode.Test, Status: OrderStatus.Confirmed })
            return;

        order.EndTest(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        Ended.Add(1);
    }
}
