using System.Runtime.CompilerServices;
using ElGuerre.Tendero.Accounts.Adapters;
using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Features.ImportCustomers;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.Accounts.Tests.Sources;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests;

/// <summary>A customer source written in memory: the contract suite's control, and the slice's.</summary>
internal sealed class FakeCustomerSource : ICustomerSourceConnector
{
    public const string Key = "fake";

    public static readonly DateTimeOffset ConsentedThen = new(2025, 11, 3, 18, 0, 0, TimeSpan.Zero);

    public List<SourceCustomer> Customers { get; } =
    [
        new("6001", "Ana@Example.com", "Ana", "Ruiz", "es", new SourceConsent(true, ConsentedThen),
        [
            new SourceAddress("Ana Ruiz", "Calle Mayor 1", null, "Madrid", null, "28013", "ES", "+34600000001", IsDefault: true),
            new SourceAddress("Ana Ruiz", "Carrer de Mallorca 1", null, "Barcelona", null, "08013", "ES", null, IsDefault: false),
        ]),
        new("6002", "luis@example.com", "Luis", "Gómez", null, null, []),
    ];

    public List<CustomerNote> Notes { get; } = [];

    public string Source => Key;

    public IReadOnlyList<CustomerNote> SourceNotes => Notes;

    public async IAsyncEnumerable<SourceCustomer> StreamCustomersAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        foreach (var customer in Customers)
            yield return customer;
    }
}

public sealed class FakeCustomerSourceContractTests : CustomerSourceConnectorContractTests
{
    protected override ICustomerSourceConnector CreateConnector() => new FakeCustomerSource();
}

/// <summary>The slice's fake provisioner passes the contract Keycloak's adapter does: the control of the suite.</summary>
public sealed class FakeIdentityProvisionerContractTests : ElGuerre.Tendero.Accounts.Tests.Identity.IdentityProvisionerContractTests
{
    protected override IIdentityProvisioner CreateProvisioner() => new ImportCustomersTests.Provisioner();
}

