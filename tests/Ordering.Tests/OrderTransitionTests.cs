using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// The AllowedTransitions table is the single source of truth for an order's
/// lifecycle, so it is walked WHOLE here: from every state, only the permitted
/// targets work and every other one fails. The expected matrix is written by hand
/// on purpose — it is a second, independent declaration of the truth, and
/// changing the table forces this one to change too.
/// </summary>
public sealed class OrderTransitionTests
{
    private static readonly TestClock Clock = new();

    private static readonly Dictionary<OrderStatus, OrderStatus[]> Expected = new()
    {
        [OrderStatus.Pending] = [OrderStatus.PaymentAuthorized, OrderStatus.PaymentFailed, OrderStatus.Cancelled],
        [OrderStatus.PaymentAuthorized] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.PaymentFailed] = [OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped] = [OrderStatus.Delivered],
        [OrderStatus.Delivered] = [],
        [OrderStatus.Cancelled] = []
    };

    // Pending does not appear as a target: the domain's table allows
    // PaymentFailed -> Pending (retrying the charge), but no public method causes
    // it yet. It arrives with the checkout slice.
    private static readonly OrderStatus[] ReachableTargets =
    [
        OrderStatus.PaymentAuthorized,
        OrderStatus.PaymentFailed,
        OrderStatus.Confirmed,
        OrderStatus.Shipped,
        OrderStatus.Delivered,
        OrderStatus.Cancelled
    ];

    public static TheoryData<OrderStatus, OrderStatus> EveryPair()
    {
        var data = new TheoryData<OrderStatus, OrderStatus>();

        foreach (var from in Expected.Keys)
            foreach (var to in ReachableTargets)
                data.Add(from, to);

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Only_the_transitions_in_the_table_are_allowed(OrderStatus from, OrderStatus to)
    {
        var order = OrderBuilder.Default().InStatus(from);
        var allowed = Expected[from].Contains(to);

        if (allowed)
        {
            Transition(order, to);
            Assert.Equal(to, order.Status);
            return;
        }

        var exception = Assert.Throws<InvalidOperationException>(() => Transition(order, to));
        Assert.Contains("Illegal transition", exception.Message);
        Assert.Equal(from, order.Status);
    }

    [Fact]
    public void Cancelling_a_delivered_order_is_rejected()
    {
        var order = OrderBuilder.Default().InStatus(OrderStatus.Delivered);

        Assert.Throws<InvalidOperationException>(() => order.Cancel(Clock, OrderStop.ShopCancelled, "too late"));
        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    [Fact]
    public void A_cancelled_order_records_why()
    {
        var order = OrderBuilder.Default().Build();

        order.Cancel(Clock, OrderStop.OutOfStock, "PANS: 2 asked for, 1 available.");

        var cancelled = Assert.IsType<OrderCancelled>(
            Assert.Single(order.DomainEvents, domainEvent => domainEvent is OrderCancelled));
        Assert.Equal("PANS: 2 asked for, 1 available.", cancelled.Reason);
    }

    /// <summary>
    /// The event is not enough, and that was the bug.
    ///
    /// `OrderCancelled` carried the reason, the outbox delivered it, the handler
    /// marked it processed, and the order was left saying `Cancelled` and
    /// nothing else. A shopper reading the order page had no explanation and a
    /// shopkeeper had no row to point at. The reason has to survive on the
    /// aggregate, which is where both of them read it.
    /// </summary>
    [Fact]
    public void The_reason_survives_the_event_that_announced_it()
    {
        var order = OrderBuilder.Default().Build();

        order.Cancel(Clock, OrderStop.OutOfStock, "PANS: 2 asked for, 1 available.");

        Assert.Equal(OrderStop.OutOfStock, order.Stop?.Code);
        Assert.Equal("PANS: 2 asked for, 1 available.", order.Stop?.Detail);
    }

    /// <summary>A declined card is a refusal too, and it says so the same way.</summary>
    [Fact]
    public void A_declined_payment_records_its_own_code()
    {
        var order = OrderBuilder.Default().Build();

        order.FailPayment(Clock, "card declined");

        Assert.Equal(OrderStop.PaymentDeclined, order.Stop?.Code);
        Assert.Equal("card declined", order.Stop?.Detail);
    }

    /// <summary>
    /// An order in flight has nothing to explain, and a stale reason left on one
    /// would be read as current by both screens.
    /// </summary>
    [Fact]
    public void An_order_that_has_not_stopped_carries_no_reason()
    {
        var order = OrderBuilder.Default().Build();

        order.AuthorizePayment(Clock);
        order.Confirm(Clock);

        Assert.Null(order.Stop);
    }

    private static void Transition(Order order, OrderStatus target)
    {
        switch (target)
        {
            case OrderStatus.PaymentAuthorized: order.AuthorizePayment(Clock); break;
            case OrderStatus.PaymentFailed: order.FailPayment(Clock, "card declined"); break;
            case OrderStatus.Confirmed: order.Confirm(Clock); break;
            case OrderStatus.Shipped: order.Ship(Clock); break;
            case OrderStatus.Delivered: order.Deliver(Clock); break;
            case OrderStatus.Cancelled: order.Cancel(Clock, OrderStop.ShopCancelled); break;
            default: throw new ArgumentOutOfRangeException(nameof(target), target, "Unreachable target.");
        }
    }
}
