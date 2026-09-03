using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Ports;

/// <summary>
/// Access to the ReturnRequest aggregate.
///
/// <c>OpenAsync</c> is the queue the backoffice works through, and it is
/// deliberately not "all returns": a resolved one is history, and a queue that
/// grows forever stops being a queue.
/// </summary>
public interface IReturnRequestRepository
{
    Task<ReturnRequest?> FindByIdAsync(ReturnRequestId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReturnRequest>> ForOrderAsync(
        OrderId orderId, CancellationToken cancellationToken = default);

    /// <summary>Everything still waiting on somebody: requested, approved, or
    /// received and not yet refunded.</summary>
    Task<IReadOnlyList<ReturnRequest>> OpenAsync(CancellationToken cancellationToken = default);

    void Add(ReturnRequest request);
}
