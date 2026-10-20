using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>A customer's saved ways to pay as an aggregate (P15-4): what the commands, checkout and the forgetting worker load.</summary>
internal sealed class EfWalletRepository(TenderoDbContext context) : IWalletRepository
{
    // WITH tracking: whoever loads a wallet does it to change it, or to charge one of its cards.
    public Task<Wallet?> FindAsync(CustomerId customer, CancellationToken ct = default) =>
        context.Wallets.FirstOrDefaultAsync(wallet => wallet.CustomerId == customer, ct);

    public void Add(Wallet wallet) => context.Wallets.Add(wallet);

    public void Remove(Wallet wallet) => context.Wallets.Remove(wallet);

    /// <summary>Read in SQL off the jsonb list's length, so a pass never loads the wallets that have nothing waiting.</summary>
    public async Task<IReadOnlyList<Wallet>> WithTokensToForgetAsync(int limit, CancellationToken ct = default) =>
        await context.Wallets
            .FromSql($"select * from ordering.wallets where jsonb_array_length(to_forget) > 0 order by updated_at limit {limit}")
            .ToListAsync(ct);
}

/// <summary>A customer's wallet as the page reads it: untracked, never saved back.</summary>
internal sealed class EfWalletReader(TenderoDbContext context) : IWalletReader
{
    public Task<Wallet?> ForCustomerAsync(CustomerId customer, CancellationToken ct = default) =>
        context.Wallets.AsNoTracking().FirstOrDefaultAsync(wallet => wallet.CustomerId == customer, ct);
}
