using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Features.ManageAddresses;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests;

/// <summary>
/// The address book's endpoints behind the dispatcher (P15-2): always the caller's
/// own book, nothing kept for somebody not yet linked, a full book refused before
/// it is touched, an address the kernel refuses said so — and nothing saved unless
/// something changed.
/// </summary>
public sealed class ManageAddressesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly SavedAddress Home = new("Ana García", "Calle Mayor 12", "3º B", "Madrid", null, "28013", "es", null);
    private static readonly SavedAddress Work = new("Ana García", "Paseo de la Castellana 89", null, "Madrid", null, "28046", "ES", null);

    [Fact]
    public async Task An_address_is_kept_in_the_callers_own_book_and_read_back()
    {
        var world = World.Linked();

        var answer = await world.Handlers.HandleAsync(new AddAddressCommand("Home", Home, DeliveryDefault: true, BillingDefault: false), Ct);
        var read = await world.Reader.HandleAsync(new GetAddressBookQuery(), Ct);

        Assert.Equal(AddressBookOutcome.Done, answer.Outcome);
        var entry = Assert.Single(read.Entries);
        Assert.Equal(("Home", true, false), (entry.Label, entry.DeliveryDefault, entry.BillingDefault));
        // The kernel's own normalisation: the country as two capitals.
        Assert.Equal("ES", entry.Address.CountryCode);
        Assert.Equal(AddressBook.MaxEntries, read.MaxEntries);
        Assert.Equal(world.Customer, Assert.Single(world.Store).CustomerId);
        Assert.Equal(1, world.Saves);
    }

    [Fact]
    public async Task Somebody_not_yet_linked_is_refused_and_nothing_is_stored()
    {
        var world = World.Unlinked();

        var answer = await world.Handlers.HandleAsync(new AddAddressCommand("Home", Home, false, false), Ct);
        var read = await world.Reader.HandleAsync(new GetAddressBookQuery(), Ct);

        Assert.Equal(AddressBookOutcome.NoCustomer, answer.Outcome);
        Assert.Empty(read.Entries);
        Assert.Empty(world.Store);
        Assert.Equal(0, world.Saves);
    }

    [Fact]
    public async Task A_full_book_refuses_an_eleventh_and_saves_nothing()
    {
        var world = World.Linked();
        for (var i = 0; i < AddressBook.MaxEntries; i++)
            await world.Handlers.HandleAsync(new AddAddressCommand($"Entry {i}", Home, false, false), Ct);

        var answer = await world.Handlers.HandleAsync(new AddAddressCommand("One more", Work, false, false), Ct);

        Assert.Equal(AddressBookOutcome.Full, answer.Outcome);
        Assert.Equal(AddressBook.MaxEntries, answer.View.Entries.Count);
        Assert.Equal(AddressBook.MaxEntries, world.Saves);
    }

    [Fact]
    public async Task An_address_the_kernel_refuses_is_said_so_with_its_reason_and_starts_no_book()
    {
        var world = World.Linked();

        var answer = await world.Handlers.HandleAsync(new AddAddressCommand("Home", Home with { CountryCode = "ESP" }, false, false), Ct);

        Assert.Equal(AddressBookOutcome.InvalidAddress, answer.Outcome);
        Assert.Contains("ESP", answer.Reason);
        Assert.Empty(answer.View.Entries);
        Assert.Empty(world.Store);
        Assert.Equal(0, world.Saves);
    }

    [Fact]
    public async Task Editing_removing_or_choosing_in_a_book_that_was_never_started_is_an_unknown_entry()
    {
        var world = World.Linked();
        var nobody = AddressEntryId.New();

        Assert.Equal(AddressBookOutcome.UnknownEntry, (await world.Handlers.HandleAsync(new EditAddressCommand(nobody, "Home", Home, false, false), Ct)).Outcome);
        Assert.Equal(AddressBookOutcome.UnknownEntry, (await world.Handlers.HandleAsync(new RemoveAddressCommand(nobody), Ct)).Outcome);
        Assert.Equal(AddressBookOutcome.UnknownEntry, (await world.Handlers.HandleAsync(new UseAddressCommand(nobody, AddressUse.Billing), Ct)).Outcome);
        Assert.Empty(world.Store);
        Assert.Equal(0, world.Saves);
    }

    [Fact]
    public async Task Removing_the_default_leaves_the_book_without_one()
    {
        var world = World.Linked();
        var added = await world.Handlers.HandleAsync(new AddAddressCommand("Home", Home, true, true), Ct);
        await world.Handlers.HandleAsync(new AddAddressCommand("Work", Work, false, false), Ct);
        var home = new AddressEntryId(Guid.Parse(added.View.Entries.Single().Id));

        var answer = await world.Handlers.HandleAsync(new RemoveAddressCommand(home), Ct);

        Assert.Equal(AddressBookOutcome.Done, answer.Outcome);
        var left = Assert.Single(answer.View.Entries);
        Assert.Equal(("Work", false, false), (left.Label, left.DeliveryDefault, left.BillingDefault));
    }

    [Fact]
    public async Task Choosing_a_default_and_editing_an_entry_answer_the_book_as_it_is_now()
    {
        var world = World.Linked();
        await world.Handlers.HandleAsync(new AddAddressCommand("Home", Home, true, false), Ct);
        var work = new AddressEntryId(Guid.Parse((await world.Handlers.HandleAsync(new AddAddressCommand("Work", Work, false, false), Ct)).View.Entries[1].Id));

        var chosen = await world.Handlers.HandleAsync(new UseAddressCommand(work, AddressUse.Delivery), Ct);
        var edited = await world.Handlers.HandleAsync(new EditAddressCommand(work, "Office", Work, true, true), Ct);

        Assert.Equal([false, true], chosen.View.Entries.Select(entry => entry.DeliveryDefault));
        Assert.Equal(["Home", "Office"], edited.View.Entries.Select(entry => entry.Label));
        Assert.True(edited.View.Entries[1].BillingDefault);
    }

    /// <summary>The audit row says an address was kept or changed, never where nor what the shopper calls it.</summary>
    [Theory]
    [InlineData(typeof(AddAddressCommand))]
    [InlineData(typeof(EditAddressCommand))]
    public void The_address_and_its_label_never_reach_the_audit_log(Type command)
    {
        foreach (var property in new[] { nameof(AddAddressCommand.Label), nameof(AddAddressCommand.Address) })
            Assert.True(command.GetProperty(property)!.IsDefined(typeof(AuditRedactedAttribute), inherit: true), $"{command.Name}.{property}");
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Home", true)]
    public void A_label_is_required(string label, bool valid)
    {
        Assert.Equal(valid, new AddAddressValidator().Validate(new AddAddressCommand(label, Home, false, false)).IsValid);
        Assert.Equal(valid, new EditAddressValidator().Validate(new EditAddressCommand(AddressEntryId.New(), label, Home, false, false)).IsValid);
    }

    [Theory]
    [InlineData(AddressBook.MaxLabelLength, true)]
    [InlineData(AddressBook.MaxLabelLength + 1, false)]
    public void A_label_is_as_long_as_the_book_keeps(int length, bool valid)
    {
        Assert.Equal(valid, new AddAddressValidator().Validate(new AddAddressCommand(new string('a', length), Home, false, false)).IsValid);
    }

    [Fact]
    public void An_entry_needs_its_address_and_the_fields_a_parcel_needs()
    {
        var validator = new AddAddressValidator();

        Assert.False(validator.Validate(new AddAddressCommand("Home", null!, false, false)).IsValid);
        Assert.False(validator.Validate(new AddAddressCommand("Home", Home with { Line1 = "" }, false, false)).IsValid);
        Assert.False(validator.Validate(new AddAddressCommand("Home", Home with { PostalCode = " " }, false, false)).IsValid);
        Assert.False(validator.Validate(new AddAddressCommand("Home", Home with { CountryCode = "ESP" }, false, false)).IsValid);
        Assert.False(validator.Validate(new AddAddressCommand("Home", Home with { City = new string('a', AddressEntryRules.MaxFieldLength + 1) }, false, false)).IsValid);
        Assert.True(validator.Validate(new AddAddressCommand("Home", Home with { Line2 = null, Phone = null }, false, false)).IsValid);
    }

    [Fact]
    public void A_default_is_for_delivery_or_for_billing()
    {
        Assert.False(new UseAddressValidator().Validate(new UseAddressCommand(AddressEntryId.New(), (AddressUse)7)).IsValid);
        Assert.True(new UseAddressValidator().Validate(new UseAddressCommand(AddressEntryId.New(), AddressUse.Billing)).IsValid);
    }

    private sealed class World
    {
        private World(CustomerId? customer)
        {
            Customer = customer;
            var principal = new FixedPrincipal(new CommercePrincipal(customer, null, "ana", "Ana García", [TenderoRoles.Shopper]));
            var books = new InMemoryBooks(Store);
            Handlers = new ManageAddressesHandlers(books, principal, UnitOfWork, Clock);
            Reader = new GetAddressBookHandler(books, principal);
        }

        public static World Linked() => new(CustomerId.New());

        public static World Unlinked() => new(null);

        public CustomerId? Customer { get; }
        public TestClock Clock { get; } = new();
        public List<AddressBook> Store { get; } = [];
        public CountingUnitOfWork UnitOfWork { get; } = new();
        public int Saves => UnitOfWork.SaveCount;
        public ManageAddressesHandlers Handlers { get; }
        public GetAddressBookHandler Reader { get; }
    }

    /// <summary>A list behind both ports: what matters is what the table ended up holding.</summary>
    private sealed class InMemoryBooks(List<AddressBook> store) : IAddressBookRepository, IAddressBookReader
    {
        public Task<AddressBook?> FindAsync(CustomerId customer, CancellationToken ct = default) =>
            Task.FromResult(store.Find(book => book.CustomerId == customer));

        public void Add(AddressBook book) => store.Add(book);

        public Task<AddressBook?> ForCustomerAsync(CustomerId customer, CancellationToken ct = default) =>
            Task.FromResult(store.Find(book => book.CustomerId == customer));
    }

    private sealed class FixedPrincipal(CommercePrincipal principal) : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } = principal;
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
