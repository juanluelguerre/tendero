using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Ordering.Adapters;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Checkout;
using ElGuerre.Tendero.Ordering.Features.ManageWallet;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// A saved card, from the wallet to the till and back out (P15-4), against a real
/// Postgres: kept through the provider, charged at checkout by the id the shop
/// minted — never the token, which the browser never holds — and, once removed,
/// a card the shop will not charge again. The demo the spec names, as a test.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SavedCardCheckoutTests(PostgresFixture postgres)
{
    private const string Sku = "B05DEFG606-DEFAULT";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_kept_card_pays_by_its_id_and_once_removed_pays_for_nothing()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var ana = new Shopper(CustomerId.New());

        await using var shop = await Shop.OpenAsync(postgres, "wallet_checkout", ana);
        await shop.StockAsync(Sku, 5, ct);

        var kept = await shop.SendAsync(new KeepCardCommand(FakePaymentProvider.CardOk, MakeDefault: true), ct);
        var visa = Assert.Single(kept.View.Methods);
        Assert.Equal((WalletOutcome.Done, "4242", true), (kept.Outcome, visa.Last4, visa.IsDefault));
        var id = new SavedMethodId(Guid.Parse(visa.Id));

        var first = await PlaceAsync(shop, id, "checkout-saved-1", ct);
        Assert.Equal(PlaceOrderOutcome.Placed, first.Outcome);

        var removed = await shop.SendAsync(new RemoveCardCommand(id), ct);
        Assert.Equal(WalletOutcome.Done, removed.Outcome);
        Assert.Empty(removed.View.Methods);

        var second = await PlaceAsync(shop, id, "checkout-saved-2", ct);
        Assert.Equal(PlaceOrderOutcome.PaymentDeclined, second.Outcome);

        // Forgotten at the provider at once, so nothing waits in the wallet's row either.
        await using var context = shop.Factory.Create();
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(each => each.CustomerId == ana.Current.Customer!.Value, ct);
        Assert.Empty(wallet.Methods);
        Assert.Empty(wallet.ToForget);
        Assert.Null(wallet.Default);
    }

    /// <summary>Another shopper's id is not a card of this one's: declined, and nothing is charged.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_saved_card_of_somebody_else_pays_for_nothing()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using var shop = await Shop.OpenAsync(postgres, "wallet_stranger", new Shopper(CustomerId.New()));
        await shop.StockAsync(Sku, 5, ct);
        var theirs = Assert.Single((await shop.SendAsync(new KeepCardCommand(FakePaymentProvider.CardOk, false), ct)).View.Methods);

        await using var stranger = await Shop.OpenOnAsync(shop.Factory, new Shopper(CustomerId.New()));
        var placed = await PlaceAsync(stranger, new SavedMethodId(Guid.Parse(theirs.Id)), "checkout-stranger", ct);

        Assert.Equal(PlaceOrderOutcome.PaymentDeclined, placed.Outcome);
    }

    /// <summary>The wallets with a token still to forget are found in SQL, and only those.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_wallets_waiting_to_forget_a_token_are_found_and_only_those()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var clock = new TestClock();

        await using var shop = await Shop.OpenAsync(postgres, "wallet_to_forget");
        await using (var context = shop.Factory.Create())
        {
            var waiting = Wallet.StartFor(CustomerId.New(), clock);
            var method = waiting.Add(clock, "fake", new CardOnFile("tok_waiting", "Visa", "4242", 8, 2028));
            waiting.SetAsideForForgetting(clock, method.Id);

            var quiet = Wallet.StartFor(CustomerId.New(), clock);
            quiet.Add(clock, "fake", new CardOnFile("tok_quiet", "Visa", "4444", 8, 2028));

            context.Wallets.AddRange(waiting, quiet);
            await context.SaveChangesAsync(ct);
        }

        var found = await shop.ReadAsync<IWalletRepository, IReadOnlyList<Wallet>>(wallets => wallets.WithTokensToForgetAsync(10, ct));

        var wallet = Assert.Single(found);
        Assert.Equal("tok_waiting", Assert.Single(wallet.ToForget).Token);
    }

    private static async Task<PlaceOrderResult> PlaceAsync(Shop shop, SavedMethodId method, string key, CancellationToken ct)
    {
        var cart = await shop.AddToCartAsync(null, Sku, 1, ct);
        var option = (await shop.ShippingOptionsAsync(cart.Token, ct)).Options[0];
        var quote = await shop.QuoteAsync(cart.Token, option.Amount, ct);

        return await shop.SendAsync(
            new PlaceOrderCommand(cart.Token, Shop.ShipTo, null, option.Code, quote.InputHash, "", key, [], "es", method), ct);
    }

    /// <summary>A signed-in person with a customer behind them: the only principal a wallet answers.</summary>
    private sealed class Shopper(CustomerId customer) : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } = new(customer, null, "ana", "Ana García", [TenderoRoles.Shopper]);
    }
}
