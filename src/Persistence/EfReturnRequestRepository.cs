using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>The ReturnRequest aggregate over EF Core.</summary>
internal sealed class EfReturnRequestRepository(TenderoDbContext context) : IReturnRequestRepository
{
    /// <summary>What is still waiting on somebody. Refunded, rejected and
    /// cancelled are history, and a queue that keeps them stops being one.</summary>
    private static readonly ReturnStatus[] Open =
        [ReturnStatus.Requested, ReturnStatus.Approved, ReturnStatus.Received];

    public Task<ReturnRequest?> FindByIdAsync(
        ReturnRequestId id, CancellationToken cancellationToken = default) =>
        context.ReturnRequests.FirstOrDefaultAsync(request => request.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ReturnRequest>> ForOrderAsync(
        OrderId orderId, CancellationToken cancellationToken = default) =>
        await context.ReturnRequests
            .AsNoTracking()
            .Where(request => request.OrderId == orderId)
            .OrderByDescending(request => request.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReturnRequest>> OpenAsync(
        CancellationToken cancellationToken = default) =>
        await context.ReturnRequests
            .Where(request => Open.Contains(request.Status))
            .OrderBy(request => request.CreatedAt)
            .ToListAsync(cancellationToken);

    public void Add(ReturnRequest request) => context.ReturnRequests.Add(request);
}
