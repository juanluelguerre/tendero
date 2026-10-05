using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.Assist.Features.DecideProposals;
using ElGuerre.Tendero.Assist.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Assist.Tests;

/// <summary>
/// Approving runs each row on its own, as the person approving: a stale row runs
/// nothing, a refusal or an exception fails that row alone, and each command runs
/// in a scope that names its proposal for the audit.
/// </summary>
[Collection(nameof(FakeEntry))]
public sealed class DecideProposalsTests
{
    private static readonly TestClock Clock = new();

    public DecideProposalsTests()
    {
        FakeEntry.Refusing.Clear();
        FakeEntry.Throwing.Clear();
        FakeEntry.Applied.Clear();
        FakeEntry.NotJson.Clear();
        FakeEntry.OnApply = null;
    }

    [Fact]
    public async Task An_approved_row_runs_as_the_approver_and_names_its_proposal()
    {
        var shop = Shop(("MUG-1", "old"));
        var proposal = shop.Propose("MUG-1", "new");

        var result = await shop.Approve(proposal.Id);

        Assert.Equal(DecisionOutcome.Applied, Assert.Single(result.Rows).Outcome);
        Assert.Equal("new", shop.World["MUG-1"]);
        Assert.Equal(("MUG-1", (ProposalId?)proposal.Id), Assert.Single(FakeEntry.Applied));
        Assert.Equal((ProposalStatus.Applied, "Lucía"), (proposal.Status, proposal.DecidedBy));
    }

    [Fact]
    public async Task A_row_whose_subject_changed_is_stale_and_runs_nothing()
    {
        var shop = Shop(("MUG-1", "old"));
        var proposal = shop.Propose("MUG-1", "new");
        shop.World["MUG-1"] = "edited by hand";

        var result = await shop.Approve(proposal.Id);

        Assert.Equal(DecisionOutcome.Stale, Assert.Single(result.Rows).Outcome);
        Assert.Equal("edited by hand", shop.World["MUG-1"]);
        Assert.Equal(ProposalStatus.Stale, proposal.Status);
    }

    /// <summary>
    /// The snapshot comes back from a jsonb column, and jsonb keeps the value, not
    /// the text: its own spacing and its own key order. Compared as text, every
    /// proposal read back from Postgres was stale — the integration test found it.
    /// </summary>
    [Fact]
    public async Task A_snapshot_written_differently_but_meaning_the_same_is_not_stale()
    {
        var shop = Shop(("MUG-1", "Mug"));
        var proposal = ActionProposal.Propose(FakeEntry.CommandName, FakeEntry.Payload("MUG-1", "new").GetRawText(), "MUG-1",
            """{"subject": "MUG-1", "value": "Mug"}""", "because", new ProposalProvenance(null, null, null, null, null, "lucia"), Clock);
        shop.Proposals.Add(proposal);

        Assert.Equal(DecisionOutcome.Applied, Assert.Single((await shop.Approve(proposal.Id)).Rows).Outcome);
    }

    [Fact]
    public async Task A_row_whose_subject_has_gone_is_stale()
    {
        var shop = Shop(("MUG-1", "old"));
        var proposal = shop.Propose("MUG-1", "new");
        shop.World.Remove("MUG-1");

        Assert.Equal(DecisionOutcome.Stale, Assert.Single((await shop.Approve(proposal.Id)).Rows).Outcome);
    }

    [Fact]
    public async Task One_refusal_and_one_exception_fail_their_rows_and_stop_none_of_the_others()
    {
        var shop = Shop(("A", "1"), ("B", "1"), ("C", "1"));
        var a = shop.Propose("A", "2");
        var b = shop.Propose("B", "2");
        var c = shop.Propose("C", "2");
        FakeEntry.Refusing.Add("A");
        FakeEntry.Throwing.Add("B");

        var result = await shop.Approve(a.Id, b.Id, c.Id);

        Assert.Equal(
            [(DecisionOutcome.Failed, "Archived"), (DecisionOutcome.Failed, "B timed out."), (DecisionOutcome.Applied, null)],
            result.Rows.Select(row => (row.Outcome, row.Reason)));
        Assert.Equal([ProposalStatus.Failed, ProposalStatus.Failed, ProposalStatus.Applied], new[] { a, b, c }.Select(p => p.Status));
        Assert.Equal("2", shop.World["C"]);
    }

