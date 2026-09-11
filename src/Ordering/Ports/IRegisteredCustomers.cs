using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Ports;

/// <summary>
/// Whether an order's customer has an account — the one fact about Accounts that
/// Ordering needs to decide who may read an order by its id.
///
/// A port and not a project reference: Ordering must not learn what a
/// <c>Customer</c> is, only this yes or no. Persistence answers it, as it answers
/// every other reader.
/// </summary>
public interface IRegisteredCustomers
{
    Task<bool> HasAccountAsync(CustomerId customer, CancellationToken cancellationToken = default);
}
