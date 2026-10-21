using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Ports;

/// <summary>A customer's saved ways to pay as an aggregate, for the commands that change it and checkout, which charges one (P15-4).</summary>
public interface IWalletRepository
{
    Task<Wallet?> FindAsync(CustomerId customer, CancellationToken ct = default);

    void Add(Wallet wallet);

    /// <summary>The wallet row gone, once nothing in it waits to be forgotten (P15-9).</summary>
    void Remove(Wallet wallet);

    /// <summary>The wallets with a token their provider has still to forget, oldest first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<Wallet>> WithTokensToForgetAsync(int limit, CancellationToken ct = default);
}

/// <summary>A customer's wallet as the page reads it: untracked, never saved back.</summary>
public interface IWalletReader
{
    /// <summary>Null for a customer who never kept a card — empty, as far as anybody can tell.</summary>
    Task<Wallet?> ForCustomerAsync(CustomerId customer, CancellationToken ct = default);
}
