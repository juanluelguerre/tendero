using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Features.ManageAddresses;
using ElGuerre.Tendero.Accounts.Features.YourData;
using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Ordering.Adapters;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Checkout;
using ElGuerre.Tendero.Ordering.Features.ManageWallet;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The shopper's own data (P15-9), across both contexts and a real Postgres: one
/// download of everything the shop holds — never a token — and the account removed
/// whole: the book, the wallet and the trail forgotten, each card forgotten at its
/// provider, the orders kept with the addresses they froze, and the customer kept,
/// anonymised, as their owner.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class YourDataTests(PostgresFixture postgres)
{
    private const string Sku = "B05DEFG606-DEFAULT";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_shopper_downloads_what_the_shop_holds_and_removes_the_account()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var clock = new TestClock();
        // Imported from a store, with the identity provisioned for her (phase 16, S2b):
        // a signed-in shopper in every way, and one the shop also holds an email for.
        var ana = Customer.Imported(clock, new CustomerOrigin("shopify", "6001"), "ana@example.com", "Ana García", "es", null, "ana");
        var principal = new Shopper(ana.Id);

        await using var shop = await Shop.OpenAsync(postgres, "your_data", principal);
        await using (var context = shop.Factory.Create())
        {
            context.Customers.Add(ana);
            await context.SaveChangesAsync(ct);
        }

        await shop.StockAsync(Sku, 5, ct);
        await shop.SendAsync(new AddAddressCommand("Home", new SavedAddress("Ana García", "Calle Mayor 12", null, "Madrid", null, "28013", "ES", null), true, true), ct);
        var card = Assert.Single((await shop.SendAsync(new KeepCardCommand(FakePaymentProvider.CardOk, true), ct)).View.Methods);

        var cart = await shop.AddToCartAsync(null, Sku, 1, ct);
        var option = (await shop.ShippingOptionsAsync(cart.Token, ct)).Options[0];
        var quote = await shop.QuoteAsync(cart.Token, option.Amount, ct);
        var placed = await shop.SendAsync(new PlaceOrderCommand(
            cart.Token, Shop.ShipTo, null, option.Code, quote.InputHash, "", "checkout-your-data", [], "es", new SavedMethodId(Guid.Parse(card.Id))), ct);
        Assert.Equal(PlaceOrderOutcome.Placed, placed.Outcome);

        // ---- What the shop holds ----
        var export = await shop.AskAsync(new ExportPersonalDataQuery(), ct);

        Assert.NotNull(export);
        Assert.Equal(("Ana García", "ana@example.com", "es"), (export.Account.DisplayName, export.Account.Email, export.Account.Culture));
        Assert.Equal(["addresses", "orders", "paymentMethods"], export.Sections.Keys.Order(StringComparer.Ordinal));
        var downloaded = System.Text.Json.JsonSerializer.Serialize(export);
        Assert.DoesNotContain("tok_", downloaded, StringComparison.Ordinal);
        Assert.Contains("4242", downloaded, StringComparison.Ordinal);

        // ---- Removing the account ----
        var removal = await shop.SendAsync(new RemoveAccountCommand(), ct);

        Assert.Equal(AccountRemovalOutcome.Removed, removal.Outcome);
        Assert.Equal(["account", "addresses", "paymentMethods"], removal.View!.Forgotten.Order(StringComparer.Ordinal));
        Assert.Equal(["orders"], removal.View.Kept);
        Assert.Equal(6, removal.View.KeptForYears);

        await using var read = shop.Factory.Create();
        var erased = await read.Customers.AsNoTracking().SingleAsync(customer => customer.Id == ana.Id, ct);
        Assert.True(erased.IsErased);
        Assert.Equal((null, null), (erased.Subject, erased.DisplayName));
        Assert.False(await read.AddressBooks.AnyAsync(book => book.CustomerId == ana.Id, ct));
        Assert.False(await read.Wallets.AnyAsync(wallet => wallet.CustomerId == ana.Id, ct));
        var kept = await read.Orders.AsNoTracking().SingleAsync(order => order.CustomerId == ana.Id, ct);
        Assert.Equal(Shop.ShipTo, kept.ShippingAddress);

        // The removed account is nobody now: its download answers nothing.
        Assert.Null(await shop.AskAsync(new ExportPersonalDataQuery(), ct));
    }

    private sealed class Shopper(CustomerId customer) : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } = new(customer, null, "ana", "Ana García", [TenderoRoles.Shopper]);
    }
}
