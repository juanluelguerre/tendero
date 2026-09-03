using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Ports;

/// <summary>
/// Access to the Order aggregate.
///
/// It arrives with the stock saga, which is the first thing in the repository
/// that loads an order it did not create: the handler for <c>OrderPlaced</c>
/// gets an id and needs the lines. Checkout will use the same port in phase 5.
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
