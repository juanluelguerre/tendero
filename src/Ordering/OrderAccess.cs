using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering;

/// <summary>
/// Who may reach an order through its id: reading it, and opening a return
/// against it.
///
/// **A guest's order keeps its id as the credential**, because there is still no
/// account to scope it to — a guest has to reload their confirmation page and
/// come back a week later to send something back. The id is a GUID v7 whose 74
/// random bits nobody enumerates.
///
/// **An order that belongs to an account is that account's**, and the
/// shopkeeper's. The endpoint used to say this would change "the moment Accounts
/// exists"; Accounts shipped on 2026-09-04 and the UI review of 2026-09-11 still
/// read a shopper's name and address with nothing but a URL.
///
/// Not a slice: two slices ask it (GetOrder and RequestReturn), and shared
/// behaviour goes down or out rather than across (CLAUDE.md, invariant 2).
/// </summary>
public sealed class OrderAccess(IRegisteredCustomers accounts, IPrincipalAccessor principals)
{
    public async Task<bool> CanReadAsync(Order order, CancellationToken cancellationToken)
    {
        var principal = principals.Current;

        if (principal.IsInRole(TenderoRoles.Shopkeeper) || principal.Customer == order.CustomerId)
            return true;

        return !await accounts.HasAccountAsync(order.CustomerId, cancellationToken);
    }
}
