using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// A customer's saved ways to pay (P15-4, docs/specs/shopper-account.md): the
/// provider's tokens and what may be shown of each card, one default, five at most;
/// a default removed leaves none, and a method that could not be forgotten at the
/// provider stops being offered while it waits to be.
/// </summary>
public sealed class WalletTests
{
    private static CardOnFile Card(string last4, int month = 8, int year = 2028) =>
        new($"tok_{Guid.NewGuid():N}", "Visa", last4, month, year);

    private static (Wallet Wallet, TestClock Clock) Fresh()
    {
        var clock = new TestClock();
        return (Wallet.StartFor(CustomerId.New(), clock), clock);
    }

    [Fact]
    public void A_kept_card_is_its_token_the_provider_and_what_may_be_shown()
    {
        var (wallet, clock) = Fresh();
        var card = Card("4242");

        var method = wallet.Add(clock, "fake", card);

        var kept = Assert.Single(wallet.Methods);
        Assert.Equal(method.Id, kept.Id);
        Assert.Equal(("fake", card.Token, "Visa", "4242", 8, 2028), (kept.ProviderKey, kept.Token, kept.Brand, kept.Last4, kept.ExpiryMonth, kept.ExpiryYear));
        Assert.Equal(clock.GetUtcNow(), kept.SavedAt);
    }

    [Fact]
    public void The_first_card_is_not_made_the_default_by_itself()
    {
        var (wallet, clock) = Fresh();

        wallet.Add(clock, "fake", Card("4242"));

        Assert.Null(wallet.Default);
    }

    [Fact]
    public void One_method_is_the_default_and_a_new_default_replaces_it()
    {
        var (wallet, clock) = Fresh();
        var visa = wallet.Add(clock, "fake", Card("4242"), makeDefault: true);
        var master = wallet.Add(clock, "fake", Card("4444"));

        Assert.Equal(visa.Id, wallet.Default);
        Assert.True(wallet.UseByDefault(clock, master.Id));
        Assert.Equal(master.Id, wallet.Default);
    }

    [Fact]
    public void Removing_the_default_leaves_no_default_and_promotes_nothing()
    {
        var (wallet, clock) = Fresh();
        var visa = wallet.Add(clock, "fake", Card("4242"), makeDefault: true);
        wallet.Add(clock, "fake", Card("4444"));

        var removed = wallet.Remove(clock, visa.Id);

        Assert.Equal(visa.Token, removed?.Token);
        Assert.Null(wallet.Default);
        Assert.Equal(["4444"], wallet.Methods.Select(method => method.Last4));
    }

    [Fact]
    public void Removing_a_method_that_is_not_there_changes_nothing()
    {
        var (wallet, clock) = Fresh();
        wallet.Add(clock, "fake", Card("4242"), makeDefault: true);

        Assert.Null(wallet.Remove(clock, SavedMethodId.New()));
        Assert.False(wallet.UseByDefault(clock, SavedMethodId.New()));
        Assert.Single(wallet.Methods);
    }

    /// <summary>
    /// The provider could not be reached to forget it: the method leaves the wallet
    /// at once — never offered again — and waits, token and all, to be forgotten.
    /// </summary>
    [Fact]
    public void A_method_waiting_to_be_forgotten_is_no_longer_offered_and_keeps_its_token_until_it_is()
    {
        var (wallet, clock) = Fresh();
        var visa = wallet.Add(clock, "fake", Card("4242"), makeDefault: true);

        Assert.True(wallet.SetAsideForForgetting(clock, visa.Id));

        Assert.Empty(wallet.Methods);
        Assert.Null(wallet.Default);
        var waiting = Assert.Single(wallet.ToForget);
        Assert.Equal(("fake", visa.Token), (waiting.ProviderKey, waiting.Token));

        wallet.Forgotten(clock, visa.Token);

        Assert.Empty(wallet.ToForget);
    }

    [Fact]
    public void The_wallet_holds_five_methods_and_refuses_a_sixth()
    {
        var (wallet, clock) = Fresh();

        for (var i = 0; i < Wallet.MaxMethods; i++)
        {
            Assert.False(wallet.IsFull);
            wallet.Add(clock, "fake", Card($"00{i}0"));
        }

        Assert.True(wallet.IsFull);
        Assert.Throws<InvalidOperationException>(() => wallet.Add(clock, "fake", Card("9999")));
    }

    /// <summary>Good through the last day of its month; expired from the first of the next.</summary>
    [Theory]
    [InlineData(2028, 8, 31, false)]
    [InlineData(2028, 9, 1, true)]
    [InlineData(2027, 12, 31, false)]
    [InlineData(2029, 1, 1, true)]
    public void A_card_expires_at_the_end_of_its_month(int year, int month, int day, bool expired)
    {
        var (wallet, clock) = Fresh();
        var method = wallet.Add(clock, "fake", Card("4242", 8, 2028));

        Assert.Equal(expired, method.HasExpiredAt(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void A_live_method_is_found_by_its_id_and_one_set_aside_is_not()
    {
        var (wallet, clock) = Fresh();
        var visa = wallet.Add(clock, "fake", Card("4242"));
        var master = wallet.Add(clock, "fake", Card("4444"));
        wallet.SetAsideForForgetting(clock, master.Id);

        Assert.Equal(visa.Token, wallet.Find(visa.Id)?.Token);
        Assert.Null(wallet.Find(master.Id));
    }
}