    [Fact]
    public async Task The_same_id_twice_runs_the_command_once()
    {
        var shop = Shop(("MUG-1", "old"));
        var proposal = shop.Propose("MUG-1", "new");

        var result = await shop.Approve(proposal.Id, proposal.Id);

        Assert.Single(result.Rows);
        Assert.Single(FakeEntry.Applied);
    }

    [Fact]
    public async Task A_decided_proposal_is_not_waiting_and_a_missing_one_is_not_found()
    {
        var shop = Shop(("MUG-1", "old"));
        var rejected = shop.Propose("MUG-1", "new");
        rejected.Reject("Lucía", "No", Clock);

        var result = await shop.Approve(rejected.Id, ProposalId.New(Clock.GetUtcNow()));

        Assert.Equal([DecisionOutcome.NotWaiting, DecisionOutcome.NotFound], result.Rows.Select(row => row.Outcome));
        Assert.Empty(FakeEntry.Applied);
    }

    /// <summary>The process died after Approved: approving again finishes the row
    /// without the staleness check (ruling 1 of the plan).</summary>
    [Fact]
    public async Task An_approved_row_whose_result_is_unknown_can_be_approved_again()
    {
        var shop = Shop(("MUG-1", "old"));
        var proposal = shop.Propose("MUG-1", "new");
        proposal.Approve("Lucía", Clock);
        Clock.Advance(ActionProposal.StillRunning + TimeSpan.FromSeconds(1));

        var result = await shop.Approve(proposal.Id);

        Assert.Equal(DecisionOutcome.Applied, Assert.Single(result.Rows).Outcome);
        Assert.Equal("new", shop.World["MUG-1"]);
    }

    /// <summary>
    /// A retry whose "now" moved cannot tell whether its own command ran before the
    /// process died or a person changed it since: either way, running it again
    /// could overwrite a person's edit, so it runs nothing and says why (M-2).
    /// </summary>
    [Fact]
    public async Task A_retry_whose_subject_changed_since_the_approval_runs_nothing()
    {
        var shop = Shop(("MUG-1", "old"));
        var proposal = shop.Propose("MUG-1", "new");
        proposal.Approve("Lucía", Clock);
        shop.World["MUG-1"] = "written by hand the next morning";
        Clock.Advance(ActionProposal.StillRunning + TimeSpan.FromSeconds(1));

        var row = Assert.Single((await shop.Approve(proposal.Id)).Rows);

        Assert.Equal(DecisionOutcome.Failed, row.Outcome);
        Assert.Contains("changed", row.Reason);
        Assert.Equal("written by hand the next morning", shop.World["MUG-1"]);
        Assert.Empty(FakeEntry.Applied);
    }

    /// <summary>A client that disconnects mid-batch: the row whose command ran is
    /// still recorded, and no row after it starts (M-1).</summary>
    [Fact]
    public async Task A_cancelled_batch_records_the_row_that_ran_and_starts_no_other()
    {
        var shop = Shop(("A", "1"), ("B", "1"));
        var a = shop.Propose("A", "2");
        var b = shop.Propose("B", "2");
        using var disconnect = new CancellationTokenSource();
        FakeEntry.OnApply = _ => disconnect.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => shop.Approve(disconnect.Token, a.Id, b.Id));

