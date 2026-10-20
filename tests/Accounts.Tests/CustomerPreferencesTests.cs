using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests;

/// <summary>
/// A customer's preferences (P15-6, docs/specs/shopper-account.md, decision 7): the
/// language the shop speaks to them in, chosen once and not overwritten by the
/// next sign-in; and one marketing consent, off until given, kept with its date
/// and the wording it was given to, withdrawable.
/// </summary>
public sealed class CustomerPreferencesTests
{
    private static Customer Ana(TestClock clock) => Customer.Identified(clock, "ana", "Ana García", "es", Segments.Retail);

    [Fact]
    public void A_language_seen_at_sign_in_is_kept_until_the_customer_chooses_one()
    {
        var clock = new TestClock();
        var ana = Ana(clock);

        ana.ObserveCulture(clock, "en");
        Assert.Equal("en", ana.Culture);

        ana.ChooseCulture(clock, "es");
        ana.ObserveCulture(clock, "en");

        Assert.Equal("es", ana.Culture);
        Assert.True(ana.CultureChosen);
    }

    [Fact]
    public void A_language_the_shop_does_not_speak_is_refused()
    {
        var clock = new TestClock();

        Assert.Throws<ArgumentException>(() => Ana(clock).ChooseCulture(clock, "fr"));
    }

    [Fact]
    public void Marketing_is_off_until_given()
    {
        var ana = Ana(new TestClock());

        Assert.Null(ana.Marketing);
        Assert.False(ana.MayBeSentMarketing);
    }

    [Fact]
    public void A_consent_is_kept_with_its_date_and_the_wording_it_was_given_to()
    {
        var clock = new TestClock();
        var ana = Ana(clock);
        clock.Advance(TimeSpan.FromDays(3));

        ana.GiveMarketingConsent(clock, "marketing-2026-10");

        Assert.Equal(new MarketingConsent(true, clock.GetUtcNow(), "marketing-2026-10"), ana.Marketing);
        Assert.True(ana.MayBeSentMarketing);
    }

    /// <summary>Withdrawn is a fact with its own date, not the absence of one: it says when the shop was told to stop.</summary>
    [Fact]
    public void A_withdrawn_consent_is_kept_as_withdrawn_with_when()
    {
        var clock = new TestClock();
        var ana = Ana(clock);
        ana.GiveMarketingConsent(clock, "marketing-2026-10");
        clock.Advance(TimeSpan.FromDays(10));

        ana.WithdrawMarketingConsent(clock, "marketing-2026-10");

        Assert.Equal(new MarketingConsent(false, clock.GetUtcNow(), "marketing-2026-10"), ana.Marketing);
        Assert.False(ana.MayBeSentMarketing);
    }

    [Fact]
    public void Giving_again_what_was_already_given_changes_nothing()
    {
        var clock = new TestClock();
        var ana = Ana(clock);
        ana.GiveMarketingConsent(clock, "marketing-2026-10");
        var first = ana.Marketing;
        clock.Advance(TimeSpan.FromDays(1));

        ana.GiveMarketingConsent(clock, "marketing-2026-10");

        Assert.Equal(first, ana.Marketing);
    }

    /// <summary>New wording is a new consent: what the shopper agreed to is part of what they agreed.</summary>
    [Fact]
    public void Giving_it_to_new_wording_is_a_new_consent()
    {
        var clock = new TestClock();
        var ana = Ana(clock);
        ana.GiveMarketingConsent(clock, "marketing-2026-10");
        clock.Advance(TimeSpan.FromDays(1));

        ana.GiveMarketingConsent(clock, "marketing-2027-01");

        Assert.Equal(("marketing-2027-01", clock.GetUtcNow()), (ana.Marketing!.Wording, ana.Marketing.At));
    }

    [Fact]
    public void A_guest_has_no_preferences_to_set()
    {
        var clock = new TestClock();
        var guest = Customer.Guest(clock, "es", Segments.Retail);

        Assert.Throws<InvalidOperationException>(() => guest.GiveMarketingConsent(clock, "marketing-2026-10"));
        Assert.Throws<InvalidOperationException>(() => guest.ChooseCulture(clock, "en"));
    }
}
