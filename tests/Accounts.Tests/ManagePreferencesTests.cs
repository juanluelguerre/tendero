using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Features.ManagePreferences;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests;

/// <summary>
/// The preferences' endpoints behind the dispatcher (P15-6): the caller's own
/// customer, never a guest's; a language chosen, a consent given and withdrawn to
/// the current wording, each saved and each answered as it is now.
/// </summary>
public sealed class ManagePreferencesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_signed_in_customer_reads_what_the_shop_holds_and_marketing_is_off_until_given()
    {
        var world = World.Of(Customer.Identified(new TestClock(), "ana", "Ana García", "es", Segments.Retail));

        var read = await world.Handlers.HandleAsync(new GetPreferencesQuery(), Ct);

        Assert.Equal(PreferencesOutcome.Done, read.Outcome);
        Assert.Equal(("es", false, null, MarketingWordings.Current), (read.View!.Culture, read.View.CultureChosen, read.View.Marketing, read.View.MarketingWording));
    }

    [Fact]
    public async Task Choosing_a_language_and_giving_then_withdrawing_consent_are_saved_and_answered()
    {
        var world = World.Of(Customer.Identified(new TestClock(), "ana", "Ana García", "es", Segments.Retail));

        var chosen = await world.Handlers.HandleAsync(new ChooseCultureCommand("en"), Ct);
        var given = await world.Handlers.HandleAsync(new SetMarketingConsentCommand(true), Ct);
        world.Clock.Advance(TimeSpan.FromDays(2));
        var withdrawn = await world.Handlers.HandleAsync(new SetMarketingConsentCommand(false), Ct);

        Assert.Equal(("en", true), (chosen.View!.Culture, chosen.View.CultureChosen));
        Assert.True(given.View!.Marketing!.Given);
        Assert.Equal((false, world.Clock.GetUtcNow(), MarketingWordings.Current), (withdrawn.View!.Marketing!.Given, withdrawn.View.Marketing.At, withdrawn.View.Marketing.Wording));
        Assert.Equal(3, world.Saves);
    }

    [Fact]
    public async Task Somebody_not_linked_and_a_guest_have_no_preferences()
    {
        var unlinked = World.Of(null);
        var guest = World.Of(Customer.Guest(new TestClock(), "es", Segments.Retail));

        Assert.Equal(PreferencesOutcome.NoCustomer, (await unlinked.Handlers.HandleAsync(new SetMarketingConsentCommand(true), Ct)).Outcome);
        Assert.Equal(PreferencesOutcome.NoCustomer, (await guest.Handlers.HandleAsync(new ChooseCultureCommand("en"), Ct)).Outcome);
        Assert.Equal(0, unlinked.Saves + guest.Saves);
    }

    [Theory]
    [InlineData("es", true)]
    [InlineData("EN", true)]
    [InlineData("fr", false)]
    [InlineData("", false)]
    public void A_language_is_one_the_shop_speaks(string culture, bool valid) =>
        Assert.Equal(valid, new ChooseCultureValidator().Validate(new ChooseCultureCommand(culture)).IsValid);

    private sealed class World
    {
        private World(Customer? customer)
        {
            var principal = new FixedPrincipal(new CommercePrincipal(customer?.Id, null, "ana", "Ana García", [TenderoRoles.Shopper]));
            Handlers = new ManagePreferencesHandlers(new OneCustomer(customer), principal, UnitOfWork, Clock);
        }

        public static World Of(Customer? customer) => new(customer);

        public TestClock Clock { get; } = new();
        public CountingUnitOfWork UnitOfWork { get; } = new();
        public int Saves => UnitOfWork.SaveCount;
        public ManagePreferencesHandlers Handlers { get; }
    }

    private sealed class OneCustomer(Customer? customer) : ICustomerRepository
    {
        public Task<Customer?> FindByIdAsync(CustomerId id, CancellationToken ct) =>
            Task.FromResult(customer?.Id == id ? customer : null);

        public Task<Customer?> FindBySubjectAsync(string subject, CancellationToken ct) =>
            Task.FromResult(customer?.Subject == subject ? customer : null);

        public void Add(Customer added) => throw new NotSupportedException();
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
