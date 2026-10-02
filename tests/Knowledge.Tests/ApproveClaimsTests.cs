using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Features.ReviewClaims;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// A product's claims approved together (phase 12, S-5; Steward spec, decision 5):
/// each one its own decision, and a weakly supported claim never among them — the
/// server refuses it whatever the screen sent, because the screen is the part a
/// client controls.
/// </summary>
public sealed class ApproveClaimsTests
{
    private readonly TestClock clock = new();
    private readonly InMemoryKnowledge knowledge = new();
    private readonly ProductId mug = ProductId.New();
    private readonly RecordingAudit audit = new();

    private ProductClaim Propose(string quote, double support)
    {
        var document = SourceDocument.Describe(this.mug, "es", "Taza de gres esmaltado, 350 ml, apta para lavavajillas, diseñada en Lisboa.", this.clock);
        if (this.knowledge.Documents.All(existing => existing.Id != document.Id))
            this.knowledge.Add(document);

        var claim = ProductClaim.Propose(
            this.mug,
            LocalizedText.From("es", $"Dice {quote}.").With("en", $"It says {quote}."),
            null, null,
            [new Evidence(document.Id, document.Locate(quote)!, support)],
            new Provenance("chat", "Qwen/Qwen3.5-4B", "851bf6e", "claims-2026-09-26", "run", this.clock.GetUtcNow()),
            this.clock);
        this.knowledge.Add(claim);
        return claim;
    }

    private async Task<ClaimApprovals> Approve(params ClaimId[] ids)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(typeof(ApproveClaimsCommand).Assembly);
        services.AddSingleton<IKnowledgeRepository>(this.knowledge);
        services.AddSingleton<IUnitOfWork>(this.knowledge);
        services.AddSingleton<TimeProvider>(this.clock);
        services.AddSingleton<IPrincipalAccessor>(new Reviewer("Ana Tendera"));
        services.AddSingleton<IAuditWriter>(this.audit);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new ApproveClaimsCommand(ids), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_well_supported_are_approved_each_by_the_person_and_a_weak_one_is_refused()
    {
        var glaze = Propose("gres esmaltado", 0.96);
        var capacity = Propose("350 ml", 0.93);
        var origin = Propose("diseñada en Lisboa", 0.41);

        var approvals = await Approve(glaze.Id, origin.Id, capacity.Id);

        Assert.Equal(
            [(glaze.Id, BulkApprovalOutcome.Approved), (origin.Id, BulkApprovalOutcome.WeakSupport), (capacity.Id, BulkApprovalOutcome.Approved)],
            approvals.Rows.Select(row => (row.ClaimId, row.Outcome)));
        Assert.Equal((ClaimStatus.Approved, "Ana Tendera"), (glaze.Status, glaze.ReviewedBy));
        Assert.Equal(ClaimStatus.Proposed, origin.Status);
    }

    /// <summary>Decided by someone else meanwhile, or never there: that row says so, the others go on.</summary>
    [Fact]
    public async Task A_claim_already_decided_or_unknown_is_answered_on_its_row_alone()
    {
        var glaze = Propose("gres esmaltado", 0.96);
        var capacity = Propose("350 ml", 0.93);
        capacity.Reject("Luis", this.clock);
        var unknown = ClaimId.New();

        var approvals = await Approve(unknown, capacity.Id, glaze.Id, glaze.Id);

        Assert.Equal(
            [BulkApprovalOutcome.NotFound, BulkApprovalOutcome.NotWaiting, BulkApprovalOutcome.Approved],
            approvals.Rows.Select(row => row.Outcome));
    }

    /// <summary>
    /// Every row leaves its audit row, the refusals too: a weak claim pushed through
    /// a bulk approval is a denial on record, as it is through the one-claim path
    /// (review of S-5, I-2).
    /// </summary>
    [Fact]
    public async Task Every_row_is_audited_and_a_weak_claim_as_a_denial()
    {
        var glaze = Propose("gres esmaltado", 0.96);
        var origin = Propose("diseñada en Lisboa", 0.41);

        await Approve(glaze.Id, origin.Id, ClaimId.New());

        Assert.Equal(
            [("ReviewClaimCommand", AuditOutcome.Allowed, null), ("ReviewClaimCommand", AuditOutcome.Denied, "WeakSupport"),
             ("ReviewClaimCommand", AuditOutcome.Denied, "NotFound")],
            this.audit.Rows.Select(row => (row.CommandType, row.Outcome, row.Reason)));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void A_batch_holds_one_to_a_hundred_claims(int count, bool valid) =>
        Assert.Equal(valid, new ApproveClaimsValidator().Validate(
            new ApproveClaimsCommand([.. Enumerable.Range(0, count).Select(_ => ClaimId.New())])).IsValid);

    private sealed class RecordingAudit : IAuditWriter
    {
        public List<AuditEntry> Rows { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            lock (this.Rows)
                this.Rows.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class Reviewer(string name) : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } = CommercePrincipal.Anonymous with { Subject = name.ToLowerInvariant(), DisplayName = name };
    }
}
