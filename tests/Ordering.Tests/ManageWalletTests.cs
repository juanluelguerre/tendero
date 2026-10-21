using ElGuerre.Tendero.Ordering.Adapters;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.ManageWallet;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// The wallet's endpoints behind the dispatcher (P15-4): always the caller's own
/// wallet, a full one refused before the provider is asked, the provider's refusal
/// and silence told apart, and a card the provider could not forget gone from the
/// wallet anyway — waiting, out of sight, for the worker.
/// </summary>
public sealed class ManageWalletTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_card_is_kept_in_the_callers_own_wallet_and_read_back_without_its_token()
    {
        var world = World.Linked();

        var kept = await world.Handlers.HandleAsync(new KeepCardCommand(FakePaymentProvider.CardOk, MakeDefault: true), Ct);
        var read = await world.Reader.HandleAsync(new GetWalletQuery(), Ct);

        Assert.Equal(WalletOutcome.Done, kept.Outcome);
        var method = Assert.Single(read.Methods);
        Assert.Equal(("Visa", "4242", false, true), (method.Brand, method.Last4, method.Expired, method.IsDefault));
        Assert.Equal(world.Customer, Assert.Single(world.Store).CustomerId);
        // What the wire carries is what may be shown: no member holds the token.
        Assert.DoesNotContain(typeof(SavedMethodView).GetProperties(), property => property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, world.Saves);
    }

    [Fact]
    public async Task Somebody_not_yet_linked_is_refused_and_nothing_is_kept()
    {
        var world = World.Unlinked();

        var kept = await world.Handlers.HandleAsync(new KeepCardCommand(FakePaymentProvider.CardOk, false), Ct);

        Assert.Equal(WalletOutcome.NoCustomer, kept.Outcome);
        Assert.Empty(world.Store);
        Assert.Equal(0, world.Provider.Saved);
    }

    /// <summary>A card kept at the provider and then not recorded would be a token nobody can forget, so a full wallet never asks.</summary>
    [Fact]
    public async Task A_full_wallet_refuses_before_the_provider_is_asked()
    {
        var world = World.Linked();
        for (var i = 0; i < Wallet.MaxMethods; i++)
            await world.Handlers.HandleAsync(new KeepCardCommand(FakePaymentProvider.CardOk, false), Ct);
        var asked = world.Provider.Saved;

        var answer = await world.Handlers.HandleAsync(new KeepCardCommand(FakePaymentProvider.CardOk, false), Ct);

        Assert.Equal(WalletOutcome.Full, answer.Outcome);
        Assert.Equal(asked, world.Provider.Saved);
        Assert.Equal(Wallet.MaxMethods, answer.View.Methods.Count);
    }

    [Fact]
    public async Task The_provider_refusing_and_the_provider_silent_are_two_answers_and_neither_starts_a_wallet()
    {
        var world = World.Linked();

        var refused = await world.Handlers.HandleAsync(new KeepCardCommand("not-a-card", false), Ct);
        var silent = await world.Handlers.HandleAsync(new KeepCardCommand(FakePaymentProvider.CardUnreachable, false), Ct);

        Assert.Equal((WalletOutcome.CardRefused, WalletOutcome.ProviderUnavailable), (refused.Outcome, silent.Outcome));
        Assert.False(string.IsNullOrWhiteSpace(refused.Reason));
        Assert.Empty(world.Store);
        Assert.Equal(0, world.Saves);
    }

    [Fact]
    public async Task Removing_a_card_forgets_it_at_the_provider_and_leaves_no_default()
    {
        var world = World.Linked();
        var id = Id(await world.Handlers.HandleAsync(new KeepCardCommand(FakePaymentProvider.CardOk, true), Ct));
        var token = world.Store.Single().Methods.Single().Token;

        var removed = await world.Handlers.HandleAsync(new RemoveCardCommand(id), Ct);

        Assert.Equal(WalletOutcome.Done, removed.Outcome);
        Assert.Empty(removed.View.Methods);
        Assert.Equal([token], world.Provider.Forgotten);
        Assert.Empty(world.Store.Single().ToForget);
        Assert.Null(world.Store.Single().Default);
    }

    /// <summary>The provider is down: the card is gone from the wallet now, and its token waits for the worker.</summary>
    [Fact]
    public async Task A_card_the_provider_cannot_forget_now_is_gone_from_the_wallet_and_waits_to_be_forgotten()
    {
        var world = World.Linked();
        var id = Id(await world.Handlers.HandleAsync(new KeepCardCommand(FakePaymentProvider.CardOk, true), Ct));
        world.Provider.Down = true;

        var removed = await world.Handlers.HandleAsync(new RemoveCardCommand(id), Ct);

        Assert.Equal(WalletOutcome.Done, removed.Outcome);
        Assert.Empty(removed.View.Methods);
        Assert.Single(world.Store.Single().ToForget);
    }

    [Fact]
    public async Task Removing_or_choosing_a_card_that_is_not_there_is_an_unknown_method()
    {
        var world = World.Linked();

        Assert.Equal(WalletOutcome.UnknownMethod, (await world.Handlers.HandleAsync(new RemoveCardCommand(SavedMethodId.New()), Ct)).Outcome);
        Assert.Equal(WalletOutcome.UnknownMethod, (await world.Handlers.HandleAsync(new UseCardByDefaultCommand(SavedMethodId.New()), Ct)).Outcome);
        Assert.Empty(world.Store);
    }

    [Fact]
    public async Task Choosing_the_default_answers_the_wallet_as_it_is_now()
    {
        var world = World.Linked();
        await world.Handlers.HandleAsync(new KeepCardCommand(FakePaymentProvider.CardOk, true), Ct);
        var master = Id(await world.Handlers.HandleAsync(new KeepCardCommand(FakePaymentProvider.CardMastercard, false), Ct), last: true);

        var chosen = await world.Handlers.HandleAsync(new UseCardByDefaultCommand(master), Ct);

        Assert.Equal([false, true], chosen.View.Methods.Select(method => method.IsDefault));
    }

    [Fact]
    public async Task An_expired_card_says_so_in_the_wallet()
    {
        var world = World.Linked();

        var kept = await world.Handlers.HandleAsync(new KeepCardCommand(FakePaymentProvider.CardExpired, false), Ct);

        Assert.True(Assert.Single(kept.View.Methods).Expired);
    }

    /// <summary>The worker offers every waiting token again, and drops the ones the provider confirms.</summary>
    [Fact]
    public async Task The_worker_forgets_what_waited_once_the_provider_answers()
    {
        var world = World.Linked();
        var id = Id(await world.Handlers.HandleAsync(new KeepCardCommand(FakePaymentProvider.CardOk, false), Ct));
        world.Provider.Down = true;
        await world.Handlers.HandleAsync(new RemoveCardCommand(id), Ct);
        world.Provider.Down = false;
        var savesBefore = world.Saves;

        var forgotten = await world.Worker().ForgetAsync(Ct);

        Assert.Equal(1, forgotten);
        Assert.Empty(world.Store.Single().ToForget);
        // Saved: what the provider forgot is dropped from the row, not only from memory.
        Assert.Equal(savesBefore + 1, world.Saves);
    }

    [Fact]
    public void The_instrument_never_reaches_the_audit_log() =>
        Assert.True(typeof(KeepCardCommand).GetProperty(nameof(KeepCardCommand.Instrument))!.IsDefined(typeof(AuditRedactedAttribute), inherit: true));

    [Theory]
    [InlineData("", false)]
    [InlineData(FakePaymentProvider.CardOk, true)]
    public void An_instrument_is_required(string instrument, bool valid) =>
        Assert.Equal(valid, new KeepCardValidator().Validate(new KeepCardCommand(instrument, false)).IsValid);

    private static SavedMethodId Id(WalletResult result, bool last = false) =>
        new(Guid.Parse((last ? result.View.Methods[^1] : result.View.Methods[0]).Id));

    private sealed class World
    {
        private readonly FixedPrincipal principal;
        private readonly InMemoryWallets wallets;

        private World(CustomerId? customer)
        {
            Customer = customer;
            this.principal = new FixedPrincipal(new CommercePrincipal(customer, null, "ana", "Ana García", [TenderoRoles.Shopper]));
            this.wallets = new InMemoryWallets(Store);
            Handlers = new ManageWalletHandlers(this.wallets, new OneProvider(Provider), Options.Create(new CheckoutOptions()), this.principal, UnitOfWork, Clock);
            Reader = new GetWalletHandler(this.wallets, this.principal, Clock);
        }

        public static World Linked() => new(CustomerId.New());

        public static World Unlinked() => new(null);

        public CustomerId? Customer { get; }
        public TestClock Clock { get; } = new();
        public CountingProvider Provider { get; } = new();
        public List<Wallet> Store { get; } = [];
        public CountingUnitOfWork UnitOfWork { get; } = new();
        public int Saves => UnitOfWork.SaveCount;
        public ManageWalletHandlers Handlers { get; }
        public GetWalletHandler Reader { get; }

        public ForgetSavedCardsWorker Worker()
        {
            var services = new ServiceCollection()
                .AddSingleton<IWalletRepository>(this.wallets)
                .AddSingleton<IPaymentProviderRegistry>(new OneProvider(Provider))
                .AddSingleton<IUnitOfWork>(UnitOfWork)
                .BuildServiceProvider();
            return new ForgetSavedCardsWorker(services.GetRequiredService<IServiceScopeFactory>(), Clock, NullLogger<ForgetSavedCardsWorker>.Instance);
        }
    }

    /// <summary>The fake provider, counting what it was asked to keep and forget, and able to go down for forgetting.</summary>
    private sealed class CountingProvider : IPaymentProvider
    {
        private readonly FakePaymentProvider fake = new(Options.Create(new FakePaymentOptions()), new TestClock());

        public int Saved { get; private set; }
        public List<string> Forgotten { get; } = [];
        public bool Down { get; set; }

        public string Key => FakePaymentProvider.Key;

        public Task<InstrumentSaving> SaveInstrumentAsync(string instrument, CancellationToken cancellationToken = default)
        {
            Saved++;
            return this.fake.SaveInstrumentAsync(instrument, cancellationToken);
        }

        public Task<InstrumentForgetting> ForgetInstrumentAsync(string token, CancellationToken cancellationToken = default)
        {
            if (Down)
                return Task.FromResult(InstrumentForgetting.Unavailable("down"));

            Forgotten.Add(token);
            return this.fake.ForgetInstrumentAsync(token, cancellationToken);
        }

        public Task<PaymentAuthorization> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default) => this.fake.AuthorizeAsync(request, cancellationToken);
        public Task<PaymentCapture> CaptureAsync(string authorizationId, Money amount, CancellationToken cancellationToken = default) => this.fake.CaptureAsync(authorizationId, amount, cancellationToken);
        public Task<PaymentRefund> RefundAsync(string captureId, Money amount, CancellationToken cancellationToken = default) => this.fake.RefundAsync(captureId, amount, cancellationToken);
        public Task<PaymentVoid> VoidAsync(string authorizationId, CancellationToken cancellationToken = default) => this.fake.VoidAsync(authorizationId, cancellationToken);
        public WebhookVerification VerifyWebhook(string rawBody, IReadOnlyDictionary<string, string> headers, DateTimeOffset now) => this.fake.VerifyWebhook(rawBody, headers, now);
    }

    private sealed class OneProvider(IPaymentProvider provider) : IPaymentProviderRegistry
    {
        public IReadOnlyCollection<string> Keys => [provider.Key];

        public IPaymentProvider Get(string key) => provider;
    }

    private sealed class InMemoryWallets(List<Wallet> store) : IWalletRepository, IWalletReader
    {
        public Task<Wallet?> FindAsync(CustomerId customer, CancellationToken ct = default) =>
            Task.FromResult(store.Find(wallet => wallet.CustomerId == customer));

        public void Add(Wallet wallet) => store.Add(wallet);

        public void Remove(Wallet wallet) => store.Remove(wallet);

        public Task<IReadOnlyList<Wallet>> WithTokensToForgetAsync(int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Wallet>>([.. store.Where(wallet => wallet.ToForget.Count > 0).Take(limit)]);

        public Task<Wallet?> ForCustomerAsync(CustomerId customer, CancellationToken ct = default) =>
            Task.FromResult(store.Find(wallet => wallet.CustomerId == customer));
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
