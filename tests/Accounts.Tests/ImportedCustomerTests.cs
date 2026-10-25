using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests;

/// <summary>
/// A customer brought from a store the merchant is leaving (phase 16, S2b,
/// P16-17): with their email, the store's id for them, their name and their
/// marketing consent as the store recorded it — dated then, not now — and the
/// identity provisioned for them when there is one. Without one they come as a
/// guest the shop can still address.
/// </summary>
public sealed class ImportedCustomerTests
{
    private static readonly CustomerOrigin Shopify = new("shopify", "6001");
    private static readonly DateTimeOffset ConsentedThen = new(2025, 11, 3, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_imported_customer_keeps_their_email_where_they_came_from_and_their_dated_consent()
    {
        var clock = new TestClock();
        var consent = new MarketingConsent(true, ConsentedThen, "Imported from shopify: consent given to the store on 2025-11-03");

        var ana = Customer.Imported(clock, Shopify, " Ana@Example.com ", "Ana Ruiz", "es", consent, subject: "kc-ana");

        Assert.Equal(("ana@example.com", "Ana Ruiz", "kc-ana"), (ana.Email, ana.DisplayName, ana.Subject));
        Assert.Equal(Shopify, ana.ImportedFrom);
        Assert.Equal(consent, ana.Marketing);
        Assert.True(ana.MayBeSentMarketing);
        Assert.Equal(Segments.Retail, ana.Segment);
    }

    /// <summary>With no identity provisioned, a guest with an email: their orders still have an owner.</summary>
    [Fact]
    public void Without_an_identity_an_imported_customer_is_a_guest_with_an_email()
    {
        var luis = Customer.Imported(new TestClock(), Shopify, "luis@example.com", "Luis Gómez", "es", marketing: null, subject: null);

        Assert.True(luis.IsGuest);
        Assert.Equal("luis@example.com", luis.Email);
        Assert.False(luis.MayBeSentMarketing);
    }

    /// <summary>
    /// Somebody the shop already had, found in the store by their email: the same
    /// person, told where else they came from, once. A second store id would be two
    /// people's orders landing on one.
    /// </summary>
    [Fact]
    public void A_customer_the_shop_had_is_told_where_else_they_came_from_once()
    {
        var clock = new TestClock();
        var ana = Customer.Identified(clock, "ana", "Ana Ruiz", "es", Segments.Retail);

        ana.LinkOrigin(clock, Shopify);
        ana.LinkOrigin(clock, Shopify);

        Assert.Equal(Shopify, ana.ImportedFrom);
        Assert.Throws<InvalidOperationException>(() => ana.LinkOrigin(clock, new CustomerOrigin("shopify", "6999")));
    }

    /// <summary>
    /// Linking is a change like any other: dated, and refused for somebody erased.
    /// Found by phase 16 S2b's Stryker close: both checks survived being removed.
    /// </summary>
    [Fact]
    public void Linking_is_dated_and_refused_for_somebody_erased()
    {
        var clock = new TestClock();
        var ana = Customer.Identified(clock, "ana", "Ana Ruiz", "es", Segments.Retail);
        var later = clock.Advance(TimeSpan.FromDays(3));

        ana.LinkOrigin(clock, Shopify);
        Assert.Equal(later, ana.UpdatedAt);

        var luis = Customer.Identified(clock, "luis", "Luis Gómez", "es", Segments.Retail);
        luis.Erase(clock);
        Assert.Throws<InvalidOperationException>(() => luis.LinkOrigin(clock, new CustomerOrigin("shopify", "6002")));
    }

    [Fact]
    public void An_imported_customer_needs_an_email() =>
        Assert.Throws<ArgumentException>(() => Customer.Imported(new TestClock(), Shopify, " ", "Ana", "es", null, null));

    /// <summary>
    /// Erasing clears the email with the rest of who they were. The store's id is
    /// kept: it is what stops the next import from bringing back somebody who asked
    /// to be forgotten.
    /// </summary>
    [Fact]
    public void Erasing_an_imported_customer_clears_their_email_and_keeps_where_they_came_from()
    {
        var clock = new TestClock();
        var ana = Customer.Imported(clock, Shopify, "ana@example.com", "Ana Ruiz", "es", null, "kc-ana");

        ana.Erase(clock);

        Assert.Equal((null, null, null), (ana.Email, ana.DisplayName, ana.Subject));
        Assert.Equal(Shopify, ana.ImportedFrom);
    }
}
