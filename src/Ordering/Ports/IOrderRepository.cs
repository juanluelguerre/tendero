using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Ports;

/// <summary>
/// Access to the Order aggregate.
///
/// It arrives with the stock saga, which is the first thing in the repository
/// that loads an order it did not create: the handler for <c>OrderPlaced</c>
/// gets an id and needs the lines. Checkout used the same port in phase 5.
///
/// Tracking, always. Everything that loads an order here does so to move it
/// through its state machine and commit — there is no read side yet, and when
/// there is (the orders list, the agent activity panel) it gets its own port,
/// the way <c>IProductCatalogReader</c> is separate from
/// <c>IProductRepository</c>.
/// </summary>
public interface IOrderRepository
{
    Task<Order?> FindByIdAsync(OrderId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The order this checkout already produced, if any.
    ///
    /// It is the FIRST thing `PlaceOrder` asks, before anything is charged. The
    /// unique index on the column is what makes it true under a race; this is
    /// what makes the common case answer without one.
    /// </summary>
    Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default);

    void Add(Order order);
}

/// <summary>
/// The READ side of orders, separate from the repository that writes them.
///
/// The same split `IProductCatalogReader` makes against `IProductRepository`,
/// and for the same reason: everything on the write side is tracked because it
/// exists to move an aggregate through its state machine, and a list that
/// tracked thirty orders to render six columns would be paying for change
/// detection nobody uses.
/// </summary>
public interface IOrderReader
{
    /// <summary>
    /// The most recent orders, newest first. There is no paging: the shop has
    /// six products and a laboratory's worth of orders, and a cap is the honest
    /// version of a page size nobody has asked for yet.
    /// </summary>
    Task<IReadOnlyList<Order>> RecentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// One customer's orders, newest first.
    ///
    /// A separate method rather than a nullable filter on <see cref="RecentAsync"/>,
    /// and the reason is that the two have different failure modes. The
    /// shopkeeper's list is allowed to be everything; this one must NEVER be —
    /// a filter that could be passed null would be one refactor away from
    /// showing a shopper the whole shop's orders, and the type would not say so.
    /// </summary>
    Task<IReadOnlyList<Order>> ForCustomerAsync(
        CustomerId customer, CancellationToken cancellationToken = default);
}
