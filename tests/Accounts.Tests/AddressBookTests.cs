using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests;

/// <summary>
/// A customer's address book (P15-2, docs/specs/shopper-account.md): entries the
/// shopper names, one default for delivery and one for billing, ten at most — and
/// a default removed leaves none, because a default applied without being shown is
/// an order sent to the wrong house.
/// </summary>
public sealed class AddressBookTests
{
    private static readonly Address Home = Address.Create("Ana García", "Calle Mayor 12", "3º B", "Madrid", null, "28013", "ES");
    private static readonly Address Work = Address.Create("Ana García", "Paseo de la Castellana 89", null, "Madrid", null, "28046", "es");

    private static (AddressBook Book, TestClock Clock) Fresh()
    {
        var clock = new TestClock();
        return (AddressBook.StartFor(CustomerId.New(), clock), clock);
    }

    [Fact]
    public void An_entry_is_the_address_and_the_label_the_shopper_wrote()
    {
        var (book, clock) = Fresh();

        var entry = book.Add(clock, "  Home ", Home);

        var kept = Assert.Single(book.Entries);
        Assert.Equal(entry.Id, kept.Id);
        Assert.Equal("Home", kept.Label);
        Assert.Equal(Home, kept.Address);
    }

    [Fact]
    public void The_first_entry_is_not_made_a_default_by_itself()
    {
        var (book, clock) = Fresh();

        book.Add(clock, "Home", Home);

        Assert.Null(book.DeliveryDefault);
        Assert.Null(book.BillingDefault);
    }

    [Fact]
    public void One_entry_may_be_the_default_for_delivery_and_for_billing()
    {
        var (book, clock) = Fresh();
        var home = book.Add(clock, "Home", Home);

        Assert.True(book.UseFor(clock, home.Id, AddressUse.Delivery));
        Assert.True(book.UseFor(clock, home.Id, AddressUse.Billing));

        Assert.Equal(home.Id, book.DeliveryDefault);
        Assert.Equal(home.Id, book.BillingDefault);
    }

    [Fact]
    public void A_new_default_replaces_the_old_one_of_its_kind_only()
    {
        var (book, clock) = Fresh();
        var home = book.Add(clock, "Home", Home, delivery: true, billing: true);
        var work = book.Add(clock, "Work", Work);

        book.UseFor(clock, work.Id, AddressUse.Delivery);

        Assert.Equal(work.Id, book.DeliveryDefault);
        Assert.Equal(home.Id, book.BillingDefault);
    }

    [Fact]
    public void Removing_the_default_leaves_no_default_and_promotes_nothing()
    {
        var (book, clock) = Fresh();
        var home = book.Add(clock, "Home", Home, delivery: true, billing: true);
        book.Add(clock, "Work", Work);

        Assert.True(book.Remove(clock, home.Id));

        Assert.Null(book.DeliveryDefault);
        Assert.Null(book.BillingDefault);
        Assert.Equal(["Work"], book.Entries.Select(entry => entry.Label));
    }

    [Fact]
    public void Removing_an_entry_that_is_not_there_changes_nothing()
    {
        var (book, clock) = Fresh();
        var home = book.Add(clock, "Home", Home, delivery: true);
        var before = book.UpdatedAt;
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.False(book.Remove(clock, AddressEntryId.New()));

        Assert.Equal(home.Id, book.DeliveryDefault);
        Assert.Equal(before, book.UpdatedAt);
    }

    [Fact]
    public void Editing_an_entry_changes_its_address_and_label_and_keeps_its_identity_and_defaults()
    {
        var (book, clock) = Fresh();
        var home = book.Add(clock, "Home", Home, delivery: true);

        Assert.True(book.Edit(clock, home.Id, "Our flat", Work, delivery: true, billing: false));

        var edited = Assert.Single(book.Entries);
        Assert.Equal(home.Id, edited.Id);
        Assert.Equal("Our flat", edited.Label);
        Assert.Equal(Work, edited.Address);
        Assert.Equal(home.Id, book.DeliveryDefault);
    }

    [Fact]
    public void Unticking_a_default_while_editing_leaves_no_default_of_that_kind()
    {
        var (book, clock) = Fresh();
        var home = book.Add(clock, "Home", Home, delivery: true, billing: true);

        book.Edit(clock, home.Id, "Home", Home, delivery: false, billing: true);

        Assert.Null(book.DeliveryDefault);
        Assert.Equal(home.Id, book.BillingDefault);
    }

    [Fact]
    public void An_address_handed_out_before_an_edit_is_the_address_it_was()
    {
        // What an order froze is a value: an edit replaces the entry's address,
        // it does not change the one already handed out.
        var (book, clock) = Fresh();
        var home = book.Add(clock, "Home", Home);
        var frozen = book.Entries.Single().Address;

        book.Edit(clock, home.Id, "Home", Work, delivery: false, billing: false);

        Assert.Equal(Home, frozen);
        Assert.Equal(Work, book.Entries.Single().Address);
    }

    [Fact]
    public void Editing_an_entry_that_is_not_there_answers_false()
    {
        var (book, clock) = Fresh();

        Assert.False(book.Edit(clock, AddressEntryId.New(), "Home", Home, delivery: false, billing: false));
        Assert.False(book.UseFor(clock, AddressEntryId.New(), AddressUse.Delivery));
    }

    [Fact]
    public void The_book_holds_ten_entries_and_refuses_an_eleventh()
    {
        var (book, clock) = Fresh();

        for (var i = 0; i < AddressBook.MaxEntries; i++)
        {
            Assert.False(book.IsFull);
            book.Add(clock, $"Entry {i}", Home);
        }

        Assert.True(book.IsFull);
        Assert.Throws<InvalidOperationException>(() => book.Add(clock, "One more", Home));
        Assert.Equal(AddressBook.MaxEntries, book.Entries.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_label_is_required(string label)
    {
        var (book, clock) = Fresh();

        Assert.Throws<ArgumentException>(() => book.Add(clock, label, Home));
        Assert.Empty(book.Entries);
    }

    [Fact]
    public void A_label_longer_than_its_limit_is_refused()
    {
        var (book, clock) = Fresh();

        Assert.Throws<ArgumentException>(() => book.Add(clock, new string('a', AddressBook.MaxLabelLength + 1), Home));
        book.Add(clock, new string('a', AddressBook.MaxLabelLength), Home);
    }

    [Fact]
    public void Entries_are_kept_in_the_order_they_were_added()
    {
        var (book, clock) = Fresh();

        book.Add(clock, "Home", Home);
        book.Add(clock, "Work", Work);
        book.Add(clock, "Mum's", Home);

        Assert.Equal(["Home", "Work", "Mum's"], book.Entries.Select(entry => entry.Label));
    }

    [Fact]
    public void A_change_moves_the_book_s_updated_time()
    {
        var (book, clock) = Fresh();
        var created = book.CreatedAt;
        clock.Advance(TimeSpan.FromHours(1));

        book.Add(clock, "Home", Home);

        Assert.Equal(created, book.CreatedAt);
        Assert.Equal(clock.GetUtcNow(), book.UpdatedAt);
    }

    [Fact]
    public void An_entry_without_an_address_is_refused()
    {
        var (book, clock) = Fresh();
        var home = book.Add(clock, "Home", Home);

        Assert.Throws<ArgumentNullException>(() => book.Add(clock, "Work", null!));
        Assert.Throws<ArgumentNullException>(() => book.Edit(clock, home.Id, "Home", null!, delivery: false, billing: false));
        Assert.Equal(Home, Assert.Single(book.Entries).Address);
    }
}
