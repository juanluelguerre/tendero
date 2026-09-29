using System.Reflection;
using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Features.ManageTrail;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests;

/// <summary>
/// The trail's endpoints behind the dispatcher (R-8): always the caller's own
/// trail, nothing kept for somebody not yet linked, a switch that sticks even
/// before anything was recorded, and the inputs bounded before they reach a rule.
/// </summary>
public sealed class ManageTrailTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_view_is_recorded_on_the_callers_own_trail_and_read_back()
    {
        var world = World.Linked();

        await world.Handlers.HandleAsync(new RecordBrowsingCommand(BrowsingKind.View, "b073wxyz01"), Ct);
        var answer = await world.Handlers.HandleAsync(new RecordBrowsingCommand(BrowsingKind.Search, "cafetera italiana"), Ct);
        var read = await world.Reader.HandleAsync(new GetBrowsingHistoryQuery(), Ct);

        Assert.Equal(["B073WXYZ01"], answer.Views.Select(view => view.Code));
        Assert.Equal(["cafetera italiana"], answer.Searches.Select(search => search.Text));
        Assert.Equal(answer.Views, read.Views);
        Assert.Equal(world.Customer, Assert.Single(world.Store).CustomerId);
    }

    /// <summary>Signed in and not linked yet: nobody to keep a trail for, so nothing is kept — an empty answer, not an error.</summary>
    [Fact]
    public async Task Somebody_not_yet_linked_gets_an_empty_trail_and_nothing_is_stored()
    {
        var world = World.Unlinked();

        var answer = await world.Handlers.HandleAsync(new RecordBrowsingCommand(BrowsingKind.View, "B073WXYZ01"), Ct);

        Assert.True(answer.Enabled);
        Assert.Empty(answer.Views);
        Assert.Empty(world.Store);
        Assert.Equal(0, world.Saves);
    }

    /// <summary>Switching off before anything was recorded still sticks: the next view is not kept.</summary>
    [Fact]
    public async Task Switched_off_the_trail_keeps_nothing_even_when_it_did_not_exist_yet()
    {
        var world = World.Linked();

        var off = await world.Handlers.HandleAsync(new SetBrowsingCommand(Enabled: false), Ct);
        var after = await world.Handlers.HandleAsync(new RecordBrowsingCommand(BrowsingKind.View, "B073WXYZ01"), Ct);

        Assert.False(off.Enabled);
        Assert.False(after.Enabled);
        Assert.Empty(after.Views);
    }

    [Fact]
    public async Task Switching_off_clears_what_was_kept()
    {
        var world = World.Linked();
        await world.Handlers.HandleAsync(new RecordBrowsingCommand(BrowsingKind.View, "B073WXYZ01"), Ct);

        await world.Handlers.HandleAsync(new SetBrowsingCommand(Enabled: false), Ct);
        var read = await world.Reader.HandleAsync(new GetBrowsingHistoryQuery(), Ct);

        Assert.False(read.Enabled);
        Assert.Empty(read.Views);
    }

    [Fact]
    public async Task Forgetting_or_clearing_a_trail_that_never_existed_creates_nothing()
    {
        var world = World.Linked();

        await world.Handlers.HandleAsync(new ForgetBrowsingCommand(BrowsingKind.View, "B073WXYZ01"), Ct);
        await world.Handlers.HandleAsync(new ClearBrowsingCommand(), Ct);

        Assert.Empty(world.Store);
    }

    [Fact]
    public async Task The_browsers_trail_is_merged_once_at_sign_in()
    {
        var world = World.Linked();
        var at = world.Clock.GetUtcNow().AddHours(-1);

        var answer = await world.Handlers.HandleAsync(new MergeBrowsingCommand(
            [new ViewedProduct("B073WXYZ01", at), new ViewedProduct("B07C0FFEE1", at.AddMinutes(5))],
            [new PastSearch("mochila", at)]), Ct);

        Assert.Equal(["B07C0FFEE1", "B073WXYZ01"], answer.Views.Select(view => view.Code));
        Assert.Equal(["mochila"], answer.Searches.Select(search => search.Text));
    }

    /// <summary>
    /// The trail is personal data. Recording it is not audited at all — a product
    /// viewed is the customer's passage, not a change to the shop, and a row per
    /// click would keep what every customer looked at in the audit log
    /// (<see cref="IUnauditedCommand"/>).
    /// </summary>
    [Fact]
    public void Recording_the_trail_never_reaches_the_audit_log() =>
        Assert.True(typeof(IUnauditedCommand).IsAssignableFrom(typeof(RecordBrowsingCommand)));

    /// <summary>
    /// The commands that change the trail are audited, and the codes and the words
    /// they carry are marked never to reach the row — a log that kept what a
    /// customer deleted would make "delete" a lie.
    /// </summary>
    [Theory]
    [InlineData(typeof(ForgetBrowsingCommand), nameof(ForgetBrowsingCommand.Key))]
    [InlineData(typeof(MergeBrowsingCommand), nameof(MergeBrowsingCommand.Views))]
    [InlineData(typeof(MergeBrowsingCommand), nameof(MergeBrowsingCommand.Searches))]
    public void What_was_viewed_or_searched_never_reaches_the_audit_log(Type command, string property) =>
        Assert.True(command.GetProperty(property)!.IsDefined(typeof(AuditRedactedAttribute)));

    [Theory]
    [InlineData(BrowsingKind.View, "B073WXYZ01", true)]
    [InlineData(BrowsingKind.View, "b073wxyz01", true)]
    [InlineData(BrowsingKind.View, "B073 WXYZ", false)]
    [InlineData(BrowsingKind.View, "<script>", false)]
    [InlineData(BrowsingKind.View, "B073WXYZ01B073WXYZ01X", false)]
    [InlineData(BrowsingKind.Search, "cafetera", true)]
    [InlineData(BrowsingKind.Search, "c", false)]
    [InlineData(BrowsingKind.Search, "   ", false)]
    public void A_key_is_a_code_or_a_search_the_search_box_would_accept(BrowsingKind kind, string key, bool valid)
    {
        Assert.Equal(valid, new RecordBrowsingValidator().Validate(new RecordBrowsingCommand(kind, key)).IsValid);
        Assert.Equal(valid, new ForgetBrowsingValidator().Validate(new ForgetBrowsingCommand(kind, key)).IsValid);
    }

    [Fact]
    public void A_merge_is_no_longer_than_the_trail_keeps()
    {
        var at = DateTimeOffset.UtcNow;
        var validator = new MergeBrowsingValidator();

        Assert.True(validator.Validate(new MergeBrowsingCommand(
            [.. Enumerable.Range(0, BrowsingHistory.MaxViews).Select(i => new ViewedProduct($"P{i}", at))],
            [.. Enumerable.Range(0, BrowsingHistory.MaxSearches).Select(i => new PastSearch($"query {i}", at))])).IsValid);
        Assert.False(validator.Validate(new MergeBrowsingCommand(
            [.. Enumerable.Range(0, BrowsingHistory.MaxViews + 1).Select(i => new ViewedProduct($"P{i}", at))], [])).IsValid);
        Assert.False(validator.Validate(new MergeBrowsingCommand([], [new PastSearch("x", at)])).IsValid);
        Assert.False(validator.Validate(new MergeBrowsingCommand([new ViewedProduct("B073WXYZ01", default)], [])).IsValid);
    }

    private sealed class World
    {
        private World(CustomerId? customer)
        {
            Customer = customer;
            var principal = new FixedPrincipal(new CommercePrincipal(customer, null, "ana", "Ana Ruiz", [TenderoRoles.Shopper]));
            var histories = new InMemoryHistories(Store);
            Handlers = new ManageTrailHandlers(histories, principal, UnitOfWork, Clock);
            Reader = new GetBrowsingHistoryHandler(histories, principal, Clock);
        }

        public static World Linked() => new(CustomerId.New());

        public static World Unlinked() => new(null);

        public CustomerId? Customer { get; }
        public TestClock Clock { get; } = new();
        public List<BrowsingHistory> Store { get; } = [];
        public CountingUnitOfWork UnitOfWork { get; } = new();
        public int Saves => UnitOfWork.SaveCount;
        public ManageTrailHandlers Handlers { get; }
        public GetBrowsingHistoryHandler Reader { get; }
    }

    /// <summary>A list behind both ports: what matters is what the table ended up holding.</summary>
    private sealed class InMemoryHistories(List<BrowsingHistory> store) : IBrowsingHistoryRepository, IBrowsingHistoryReader
    {
        public Task<BrowsingHistory?> FindAsync(CustomerId customer, CancellationToken ct = default) =>
            Task.FromResult(store.Find(history => history.CustomerId == customer));

        public void Add(BrowsingHistory history) => store.Add(history);

        public Task<BrowsingTrail?> ForCustomerAsync(CustomerId customer, DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult(store.Find(history => history.CustomerId == customer) is { } history ? BrowsingTrail.Of(history, now) : null);
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
