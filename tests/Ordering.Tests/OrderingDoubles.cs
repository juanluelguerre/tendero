using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// A repository holding exactly one order. It lived as a private copy in two
/// test classes, and the copies had already drifted — one answered any id with
/// the order — before the third caller (the test orders, P11-0) arrived.
/// </summary>
internal sealed class OneOrder(Order order) : IOrderRepository
{
    public Task<Order?> FindByIdAsync(OrderId id, CancellationToken cancellationToken = default) =>
        Task.FromResult<Order?>(id == order.Id ? order : null);

    public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<Order?>(null);

    public void Add(Order order) { }
}

/// <summary>How many times a handler saved: a transition never saved did not happen.</summary>
internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        Saves++;
        return Task.CompletedTask;
    }
}