/// <summary>
/// Bringing a store's customers (phase 16, S2b, P16-17; spec decision S2b-1, A):
/// each one a customer with an identity provisioned for them — no password, the
/// provider's *forgot password* sets one — their addresses in their book and their
/// consent as the store dated it. Imported once: a re-import finds them by the
/// store's id, and a person the shop already had, by email, is linked and not
/// duplicated. Somebody erased here is not brought back.
/// </summary>
public sealed class ImportCustomersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Shop
    {
        public FakeCustomerSource Source { get; } = new();

        public Customers Customers { get; } = new();

        public List<AddressBook> Books { get; } = [];

        public Provisioner Provisioner { get; } = new();

        public CountingUnitOfWork UnitOfWork { get; } = new();

        public TestClock Clock { get; } = new();

        public bool WithProvisioner { get; set; } = true;

        public RehearsedCustomers Rehearsed { get; } = new();

        public Task<ImportCustomersResult> ImportAsync(bool rehearse = false, string culture = "es") =>
            new ImportCustomersHandler(
                    new OneSource(Source), Customers, Customers, new Books(Books), UnitOfWork, Clock,
                    WithProvisioner ? [Provisioner] : [], Rehearsed)
                .HandleAsync(new ImportCustomersCommand(FakeCustomerSource.Key, culture, rehearse), Ct);
    }

    [Fact]
    public async Task Each_customer_comes_with_an_identity_their_addresses_and_their_dated_consent()
    {
        var shop = new Shop();

        var result = await shop.ImportAsync();

        Assert.Equal((2, 0, 0), (result.Created, result.AlreadyImported, result.Linked));
        var ana = shop.Customers.All.Single(customer => customer.Email == "ana@example.com");
        Assert.Equal(("kc-ana@example.com", "Ana Ruiz", new CustomerOrigin("fake", "6001")), (ana.Subject, ana.DisplayName, ana.ImportedFrom));
        Assert.Equal((true, FakeCustomerSource.ConsentedThen), (ana.Marketing!.Given, ana.Marketing.At));
        Assert.Equal("imported:fake", ana.Marketing.Wording);

        var book = Assert.Single(shop.Books, each => each.CustomerId == ana.Id);
        Assert.Equal(["Madrid", "Barcelona"], book.Entries.Select(entry => entry.Address.City));
        Assert.Equal(book.Entries[0].Id, book.DeliveryDefault);
        Assert.Equal(1, shop.UnitOfWork.SaveCount);
    }

    /// <summary>Importing again finds each one by the store's id and makes nobody twice.</summary>
    [Fact]
    public async Task A_second_import_brings_nobody_twice()
    {
        var shop = new Shop();
        await shop.ImportAsync();

        var again = await shop.ImportAsync();

        Assert.Equal((0, 2), (again.Created, again.AlreadyImported));
        Assert.Equal(2, shop.Customers.All.Count);
        Assert.Equal(2, shop.Provisioner.Made.Count);
    }

    /// <summary>
    /// A person the shop already had: the provider answers with the identity that
    /// had their email, and the customer of that identity is linked to the store's
    /// id, not made again.
    /// </summary>
    [Fact]
    public async Task A_person_the_shop_had_is_linked_through_their_identity_not_duplicated()
    {
        var shop = new Shop();
        var ana = Customer.Identified(shop.Clock, "kc-ana@example.com", "Ana Ruiz", "es", Segments.Retail);
        shop.Customers.All.Add(ana);

        var result = await shop.ImportAsync(culture: "en");

        Assert.Equal((1, 1), (result.Created, result.Linked));
        Assert.Equal(new CustomerOrigin("fake", "6001"), ana.ImportedFrom);
        Assert.Contains(result.Notes, note => note.Code == CustomerNoteCode.AlreadyHere && note.About == "6001");
    }

    /// <summary>Somebody erased here asked to be forgotten; the store still remembering them changes nothing.</summary>
    [Fact]
    public async Task Somebody_erased_here_is_not_brought_back()
    {
        var shop = new Shop();
        await shop.ImportAsync();
        shop.Customers.All.Single(customer => customer.Email == "ana@example.com").Erase(shop.Clock);

        var again = await shop.ImportAsync();

        Assert.Equal(0, again.Created);
        Assert.Contains(again.Notes, note => note.Code == CustomerNoteCode.Erased && note.About == "6001");
    }

    /// <summary>Without a provisioner, or when it fails, a guest with an email: the orders still have an owner.</summary>
    [Fact]
    public async Task Without_an_identity_a_customer_comes_as_a_guest_and_it_is_said()
    {
        var shop = new Shop();
        shop.Provisioner.FailFor.Add("luis@example.com");

        var result = await shop.ImportAsync();

        Assert.True(shop.Customers.All.Single(customer => customer.Email == "luis@example.com").IsGuest);
        Assert.Contains(result.Notes, note => note.Code == CustomerNoteCode.IdentityNotProvisioned && note.About == "6002");

        var noProvisioner = new Shop { WithProvisioner = false };
        var guests = await noProvisioner.ImportAsync();
        Assert.All(noProvisioner.Customers.All, customer => Assert.True(customer.IsGuest));
        Assert.Contains(guests.Notes, note => note.Code == CustomerNoteCode.IdentityNotProvisioned && note.About is null);
    }

    /// <summary>A customer with no email cannot sign in or be written to: said, not imported.</summary>
    [Fact]
    public async Task A_customer_without_an_email_is_said_not_imported()
    {
        var shop = new Shop();
        shop.Source.Customers.Add(new SourceCustomer("6003", null, "Sin", "Correo", null, null, []));

        var result = await shop.ImportAsync();

        Assert.Equal(2, result.Created);
        Assert.Contains(result.Notes, note => note.Code == CustomerNoteCode.NoEmail && note.About == "6003");
    }

    /// <summary>An address the shop cannot read costs the address, never the customer.</summary>
    [Fact]
    public async Task An_unreadable_address_costs_the_address_not_the_customer()
    {
        var shop = new Shop();
        shop.Source.Customers[1] = shop.Source.Customers[1] with
        {
            Addresses = [new SourceAddress("Luis Gómez", " ", null, "Sevilla", null, "41001", "ES", null, IsDefault: true)],
        };

        var result = await shop.ImportAsync();

        Assert.Equal(2, result.Created);
        Assert.Contains(result.Notes, note => note.Code == CustomerNoteCode.AddressUnreadable && note.About == "6002");
    }

    /// <summary>The rehearsal (P16-13's rule, carried over): what would come and the notes, nothing written, nobody provisioned.</summary>
    [Fact]
    public async Task A_rehearsal_says_what_would_come_and_provisions_nobody()
    {
        var shop = new Shop();

        var rehearsal = await shop.ImportAsync(rehearse: true);

        Assert.Equal(2, rehearsal.Created);
        Assert.Empty(shop.Customers.All);
        Assert.Empty(shop.Books);
        Assert.Empty(shop.Provisioner.Made);
        Assert.Equal(0, shop.UnitOfWork.SaveCount);
    }

    /// <summary>
    /// A rehearsal holds who it would bring, for an order rehearsed in the same request
    /// to find its buyer (P16-21); an import for real holds nobody there.
    /// </summary>
    [Fact]
    public async Task A_rehearsal_holds_who_it_would_bring_and_an_import_for_real_nobody()
    {
        var shop = new Shop();

        await shop.ImportAsync();
        Assert.Empty(shop.Rehearsed.Of(FakeCustomerSource.Key));

        var again = new Shop();
        await again.ImportAsync(rehearse: true);
        Assert.Equal(["6001", "6002"], again.Rehearsed.Of(FakeCustomerSource.Key).Keys.Order(StringComparer.Ordinal));
        Assert.Empty(again.Rehearsed.Of("another"));
    }

    /// <summary>Each note in the language asked, starting with what it is about.</summary>
    [Fact]
    public async Task The_notes_are_said_in_the_language_asked()
    {
        var shop = new Shop();
        shop.Source.Customers.Add(new SourceCustomer("6003", null, null, null, null, null, []));

        var spanish = (await shop.ImportAsync(rehearse: true, culture: "es")).Notes.Single();
        var english = (await shop.ImportAsync(rehearse: true, culture: "en")).Notes.Single();

        Assert.NotEqual(spanish.Text, english.Text);
        Assert.StartsWith("6003: ", english.Text, StringComparison.Ordinal);
    }

    private sealed class OneSource(ICustomerSourceConnector source) : ICustomerSourceRegistry
    {
        public IReadOnlyCollection<string> Sources => [source.Source];

        public ICustomerSourceConnector Get(string name) => source;
    }

    private sealed class Customers : ICustomerRepository, IImportedCustomers
    {
        public List<Customer> All { get; } = [];

        public Task<Customer?> FindByIdAsync(CustomerId id, CancellationToken ct) => Task.FromResult(All.Find(customer => customer.Id == id));

        public Task<Customer?> FindBySubjectAsync(string subject, CancellationToken ct) => Task.FromResult(All.Find(customer => customer.Subject == subject));

        public void Add(Customer customer) => All.Add(customer);

        public Task<Customer?> FindByOriginAsync(CustomerOrigin origin, CancellationToken ct) =>
            Task.FromResult(All.Find(customer => customer.ImportedFrom == origin));
    }

    private sealed class Books(List<AddressBook> store) : IAddressBookRepository
    {
        public Task<AddressBook?> FindAsync(CustomerId customer, CancellationToken ct = default) =>
            Task.FromResult(store.Find(book => book.CustomerId == customer));

        public void Add(AddressBook book) => store.Add(book);

        public void Remove(AddressBook book) => store.Remove(book);
    }

    /// <summary>Makes an identity per email, once; refuses the emails it is told to.</summary>
    internal sealed class Provisioner : IIdentityProvisioner
    {
        public List<string> Made { get; } = [];

        public HashSet<string> FailFor { get; } = [];

        public Task<string> ProvisionAsync(IdentityToProvision person, CancellationToken cancellationToken = default)
        {
            var email = person.Email.ToLowerInvariant();
            if (FailFor.Contains(email))
                throw new InvalidOperationException("the provider refused");

            if (!Made.Contains(email))
                Made.Add(email);
            return Task.FromResult($"kc-{email}");
        }
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
