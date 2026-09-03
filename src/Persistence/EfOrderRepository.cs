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

    public void Add(Order order) => context.Orders.Add(order);
}
