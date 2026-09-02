using ElGuerre.Tendero.Ordering.Domain;
using Xunit;

using ElGuerre.Tendero.Tests;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// La tabla AllowedTransitions es la única fuente de verdad del ciclo de vida
/// del pedido, así que aquí se recorre ENTERA: desde cada estado, sólo los
/// destinos permitidos funcionan y todos los demás fallan. La matriz esperada se
/// escribe a mano a propósito — es una segunda declaración independiente de la
/// verdad, y cambiar la tabla obliga a cambiar también esta.
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

    // Pending no aparece como destino: la tabla del dominio permite
    // PaymentFailed -> Pending (reintentar el cobro), pero ningún método público
    // lo provoca todavía. Entrará con el slice de checkout.
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

        Assert.Throws<InvalidOperationException>(() => order.Cancel(Clock, "too late"));
        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    [Fact]
    public void A_cancelled_order_records_why()
    {
        var order = OrderBuilder.Default().Build();

        order.Cancel(Clock, "out of stock");

        var cancelled = Assert.IsType<OrderCancelled>(
            Assert.Single(order.DomainEvents, domainEvent => domainEvent is OrderCancelled));
        Assert.Equal("out of stock", cancelled.Reason);
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
            case OrderStatus.Cancelled: order.Cancel(Clock, "test"); break;
            default: throw new ArgumentOutOfRangeException(nameof(target), target, "Unreachable target.");
        }
    }
}
