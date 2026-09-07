using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Features.LinkIdentity;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests;

/// <summary>
/// What signing in does, and it is three different things.
///
/// The three outcomes are the whole reason this is a domain operation rather
/// than plumbing: registering somebody new, letting a guest BECOME them, and
/// recognising an account they already had are different facts, and confusing
/// them is how a person loses the order they placed yesterday.
/// </summary>
public sealed class LinkIdentityTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public async Task Somebody_the_shop_has_never_seen_is_registered()
    {
        var world = World.WithSubject("ana");

        var result = await world.Link(new LinkIdentityCommand("Ana Ruiz", "es", Guest: null));

        Assert.Equal("registered", result.Outcome);
        Assert.Equal("Ana Ruiz", result.DisplayName);
        Assert.Equal(Segments.Retail, result.Segment);
        Assert.Single(world.Customers);
        Assert.Equal(1, world.Saves);
    }

    /// <summary>
    /// The case the whole operation exists for. A guest becomes the person, and
    /// the ID DOES NOT CHANGE — so an order placed an hour ago as a guest is
    /// still theirs, without anything being rewritten.
    /// </summary>
    [Fact]
    public async Task A_guest_becomes_the_person_and_keeps_their_id()
    {
        var world = World.WithSubject("ana");
        var guest = world.AddGuest();

        var result = await world.Link(new LinkIdentityCommand("Ana Ruiz", "es", guest.Id));

        Assert.Equal("linked", result.Outcome);
        Assert.Equal(guest.Id.Value.ToString(), result.CustomerId);
        Assert.Equal("ana", guest.Subject);
        Assert.False(guest.IsGuest);
        // No second customer: linking is not registering.
        Assert.Single(world.Customers);
    }

    /// <summary>
    /// They already had an account — shopping as a guest on a device where they
    /// later sign in. The guest points at the survivor rather than being
    /// deleted, because orders placed as the guest still name that id, and a
    /// merge that rewrote them is how a refund reaches the wrong person.
    /// </summary>
    [Fact]
    public async Task An_existing_account_absorbs_the_guest_without_deleting_it()
    {
        var world = World.WithSubject("ana");
        var account = world.AddIdentified("ana", "Ana Ruiz");
        var guest = world.AddGuest();

        var result = await world.Link(new LinkIdentityCommand("Ana Ruiz", "es", guest.Id));

        Assert.Equal("recognised", result.Outcome);
        Assert.Equal(account.Id.Value.ToString(), result.CustomerId);
        Assert.Equal(account.Id, guest.SupersededBy);
        // Still there. History is not tidied away.
        Assert.Equal(2, world.Customers.Count);
    }

    [Fact]
    public async Task Signing_in_twice_changes_nothing_the_second_time()
    {
        var world = World.WithSubject("ana");
        var account = world.AddIdentified("ana", "Ana Ruiz");

        var result = await world.Link(new LinkIdentityCommand("Ana Ruiz", "es", Guest: null));

        Assert.Equal("recognised", result.Outcome);
        Assert.Equal(account.Id.Value.ToString(), result.CustomerId);
        Assert.Single(world.Customers);
    }

    /// <summary>
    /// The security boundary, and the reason the merge refuses anything that is
    /// not a guest.
    ///
    /// The guest id arrives from the CALLER. A handler that merged whatever id
    /// it was given would let anybody absorb anybody else's account by guessing
    /// a GUID — and the guess is not hard, because the id is in the caller's own
    /// order confirmation.
    /// </summary>
    [Fact]
    public async Task Somebody_elses_ACCOUNT_cannot_be_absorbed_by_offering_its_id()
    {
        var world = World.WithSubject("ana");
        var victim = world.AddIdentified("juanlu", "Juan Luis");

        var result = await world.Link(new LinkIdentityCommand("Ana Ruiz", "es", victim.Id));

        // Ana gets her own new account, and Juan Luis's is untouched.
        Assert.Equal("registered", result.Outcome);
        Assert.NotEqual(victim.Id.Value.ToString(), result.CustomerId);
        Assert.Null(victim.SupersededBy);
        Assert.Equal("juanlu", victim.Subject);
    }

    [Fact]
    public async Task A_guest_that_was_already_merged_is_not_merged_again()
    {
        var world = World.WithSubject("ana");
        var account = world.AddIdentified("ana", "Ana Ruiz");
        var guest = world.AddGuest();
        guest.Supersede(Clock, account.Id);

        var result = await world.Link(new LinkIdentityCommand("Ana Ruiz", "es", guest.Id));

        Assert.Equal("recognised", result.Outcome);
        Assert.Equal(account.Id, guest.SupersededBy);
    }

    /// <summary>The subject comes from the token and nowhere else. Without one
    /// there is nobody to link, and the refusal says so rather than inventing an
    /// anonymous account.</summary>
    [Fact]
    public async Task Without_an_authenticated_subject_there_is_nobody_to_link()
    {
        var world = World.Anonymous();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Link(new LinkIdentityCommand(null, "es", Guest: null)));
    }

    [Fact]
    public async Task Signing_in_updates_the_language_the_shop_remembers()
    {
        var world = World.WithSubject("ana");
        var account = world.AddIdentified("ana", "Ana Ruiz");

        await world.Link(new LinkIdentityCommand("Ana Ruiz", "en", Guest: null));

        Assert.Equal("en", account.Culture);
    }

    // ---------- fixtures ----------

    private sealed class World
    {
        private readonly InMemoryCustomers customers = new();
        private readonly CountingUnitOfWork unitOfWork = new();
        private readonly LinkIdentityHandler handler;

        private World(CommercePrincipal principal) =>
            this.handler = new LinkIdentityHandler(
                this.customers, new FixedPrincipal(principal), this.unitOfWork, Clock);

        public static World WithSubject(string subject) =>
            new(new CommercePrincipal(null, null, subject, subject, ["shopper"]));

        public static World Anonymous() => new(CommercePrincipal.Anonymous);

        public List<Customer> Customers => this.customers.All;
        public int Saves => this.unitOfWork.SaveCount;

        public Customer AddGuest()
        {
            var guest = Customer.Guest(Clock, "es", Segments.Retail);
            this.customers.Add(guest);
            return guest;
        }

        public Customer AddIdentified(string subject, string name)
        {
            var customer = Customer.Identified(Clock, subject, name, "es", Segments.Retail);
            this.customers.Add(customer);
            return customer;
        }

        public Task<LinkedIdentity> Link(LinkIdentityCommand command) =>
            this.handler.HandleAsync(command, TestContext.Current.CancellationToken);
    }

    /// <summary>A list, not a mock: what matters is what the repository ended up
    /// holding, and a list says that more clearly than a verification.</summary>
    private sealed class InMemoryCustomers : ICustomerRepository
    {
        public List<Customer> All { get; } = [];

        public Task<Customer?> FindByIdAsync(CustomerId id, CancellationToken ct) =>
            Task.FromResult(All.Find(customer => customer.Id == id));

        public Task<Customer?> FindBySubjectAsync(string subject, CancellationToken ct) =>
            Task.FromResult(All.Find(customer => customer.Subject == subject));

        public void Add(Customer customer) => All.Add(customer);
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
