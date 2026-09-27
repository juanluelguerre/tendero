using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Ports;

/// <summary>
/// What the shop has done over a window of time, read for the shopkeeper's
/// dashboard (P11-7) — from Postgres, never from the observability stack, which is
/// a switch the dashboard cannot depend on.
///
/// It answers with aggregates and the dashboard's slice does the arithmetic, so the
/// figures are pure functions a test checks without a database. Loading a window's
/// orders is laboratory scale: at a real shop's volume the sums move into the
/// query, and that is written down with its number rather than built for a volume
/// the laboratory does not have.
/// </summary>
public interface IShopHistory
{
    /// <summary>Orders placed in [<paramref name="from"/>, <paramref name="to"/>), any mode — the dashboard leaves rehearsals out itself.</summary>
    Task<IReadOnlyList<Order>> OrdersPlacedAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Orders currently in one of <paramref name="statuses"/>, whenever they were placed: the work in hand.</summary>
    Task<IReadOnlyList<Order>> OrdersInAsync(IReadOnlyCollection<OrderStatus> statuses, CancellationToken cancellationToken = default);

    /// <summary>Carts started in the window: the top of the checkout funnel.</summary>
    Task<IReadOnlyList<Cart>> CartsStartedAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Returns still open, and those that changed since <paramref name="since"/>: what is in hand and what was refunded.</summary>
    Task<IReadOnlyList<ReturnRequest>> ReturnsOpenOrChangedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>Which of <paramref name="customers"/> had placed an order before <paramref name="before"/>: new against returning.</summary>
    Task<IReadOnlySet<CustomerId>> CustomersWhoOrderedBeforeAsync(
        IReadOnlyCollection<CustomerId> customers, DateTimeOffset before, CancellationToken cancellationToken = default);
}
