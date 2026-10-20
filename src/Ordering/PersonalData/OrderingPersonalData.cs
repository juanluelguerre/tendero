using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.PersonalData;

/// <summary>
/// The wallet's share of what the shop holds about a person (P15-9, ADR 0064): what
/// may be shown of each card — never a token — and, when the account goes, every
/// token forgotten at its provider first. One the provider cannot forget now waits
/// in the wallet for <c>ForgetSavedCardsWorker</c>; the methods are gone either way.
/// </summary>
public sealed class WalletPersonalData(IWalletRepository wallets, IPaymentProviderRegistry payments, TimeProvider clock) : IPersonalDataHolder
{
    public string Section => "paymentMethods";

    public async Task<object?> ExportAsync(CustomerId customer, CancellationToken cancellationToken = default) =>
        await wallets.FindAsync(customer, cancellationToken) is { } wallet
            ? wallet.Methods.Select(method => new
            {
                method.Brand,
                method.Last4,
                method.ExpiryMonth,
                method.ExpiryYear,
                IsDefault = wallet.Default == method.Id,
                method.SavedAt,
            }).ToList()
            : null;

    public async Task<Erasure> EraseAsync(CustomerId customer, CancellationToken cancellationToken = default)
    {
        if (await wallets.FindAsync(customer, cancellationToken) is not { } wallet)
            return new Erasure(Section, Forgotten: false, Kept: false);

        foreach (var method in wallet.Methods.ToList())
        {
            if ((await payments.Get(method.ProviderKey).ForgetInstrumentAsync(method.Token, cancellationToken)).Succeeded)
                wallet.Remove(clock, method.Id);
            else
                wallet.SetAsideForForgetting(clock, method.Id);
        }

        // Kept only while a token waits to be forgotten: the worker drops it, and the
        // row holds nothing of the person's but that credential's way out.
        if (wallet.ToForget.Count == 0)
            wallets.Remove(wallet);

        return new Erasure(Section, Forgotten: true, Kept: false);
    }
}

/// <summary>
/// The orders' share (P15-9): every order, with the addresses it froze, in the
/// download — and **kept** when the account goes, because the law makes a shop
/// keep its invoices (six years, the Commercial Code, art. 30), and **blocked**:
/// held for that obligation and used for nothing else (LOPDGDD art. 32). Their
/// owner is the customer the account leaves behind, anonymised.
/// </summary>
public sealed class OrdersPersonalData(IOrderReader orders) : IPersonalDataHolder
{
    public string Section => "orders";

    public async Task<object?> ExportAsync(CustomerId customer, CancellationToken cancellationToken = default)
    {
        var placed = await orders.ForCustomerAsync(customer, cancellationToken);
        return placed.Count == 0
            ? null
            : placed.Select(order => new
            {
                OrderId = order.Id.ToString(),
                order.CreatedAt,
                Status = order.Status.ToString(),
                order.ShippingAddress,
                order.BillingAddress,
                Lines = order.Lines.Select(line => new { line.Sku, line.ProductName, line.Quantity }),
                Total = order.Totals.Total,
            }).ToList();
    }

    public async Task<Erasure> EraseAsync(CustomerId customer, CancellationToken cancellationToken = default) =>
        new(Section, Forgotten: false, Kept: (await orders.ForCustomerAsync(customer, cancellationToken)).Count > 0);
}
