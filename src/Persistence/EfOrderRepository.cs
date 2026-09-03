using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// The Order aggregate over EF Core.
///
/// Tracking, always: everything that loads an order does it to move it through
/// its state machine and commit. The catalogue needed two methods — one tracked
/// and one not — because it has a projection reading it; orders have no read
/// side yet, and when they do it gets its own port rather than a flag on this one.
/// </summary>
internal sealed class EfOrderRepository(TenderoDbContext context) : IOrderRepository
{
    public Task<Order?> FindByIdAsync(OrderId id, CancellationToken cancellationToken = default) =>
        context.Orders.FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

    public Task<Order?> FindByIdempotencyKeyAsync(
        string key, CancellationToken cancellationToken = default) =>
        context.Orders.FirstOrDefaultAsync(order => order.IdempotencyKey == key, cancellationToken);

    public void Add(Order order) => context.Orders.Add(order);
}

/// <summary>The read side. No tracking, newest first, capped.</summary>
internal sealed class EfOrderReader(TenderoDbContext context) : IOrderReader
{
    /// <summary>
    /// Enough to see what the shop has been doing without a page control that
    /// nothing needs yet. The number is here rather than in configuration
    /// because it is a laboratory-scale decision, and the standing backlog is
    /// where scale work goes.
    /// </summary>
    private const int Recent = 50;

    public async Task<IReadOnlyList<Order>> RecentAsync(CancellationToken cancellationToken = default) =>
        await context.Orders
            .AsNoTracking()
            .OrderByDescending(order => order.CreatedAt)
            .Take(Recent)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// One customer's orders. The filter is not optional and cannot be made so
    /// by passing null — which is the whole reason it is a separate method
    /// rather than a nullable argument on the one above.
    /// </summary>
    public async Task<IReadOnlyList<Order>> ForCustomerAsync(
        CustomerId customer, CancellationToken cancellationToken = default) =>
        await context.Orders
            .AsNoTracking()
            .Where(order => order.CustomerId == customer)
            .OrderByDescending(order => order.CreatedAt)
            .Take(Recent)
            .ToListAsync(cancellationToken);
}
