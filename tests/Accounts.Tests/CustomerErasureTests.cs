using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests;

/// <summary>
/// Removing an account (P15-9, docs/specs/shopper-account.md): the customer is kept
/// and anonymised, so an order's owner still resolves and a later sign-in with the
/// same identity starts a new customer — and nothing can make an erased customer
/// somebody again.
/// </summary>
public sealed class CustomerErasureTests
{
    [Fact]
    public void Erasing_clears_who_they_were_and_what_they_chose_and_keeps_the_customer()
    {
        var clock = new TestClock();
        var ana = Customer.Identified(clock, "ana", "Ana García", "es", Segments.Retail);
        ana.ChooseCulture(clock, "en");
        ana.GiveMarketingConsent(clock, "marketing-2026-10");
        var id = ana.Id;
        clock.Advance(TimeSpan.FromDays(1));

        ana.Erase(clock);

        Assert.Equal(id, ana.Id);
        Assert.Equal((null, null, null, false), (ana.Subject, ana.DisplayName, ana.Marketing, ana.CultureChosen));
        Assert.Equal(clock.GetUtcNow(), ana.ErasedAt);
        Assert.True(ana.IsErased);
    }

    /// <summary>A guest id an erased customer once had, offered at a later sign-in, must not bring the person back.</summary>
    [Fact]
    public void An_erased_customer_cannot_be_given_an_identity_again()
    {
        var clock = new TestClock();
        var ana = Customer.Identified(clock, "ana", "Ana García", "es", Segments.Retail);
        ana.Erase(clock);

        Assert.Throws<InvalidOperationException>(() => ana.LinkIdentity(clock, "ana", "Ana García"));
        Assert.Throws<InvalidOperationException>(() => ana.Erase(clock));
    }

    [Fact]
    public void A_guest_is_erased_as_a_customer_is()
    {
        var clock = new TestClock();
        var guest = Customer.Guest(clock, "es", Segments.Retail);

        guest.Erase(clock);

        Assert.True(guest.IsErased);
    }
}
