using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.StockSaga;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElGuerre.Tendero.Inventory.Tests;

/// <summary>
/// The saga, one handler at a time.
///
/// There is no saga framework to test around: each arm is an
/// <c>IDomainEventHandler&lt;T&gt;</c> that the existing outbox delivers to, so
/// testing it is calling it. That is the whole argument of the phase — the
/// process manager was already in the repository — and it shows up here as tests
/// that need no harness.
///
/// The compensation arm gets its own tests. It never runs on the happy path,
/// which is exactly how a release that was never written stays unnoticed until
/// a shop is quietly holding stock for orders that died weeks ago.
/// </summary>
public sealed class StockSagaTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly TestClock _clock = new();
    private readonly FakeLedger _ledger = new();
    private readonly FakeOrders _orders = new();
    private int _saves;

    private ReserveStockOnOrderPlaced OnPlaced() => new(
        _orders, _ledger, new CountingUnitOfWork(() => _saves++), _clock,
        NullLogger<ReserveStockOnOrderPlaced>.Instance);

    private Order Placed(params (string Sku, int Quantity)[] lines)
    {
        var order = Order.Place(
            _clock,
            CustomerId.New(),
            idempotencyKey: Guid.NewGuid().ToString(),
            culture: "es",
            lines:
            [
                .. lines.Select(line => new OrderLine(
                    ProductId.New(), VariantId.New(), line.Sku, "A product", null,
                    new Money(10m, "EUR"), line.Quantity))
            ]);

        _orders.Add(order);
        return order;
    }

    // ---------- Holding ----------

    [Fact]
    public async Task Placing_an_order_asks_for_its_stock_by_sku()
    {
        var order = Placed(("SHOES", 2), ("PANS", 1));

        await OnPlaced().HandleAsync(new OrderPlaced(order.Id, _clock.GetUtcNow()), Ct);

        Assert.Equal(
            [("SHOES", 2), ("PANS", 1)],
            _ledger.Requested.Select(request => (request.Sku, request.Quantity)));
    }

    /// <summary>
    /// Two lines of the same SKU are one question to inventory. Asking twice
    /// would let the first answer succeed and the second fail on stock the first
    /// had just taken.
    /// </summary>
    [Fact]
    public async Task Two_lines_of_one_sku_are_asked_for_once_as_a_total()
    {
        var order = Placed(("SHOES", 2), ("SHOES", 3));

        await OnPlaced().HandleAsync(new OrderPlaced(order.Id, _clock.GetUtcNow()), Ct);

        var request = Assert.Single(_ledger.Requested);
        Assert.Equal(("SHOES", 5), (request.Sku, request.Quantity));
    }

    /// <summary>
    /// Reserving is not confirming. The roadmap drew this arrow as
    /// "on StockReserved -> order.Confirm()", and the order's own transition
    /// table is what says it is wrong: Pending goes to PaymentAuthorized before
    /// Confirmed, and stock says nothing about whether anybody paid.
    /// </summary>
    [Fact]
    public async Task Holding_stock_does_not_confirm_the_order()
    {
        var order = Placed(("SHOES", 1));

        await OnPlaced().HandleAsync(new OrderPlaced(order.Id, _clock.GetUtcNow()), Ct);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(0, _saves);
    }

    // ---------- Refusing, and the compensation it triggers ----------

    [Fact]
    public async Task An_order_that_cannot_be_filled_is_cancelled_with_the_reason()
    {
        var order = Placed(("PANS", 2));
        _ledger.Refuse("PANS: 2 asked for, 0 available.");

        await OnPlaced().HandleAsync(new OrderPlaced(order.Id, _clock.GetUtcNow()), Ct);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(1, _saves);

        // The cancellation raises OrderCancelled, which is what the compensation
        // arm listens to. The saga is not a call chain; it is events meeting
        // handlers.
        var cancelled = Assert.Single(order.DomainEvents.OfType<OrderCancelled>());
        Assert.Equal("PANS: 2 asked for, 0 available.", cancelled.Reason);
    }

    [Fact]
    public async Task Cancelling_an_order_gives_the_hold_back()
    {
        var order = Placed(("SHOES", 2));

        await new ReleaseStockOnOrderCancelled(_ledger)
            .HandleAsync(new OrderCancelled(order.Id, "The customer changed their mind.", _clock.GetUtcNow()), Ct);

        Assert.Equal([(order.Id, "The customer changed their mind.")], _ledger.Released);
    }

    /// <summary>
    /// Compensation hangs off <c>OrderCancelled</c> and not off the reservation
    /// failing, which is what makes it run for reasons inventory never hears
    /// about: a declined card, a customer changing their mind, a shopkeeper
    /// cancelling by hand.
    /// </summary>
    [Fact]
    public async Task The_release_runs_for_cancellations_that_have_nothing_to_do_with_stock()
    {
        var order = Placed(("SHOES", 1));
        order.FailPayment(_clock, "The card was declined.");
        order.Cancel(_clock, "The card was declined.");

        var cancelled = order.DomainEvents.OfType<OrderCancelled>().Last();
        await new ReleaseStockOnOrderCancelled(_ledger).HandleAsync(cancelled, Ct);

        Assert.Equal([(order.Id, "The card was declined.")], _ledger.Released);
    }

    // ---------- Shipping ----------

    [Fact]
    public async Task Shipping_commits_the_hold()
    {
        var order = Placed(("SHOES", 1));

        await new CommitStockOnOrderShipped(_ledger)
            .HandleAsync(new OrderShipped(order.Id, _clock.GetUtcNow()), Ct);

        Assert.Equal([order.Id], _ledger.Committed);
    }

    // ---------- At-least-once delivery ----------

    [Fact]
    public async Task An_order_that_has_moved_on_is_not_reserved_again()
    {
        var order = Placed(("SHOES", 1));
        order.AuthorizePayment(_clock);

        await OnPlaced().HandleAsync(new OrderPlaced(order.Id, _clock.GetUtcNow()), Ct);

        // The outbox delivers at least once, and a redelivery must not hold the
        // stock a second time.
        Assert.Empty(_ledger.Requested);
    }

    [Fact]
    public async Task An_order_that_is_no_longer_there_is_not_a_crash()
    {
        await OnPlaced().HandleAsync(new OrderPlaced(OrderId.New(), _clock.GetUtcNow()), Ct);

        Assert.Empty(_ledger.Requested);
    }

    // ---------- Doubles ----------

    private sealed class FakeLedger : IStockLedger
    {
        private string? _refusal;

        public List<StockRequest> Requested { get; } = [];
        public List<(OrderId, string)> Released { get; } = [];
        public List<OrderId> Committed { get; } = [];

        public void Refuse(string reason) => _refusal = reason;

        public Task<ReservationOutcome> ReserveAsync(
            OrderId orderId, IReadOnlyList<StockRequest> requests, CancellationToken cancellationToken = default)
        {
            Requested.AddRange(requests);

            return Task.FromResult(_refusal is null
                ? ReservationOutcome.Held
                : ReservationOutcome.Refused(_refusal));
        }

        public Task CommitAsync(OrderId orderId, CancellationToken cancellationToken = default)
        {
            Committed.Add(orderId);
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(OrderId orderId, string reason, CancellationToken cancellationToken = default)
        {
            Released.Add((orderId, reason));
            return Task.CompletedTask;
        }

        public Task ReceiveAsync(
            string sku, string warehouseCode, int quantity, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<CountedShelf> CountAsync(
            string sku, string warehouseCode, int onHand, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CountedShelf(onHand, onHand));
    }

    private sealed class FakeOrders : IOrderRepository
    {
        private readonly List<Order> _orders = [];

        public Task<Order?> FindByIdAsync(OrderId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_orders.FirstOrDefault(order => order.Id == id));

        public void Add(Order order) => _orders.Add(order);
    }

    private sealed class CountingUnitOfWork(Action onSave) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken ct)
        {
            onSave();
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// The reservation's transition table, walked in full — the same treatment
/// <c>Order.AllowedTransitions</c> gets, and for the same reason: the table is
/// the single source of truth for the lifecycle, so the test writes the matrix
/// out by hand as a second, independent declaration of it.
/// </summary>
public sealed class ReservationTransitionTests
{
    private static readonly TestClock Clock = new();

    private static Reservation Held() =>
        Reservation.Hold(Clock, OrderId.New(), [new ReservationLine("SHOES", "MAD", 1)]);

    [Fact]
    public void A_hold_can_be_committed_released_or_expired()
    {
        Held().Commit(Clock);
        Held().Release(Clock, "no longer wanted");
        Held().Expire(Clock);
    }

    [Theory]
    [InlineData(ReservationStatus.Committed)]
    [InlineData(ReservationStatus.Released)]
    [InlineData(ReservationStatus.Expired)]
    public void Nothing_leaves_a_resolved_reservation(ReservationStatus resolved)
    {
        var reservation = Held();

        switch (resolved)
        {
            case ReservationStatus.Committed: reservation.Commit(Clock); break;
            case ReservationStatus.Released: reservation.Release(Clock, "gone"); break;
            default: reservation.Expire(Clock); break;
        }

        // Once a hold is resolved it stays resolved. A committed reservation that
        // could be released would give back stock that has already shipped.
        Assert.Throws<InvalidOperationException>(() => reservation.Commit(Clock));
        Assert.Throws<InvalidOperationException>(() => reservation.Release(Clock, "again"));
        Assert.Throws<InvalidOperationException>(() => reservation.Expire(Clock));
    }

    [Fact]
    public void A_hold_expires_on_its_own_schedule_and_a_resolved_one_never_does()
    {
        var reservation = Held();
        var justAfter = Clock.GetUtcNow() + Reservation.Lifetime;

        Assert.False(reservation.HasExpiredAt(Clock.GetUtcNow()));
        Assert.True(reservation.HasExpiredAt(justAfter));

        reservation.Commit(Clock);

        // Expiry is a property of a HOLD. Stock that has shipped cannot go stale.
        Assert.False(reservation.HasExpiredAt(justAfter));
    }

    /// <summary>
    /// A refusal is born resolved: it is the record of a hold that never
    /// happened, so there is nothing to commit or give back.
    /// </summary>
    [Fact]
    public void A_refusal_carries_its_reason_and_is_already_over()
    {
        var refused = Reservation.Refused(Clock, OrderId.New(), "PANS: 2 asked for, 0 available.");

        Assert.Equal(ReservationStatus.Released, refused.Status);
        Assert.Equal("PANS: 2 asked for, 0 available.", refused.Reason);
        Assert.Empty(refused.Lines);
        Assert.Throws<InvalidOperationException>(() => refused.Commit(Clock));
    }
}
