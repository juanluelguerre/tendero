using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests;

/// <summary>
/// The aggregate's invariants.
///
/// The one they all serve: **a guest is a Customer with no subject, not the
/// absence of one.** An order placed by somebody who never made an account still
/// belongs to a person, and modelling that person as null spreads the special
/// case into every screen that shows an order.
/// </summary>
public sealed class CustomerTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public void A_guest_is_a_customer_with_no_subject()
    {
        var guest = Customer.Guest(Clock, "es", Segments.Retail);

        Assert.True(guest.IsGuest);
        Assert.Null(guest.Subject);
        Assert.NotEqual(default, guest.Id);
        Assert.Single(guest.DomainEvents);
    }

    [Fact]
    public void Linking_a_subject_makes_a_guest_somebody()
    {
        var guest = Customer.Guest(Clock, "es", Segments.Retail);
        var id = guest.Id;

        guest.LinkIdentity(Clock, "ana", "Ana Ruiz");

        Assert.False(guest.IsGuest);
        Assert.Equal("ana", guest.Subject);
        // The id survives, which is the entire point: orders already name it.
        Assert.Equal(id, guest.Id);
    }

    /// <summary>
    /// Idempotent by subject, like every operation here that can arrive twice.
    /// A display name still updates, because refusing would freeze whatever the
    /// provider happened to return the first time.
    /// </summary>
    [Fact]
    public void Linking_the_same_subject_twice_is_not_an_error()
    {
        var customer = Customer.Identified(Clock, "ana", "Ana", "es", Segments.Retail);

        customer.LinkIdentity(Clock, "ana", "Ana Ruiz");

        Assert.Equal("ana", customer.Subject);
        Assert.Equal("Ana Ruiz", customer.DisplayName);
    }

    /// <summary>A customer with two identities is two people sharing an order
    /// history.</summary>
    [Fact]
    public void A_customer_cannot_have_two_identities()
    {
        var customer = Customer.Identified(Clock, "ana", "Ana", "es", Segments.Retail);

        var refused = Assert.Throws<InvalidOperationException>(
            () => customer.LinkIdentity(Clock, "juanlu", "Juan Luis"));

        Assert.Contains("two identities", refused.Message);
    }

    [Fact]
    public void A_guest_can_be_merged_into_an_account()
    {
        var guest = Customer.Guest(Clock, "es", Segments.Retail);
        var survivor = CustomerId.New();

        guest.Supersede(Clock, survivor);

        Assert.Equal(survivor, guest.SupersededBy);
    }

    /// <summary>
    /// Somebody with an identity of their own is not absorbed. Two real accounts
    /// merging is a decision a person makes with evidence, not something a sign
    /// in does on their behalf.
    /// </summary>
    [Fact]
    public void An_account_cannot_be_merged_away()
    {
        var customer = Customer.Identified(Clock, "ana", "Ana", "es", Segments.Retail);

        Assert.Throws<InvalidOperationException>(
            () => customer.Supersede(Clock, CustomerId.New()));
    }

    [Fact]
    public void A_customer_cannot_supersede_itself()
    {
        var guest = Customer.Guest(Clock, "es", Segments.Retail);

        Assert.Throws<InvalidOperationException>(() => guest.Supersede(Clock, guest.Id));
    }

    /// <summary>A superseded customer is history. Letting one be edited would
    /// mean two rows disagreeing about the same person.</summary>
    [Fact]
    public void A_superseded_customer_cannot_change()
    {
        var guest = Customer.Guest(Clock, "es", Segments.Retail);
        guest.Supersede(Clock, CustomerId.New());

        Assert.Throws<InvalidOperationException>(
            () => guest.LinkIdentity(Clock, "ana", "Ana"));
    }

    /// <summary>
    /// A closed set, because the segment SELECTS A TARIFF: a typo would quote
    /// everybody the default and look like a pricing bug rather than a typo.
    /// </summary>
    [Theory]
    [InlineData("retail")]
    [InlineData("VIP")]
    [InlineData("  vip  ")]
    public void A_known_segment_is_accepted_however_it_was_typed(string segment) =>
        Assert.Contains(Segments.Normalise(segment), Segments.All);

    [Theory]
    [InlineData("gold")]
    [InlineData("")]
    public void An_unknown_segment_is_refused(string segment) =>
        Assert.Throws<InvalidOperationException>(() => Segments.Normalise(segment));

    [Fact]
    public void The_shop_remembers_the_language_it_last_saw_somebody_in()
    {
        var customer = Customer.Guest(Clock, "es", Segments.Retail);

        customer.UseCulture(Clock, "en");

        Assert.Equal("en", customer.Culture);
    }
}
