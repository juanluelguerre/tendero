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

/// <summary>An agent's recent orders, counted where they are: one indexed count, by agent then time.</summary>
internal sealed class EfAgentActivity(TenderoDbContext context) : IAgentActivity
{
    public Task<int> OrdersPlacedSinceAsync(AgentId agent, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        context.Orders.CountAsync(order => order.PlacedBy == agent && order.CreatedAt >= since, cancellationToken);
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

/// <summary>
/// The dashboard's reads (P11-7): windows of orders, carts and returns, untracked.
/// The sums happen in <c>DashboardFigures</c>; at laboratory scale a window is a
/// few hundred rows, and pushing the sums into SQL is scale work, written down.
/// </summary>
internal sealed class EfShopHistory(TenderoDbContext context) : IShopHistory
{
    public async Task<IReadOnlyList<Order>> OrdersPlacedAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        await context.Orders.AsNoTracking()
            .Where(order => order.CreatedAt >= from && order.CreatedAt < to)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Order>> OrdersInAsync(IReadOnlyCollection<OrderStatus> statuses, CancellationToken cancellationToken = default) =>
        await context.Orders.AsNoTracking()
            .Where(order => statuses.Contains(order.Status))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Cart>> CartsStartedAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        await context.Carts.AsNoTracking()
            .Where(cart => cart.CreatedAt >= from && cart.CreatedAt < to)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReturnRequest>> ReturnsOpenOrChangedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
        await context.ReturnRequests.AsNoTracking()
            .Where(request => request.UpdatedAt >= since
                              || request.Status == ReturnStatus.Requested
                              || request.Status == ReturnStatus.Approved
                              || request.Status == ReturnStatus.Received)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<CustomerId>> CustomersWhoOrderedBeforeAsync(
        IReadOnlyCollection<CustomerId> customers, DateTimeOffset before, CancellationToken cancellationToken = default) =>
        (await context.Orders.AsNoTracking()
            .Where(order => customers.Contains(order.CustomerId) && order.CreatedAt < before && order.Mode == OrderMode.Live)
            .Select(order => order.CustomerId)
            .Distinct()
            .ToListAsync(cancellationToken))
        .ToHashSet();
}