        Assert.Equal(2, shop.UnitOfWork.Saved);   // A's claim and A's result
        Assert.Equal(ProposalStatus.Applied, a.Status);
        Assert.Equal(["A"], FakeEntry.Applied.Select(applied => applied.Subject));
    }

    /// <summary>An entry whose "now" is not JSON is stale, not a broken row (M-4).</summary>
    [Fact]
    public async Task A_now_that_is_not_json_makes_the_row_stale()
    {
        var shop = Shop(("MUG-1", "old"));
        var proposal = shop.Propose("MUG-1", "new");
        FakeEntry.NotJson.Add("MUG-1");

        Assert.Equal(DecisionOutcome.Stale, Assert.Single((await shop.Approve(proposal.Id)).Rows).Outcome);
    }

    /// <summary>A body with no ids reaches the validator as an empty list — a 400,
    /// not a NullReferenceException and a 500 (M-5).</summary>
    [Fact]
    public void A_body_without_ids_becomes_an_empty_decision_the_validator_refuses()
    {
        var approval = DecideProposalsEndpoints.Approval(new ApproveProposalsRequest(null!));
        var rejection = DecideProposalsEndpoints.Rejection(new RejectProposalsRequest(null!, "because"));

        Assert.Empty(approval.Ids);
        Assert.Empty(rejection.Ids);
        Assert.False(new ApproveProposalsValidator().Validate(approval).IsValid);
    }

    /// <summary>A row whose result cannot be saved — a database blip, a reason too
    /// long for its column — fails alone: the batch answers, and the rows after it
    /// still run (final review, I-1).</summary>
    [Fact]
    public async Task A_row_whose_result_cannot_be_saved_fails_alone()
    {
        var shop = Shop(("A", "1"), ("B", "1"));
        var a = shop.Propose("A", "2");
        var b = shop.Propose("B", "2");
        shop.UnitOfWork.FailOn = save => save == 2 ? new TimeoutException("the database went away") : null;   // A's result

        var result = await shop.Approve(a.Id, b.Id);

        var first = result.Rows[0];
        Assert.Equal(DecisionOutcome.Failed, first.Outcome);
        Assert.Contains("the database went away", first.Reason);
        Assert.Equal(DecisionOutcome.Applied, result.Rows[1].Outcome);
        Assert.Equal("2", shop.World["B"]);
    }

    /// <summary>Two approvals of one proposal at once — a double click, two tabs:
    /// the one whose approval was saved second is told it is not waiting, and its
    /// command does not run (final review, I-2).</summary>
    [Fact]
    public async Task A_proposal_another_approval_reached_first_is_not_waiting_and_runs_nothing()
    {
        var shop = Shop(("MUG-1", "old"));
        var proposal = shop.Propose("MUG-1", "new");
        shop.UnitOfWork.FailOn = save => save == 1 ? new ConcurrentWriteException("the proposal's version", new Exception()) : null;

        var row = Assert.Single((await shop.Approve(proposal.Id)).Rows);

        Assert.Equal(DecisionOutcome.NotWaiting, row.Outcome);
        Assert.Empty(FakeEntry.Applied);
    }

    [Fact]
    public async Task A_proposal_approved_moments_ago_is_still_running_and_is_not_run_again()
    {
        var shop = Shop(("MUG-1", "old"));
        var proposal = shop.Propose("MUG-1", "new");
        proposal.Approve("Lucía", Clock);

        var row = Assert.Single((await shop.Approve(proposal.Id)).Rows);

        Assert.Equal(DecisionOutcome.NotWaiting, row.Outcome);
        Assert.Empty(FakeEntry.Applied);
    }

    [Fact]
    public async Task A_command_the_registry_no_longer_knows_fails_its_row()
    {
        var shop = Shop(("MUG-1", "old"));
        var orphan = ActionProposal.Propose("Retired", "{}", "MUG-1", "old", "because",
            new ProposalProvenance(null, null, null, null, null, "lucia"), Clock);
        shop.Proposals.Add(orphan);

        var row = Assert.Single((await shop.Approve(orphan.Id)).Rows);

        Assert.Equal((DecisionOutcome.Failed, "No proposable command is named Retired."), (row.Outcome, row.Reason));
    }

    [Fact]
    public async Task Rejecting_records_the_reason_and_runs_nothing()
    {
        var shop = Shop(("MUG-1", "old"));
        var proposal = shop.Propose("MUG-1", "new");

        var result = await shop.Reject("Too literal", proposal.Id);

        Assert.Equal(DecisionOutcome.Rejected, Assert.Single(result.Rows).Outcome);
        Assert.Equal(("Too literal", "old"), (proposal.Result, shop.World["MUG-1"]));
    }

    /// <summary>A rejection is a write: it is saved, once for the batch — and a batch that rejected nothing saves nothing.</summary>
    [Fact]
    public async Task A_batch_that_rejects_is_saved_once_and_one_that_rejects_nothing_is_not()
    {
        var shop = Shop(("MUG-1", "old"), ("CUP-2", "old"));
        var (first, second) = (shop.Propose("MUG-1", "new"), shop.Propose("CUP-2", "new"));

        await shop.Reject("Too literal", first.Id, second.Id);
        Assert.Equal(1, shop.Rejections.SaveCount);

        // Both are decided now: nothing is waiting, so nothing is written.
        var again = await shop.Reject("Too literal", first.Id, ProposalId.New(Clock.GetUtcNow()));
        Assert.Equal([DecisionOutcome.NotWaiting, DecisionOutcome.NotFound], again.Rows.Select(row => row.Outcome));
        Assert.Equal(1, shop.Rejections.SaveCount);
    }

    [Fact]
    public void A_rejection_needs_a_reason_and_a_batch_is_bounded()
    {
        var id = ProposalId.New(Clock.GetUtcNow());

        Assert.False(new RejectProposalsValidator().Validate(new RejectProposalsCommand([id], "  ")).IsValid);
        Assert.False(new ApproveProposalsValidator().Validate(new ApproveProposalsCommand([])).IsValid);
        Assert.False(new ApproveProposalsValidator().Validate(
            new ApproveProposalsCommand([.. Enumerable.Range(0, ProposalLimits.MaxBatch + 1).Select(_ => id)])).IsValid);
    }

    // ---------- fixtures ----------

    private static TestShop Shop(params (string Subject, string Value)[] world) => new(world.ToDictionary(w => w.Subject, w => w.Value));

    private sealed class TestShop
    {
        private readonly ServiceProvider provider;

        public TestShop(Dictionary<string, string> world)
        {
            World = world;
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IProposalRepository>(Proposals);
            services.AddSingleton<IUnitOfWork>(UnitOfWork);
            services.AddSingleton<IPrincipalAccessor>(new FixedPrincipal("Lucía"));
            services.AddScoped<ProposalScope>();
            services.AddScoped<IProposableCommand>(scope => new FakeEntry(World, scope.GetRequiredService<ProposalScope>()));
            services.AddAssist();
            provider = services.BuildServiceProvider(validateScopes: true);
        }

        public Dictionary<string, string> World { get; }

        public InMemoryProposals Proposals { get; } = new();

        public ScriptedUnitOfWork UnitOfWork { get; } = new();

        public ActionProposal Propose(string subject, string value)
        {
            var proposal = ActionProposal.Propose(FakeEntry.CommandName, FakeEntry.Payload(subject, value).GetRawText(),
                subject, FakeEntry.Snapshot(World, subject), "because", new ProposalProvenance(null, null, null, null, null, "lucia"), Clock);
            Proposals.Add(proposal);
            return proposal;
        }

        public Task<ProposalDecisions> Approve(params ProposalId[] ids) => Approve(TestContext.Current.CancellationToken, ids);

        public Task<ProposalDecisions> Approve(CancellationToken cancellationToken, params ProposalId[] ids) =>
            new ApproveProposalsHandler(provider.GetRequiredService<IServiceScopeFactory>(), new FixedPrincipal("Lucía"), Clock)
                .HandleAsync(new ApproveProposalsCommand(ids), cancellationToken);

        /// <summary>What rejecting saved: its handler writes through a unit of work of its own.</summary>
        public CountingUnitOfWork Rejections { get; } = new();

        public Task<ProposalDecisions> Reject(string reason, params ProposalId[] ids) =>
            new RejectProposalsHandler(Proposals, Rejections, new FixedPrincipal("Lucía"), Clock)
                .HandleAsync(new RejectProposalsCommand(ids, reason), TestContext.Current.CancellationToken);
    }
}
