using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

internal sealed class EfCheckoutSessionRepository(TenderoDbContext context) : ICheckoutSessionRepository
{
    // WITH tracking: every caller loads a session to change it or to answer about it.
    public Task<CheckoutSession?> FindByIdAsync(CheckoutSessionId id, CancellationToken cancellationToken = default) =>
        context.CheckoutSessions.FirstOrDefaultAsync(session => session.Id == id, cancellationToken);

    public Task<CheckoutSession?> FindByCartAsync(CartId cartId, CancellationToken cancellationToken = default) =>
        context.CheckoutSessions.FirstOrDefaultAsync(session => session.CartId == cartId, cancellationToken);

    public void Add(CheckoutSession session) => context.CheckoutSessions.Add(session);
}
