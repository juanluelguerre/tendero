using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Features.ReviewClaims;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// The review queue (P10-5): what a person reads, in the order worth reading it,
/// and a decision that names who made it (invariant 12).
/// </summary>
public sealed class ReviewClaimsTests
{
    private readonly TestClock clock = new();
    private readonly InMemoryKnowledge knowledge = new();
    private readonly ProductId lamp = ProductId.New();

    private ProductClaim Propose(string quote, double support)
    {
        var document = SourceDocument.Describe(this.lamp, "es", "Flexo LED con tres temperaturas de color y brazo articulado.", this.clock);
        if (this.knowledge.Documents.All(existing => existing.Id != document.Id))
            this.knowledge.Add(document);

        var claim = ProductClaim.Propose(
            this.lamp,
            LocalizedText.From("es", $"Tiene {quote}.").With("en", $"It has {quote}."),
            null, null,
            [new Evidence(document.Id, document.Locate(quote)!, support)],
            new Provenance("chat", "Qwen/Qwen3.5-4B", "851bf6e", "claims-2026-09-26", "run", this.clock.GetUtcNow()),
            this.clock);
        this.knowledge.Add(claim);
        return claim;
    }

    [Fact]
    public async Task The_queue_puts_the_weakest_supported_claim_first_with_its_quote_where_it_sits()
    {
        Propose("brazo articulado", 0.97);
        Propose("tres temperaturas de color", 0.64);

        var queue = await new ListClaimsForReviewHandler(this.knowledge)
            .HandleAsync(new ListClaimsForReviewQuery(), TestContext.Current.CancellationToken);

        Assert.Equal([0.64, 0.97], queue.Select(claim => claim.Support));
        var quote = Assert.Single(queue[0].Quotes);
        Assert.Equal(("es", "tres temperaturas de color"), (quote.Culture, quote.Text));
        Assert.Equal("Flexo LED con tres temperaturas de color y brazo articulado."[quote.Start..quote.End], quote.Text);
    }

    [Fact]
    public async Task Approving_names_the_person_the_request_authenticated_as()
    {
        var claim = Propose("brazo articulado", 0.97);

        var result = await Review(claim, ReviewDecision.Approve, "Ana Tendera");

        Assert.Equal(ReviewOutcome.Approved, result.Outcome);
        Assert.Equal(("Ana Tendera", ClaimStatus.Approved), (claim.ReviewedBy, claim.Status));
        Assert.Empty(await new ListClaimsForReviewHandler(this.knowledge)
            .HandleAsync(new ListClaimsForReviewQuery(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_claim_already_decided_or_unknown_is_not_reviewed_twice()
    {
        var claim = Propose("brazo articulado", 0.97);
        await Review(claim, ReviewDecision.Reject, "Ana Tendera");

        Assert.Equal(ReviewOutcome.NotWaiting, (await Review(claim, ReviewDecision.Approve, "Luis")).Outcome);
        Assert.Equal(ReviewOutcome.NotFound, (await new ReviewClaimHandler(this.knowledge, new Reviewer("Luis"), this.knowledge, this.clock)
            .HandleAsync(new ReviewClaimCommand(ClaimId.New(), ReviewDecision.Approve), TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(("Ana Tendera", ClaimStatus.Rejected), (claim.ReviewedBy, claim.Status));
    }

    [Fact]
    public async Task Rejecting_is_saved_counted_and_answered_as_a_rejection()
    {
        var claim = Propose("brazo articulado", 0.97);
        using var metrics = new InstrumentRecorder("tendero.knowledge.claims.reviewed");
        var saves = this.knowledge.SaveCount;

        var result = await Review(claim, ReviewDecision.Reject, "Ana Tendera");

        Assert.Equal(ReviewOutcome.Rejected, result.Outcome);
        Assert.Equal(saves + 1, this.knowledge.SaveCount);
        Assert.Contains(metrics.Of("tendero.knowledge.claims.reviewed"), tags => Equals(tags["decision"], "reject"));
    }

    [Fact]
    public async Task The_queue_shows_the_statement_in_both_languages()
    {
        Propose("brazo articulado", 0.97);

        var queued = Assert.Single(await new ListClaimsForReviewHandler(this.knowledge)
            .HandleAsync(new ListClaimsForReviewQuery(), TestContext.Current.CancellationToken));

        Assert.Equal(("Tiene brazo articulado.", "It has brazo articulado."), (queued.StatementSpanish, queued.StatementEnglish));
    }

    private Task<ReviewClaimResult> Review(ProductClaim claim, ReviewDecision decision, string reviewer) =>
        new ReviewClaimHandler(this.knowledge, new Reviewer(reviewer), this.knowledge, this.clock)
            .HandleAsync(new ReviewClaimCommand(claim.Id, decision), TestContext.Current.CancellationToken);

    private sealed class Reviewer(string name) : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } = CommercePrincipal.Anonymous with { Subject = name.ToLowerInvariant(), DisplayName = name };
    }
}
