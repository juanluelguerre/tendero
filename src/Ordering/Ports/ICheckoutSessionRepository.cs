using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Ports;

/// <summary>UCP checkout sessions (P11-1), by id. EF Core, so Persistence registers it.</summary>
public interface ICheckoutSessionRepository
{
    Task<CheckoutSession?> FindByIdAsync(CheckoutSessionId id, CancellationToken cancellationToken = default);

    /// <summary>The session whose cart this is, if an agent opened one — so a person who pays for it on the storefront completes it (requires_escalation's way out).</summary>
    Task<CheckoutSession?> FindByCartAsync(CartId cartId, CancellationToken cancellationToken = default);

    void Add(CheckoutSession session);
}
