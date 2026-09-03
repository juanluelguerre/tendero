using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Accounts.Ports;

/// <summary>Access to the Customer aggregate, for the slices that change one.</summary>
public interface ICustomerRepository
{
    Task<Customer?> FindByIdAsync(CustomerId id, CancellationToken ct);

    /// <summary>
    /// The customer an identity provider's `sub` belongs to.
    ///
    /// Unique by construction: linking a second subject to a customer is refused
    /// by the aggregate, and a unique index makes two customers holding the same
    /// one impossible. Between them, "who is this token" has exactly one answer.
    /// </summary>
    Task<Customer?> FindBySubjectAsync(string subject, CancellationToken ct);

    void Add(Customer customer);
}

/// <summary>
/// Subject to customer id, and nothing else.
///
/// It exists for ONE caller: the API's principal accessor, which runs on every
/// authenticated request and has to fill <c>CommercePrincipal.Customer</c>. That
/// caller cannot use <see cref="ICustomerRepository"/>, and the reason is worth
/// stating — an accessor that loaded an aggregate would be materialising a
/// customer to answer a question about a token, on every request including the
/// ones that never touch a customer.
///
/// It is also strictly a READ. The principal accessor must never create a row:
/// a GET that writes is a GET that cannot be retried, and signing in would
/// register an account as a side effect of looking at a page. Registration is
/// the explicit `LinkIdentity` command, which the storefront calls once.
/// </summary>
public interface ICustomerDirectory
{
    Task<CustomerId?> ForSubjectAsync(string subject, CancellationToken ct = default);
}
