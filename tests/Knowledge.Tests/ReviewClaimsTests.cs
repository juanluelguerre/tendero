using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Features.ReviewClaims;
using ElGuerre.Tendero.Knowledge.Ports;
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

        var queue = await new ListClaimsForReviewHandler(this.knowledge, Held.Nothing)
            .HandleAsync(new ListClaimsForReviewQuery(), TestContext.Current.CancellationToken);

        Assert.Equal([0.64, 0.97], queue.Items.Select(claim => claim.Support));
        var quote = Assert.Single(queue.Items[0].Quotes);
        Assert.Equal(("es", "tres temperaturas de color"), (quote.Culture, quote.Text));
        Assert.Equal("Flexo LED con tres temperaturas de color y brazo articulado."[quote.Start..quote.End], quote.Text);
    }

    /// <summary>One product's claims, for its bulk review (S-5): the queue narrowed, the counts with it.</summary>
    [Fact]
    public async Task The_queue_can_be_narrowed_to_one_product()
    {
        Propose("brazo articulado", 0.97);
        var other = ProductId.New();
        var document = SourceDocument.Describe(other, "es", "Taza de gres.", this.clock);
        this.knowledge.Add(document);
        this.knowledge.Add(ProductClaim.Propose(
            other, LocalizedText.From("es", "Es de gres.").With("en", "It is stoneware."), null, null,
            [new Evidence(document.Id, document.Locate("gres")!, 0.9)],
            new Provenance("chat", "m", "r", "p", "run", this.clock.GetUtcNow()), this.clock));

        var queue = await new ListClaimsForReviewHandler(this.knowledge, Held.Nothing).HandleAsync(
            new ListClaimsForReviewQuery(Product: this.lamp), TestContext.Current.CancellationToken);

        Assert.Equal((1, this.lamp.ToString()), (queue.Total, Assert.Single(queue.Items).ProductId));
    }

    [Fact]
    public async Task Approving_names_the_person_the_request_authenticated_as()
    {
        var claim = Propose("brazo articulado", 0.97);

        var result = await Review(claim, ReviewDecision.Approve, "Ana Tendera");

        Assert.Equal(ReviewOutcome.Approved, result.Outcome);
        Assert.Equal(("Ana Tendera", ClaimStatus.Approved), (claim.ReviewedBy, claim.Status));
        Assert.Empty((await new ListClaimsForReviewHandler(this.knowledge, Held.Nothing)
            .HandleAsync(new ListClaimsForReviewQuery(), TestContext.Current.CancellationToken)).Items);
    }

    [Fact]
    public async Task A_claim_already_decided_or_unknown_is_not_reviewed_twice()
    {
        var claim = Propose("brazo articulado", 0.97);
        await Review(claim, ReviewDecision.Reject, "Ana Tendera");

        Assert.Equal(ReviewOutcome.NotWaiting, (await Review(claim, ReviewDecision.Approve, "Luis")).Outcome);
        Assert.Equal(ReviewOutcome.NotFound, (await new ReviewClaimHandler(this.knowledge, new Reviewer("Luis"), this.knowledge, this.clock, Held.Nothing)
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

        var queued = Assert.Single((await new ListClaimsForReviewHandler(this.knowledge, Held.Nothing)
            .HandleAsync(new ListClaimsForReviewQuery(), TestContext.Current.CancellationToken)).Items);

        Assert.Equal(("Tiene brazo articulado.", "It has brazo articulado."), (queued.StatementSpanish, queued.StatementEnglish));
    }

    /// <summary>
    /// The queue pages (R-7.2): the second page starts where the first stopped,
    /// and the total is the queue's. Five claims proposed out of order, so a page
    /// taken before the order, or the first page served twice, cannot come out right.
    /// </summary>
    [Fact]
    public async Task The_second_page_continues_the_first_and_the_total_is_the_queues()
    {
        foreach (var support in new[] { 0.9, 0.5, 0.7, 0.95, 0.6 })
            Propose("brazo articulado", support);

        var first = await Queue(new ListClaimsForReviewQuery(Page: 1, PageSize: 2));
        var second = await Queue(new ListClaimsForReviewQuery(Page: 2, PageSize: 2));
        var last = await Queue(new ListClaimsForReviewQuery(Page: 3, PageSize: 2));

        Assert.Equal([0.5, 0.6], first.Items.Select(claim => claim.Support));
        Assert.Equal([0.7, 0.9], second.Items.Select(claim => claim.Support));
        Assert.Equal([0.95], last.Items.Select(claim => claim.Support));
        Assert.Equal((5, 2, 2), (second.Total, second.Page, second.PageSize));
    }

    /// <summary>
    /// Weak is the server's rule now, so its count cannot be one page's: three weak
    /// of five, shown two at a time, and every filter counted whichever is shown.
    /// </summary>
    [Fact]
    public async Task Weak_narrows_before_the_page_and_every_filter_is_counted_over_the_queue()
    {
        foreach (var support in new[] { 0.9, 0.5, 0.7, 0.95, 0.6 })
            Propose("brazo articulado", support);

        var weak = await Queue(new ListClaimsForReviewQuery(ClaimSupportFilter.Weak, Page: 2, PageSize: 2));
        var all = await Queue(new ListClaimsForReviewQuery(ClaimSupportFilter.All, Page: 1, PageSize: 2));

        Assert.Equal([0.7], weak.Items.Select(claim => claim.Support));
        Assert.Equal(3, weak.Total);
        Assert.Equal(5, all.Total);
        Assert.Equal(Enum.GetValues<ClaimSupportFilter>(), weak.Counts.Select(count => count.Support));
        Assert.Equal(weak.Counts, all.Counts);
        Assert.Equal([3, 5], all.Counts.Select(count => count.Count));
        // Each row says whether it is weak, so the screen marks it without a copy of the line.
        Assert.Equal([true, true], all.Items.Select(claim => claim.IsWeak));
        var strongest = await Queue(new ListClaimsForReviewQuery(ClaimSupportFilter.All, Page: 3, PageSize: 2));
        Assert.False(Assert.Single(strongest.Items).IsWeak);
    }

    /// <summary>The line is the backoffice's old rule exactly: under 0.8 is weak, 0.8 itself is not.</summary>
    [Theory]
    [InlineData(0.79, true)]
    [InlineData(ProductClaim.WeakSupport, false)]
    [InlineData(0.81, false)]
    public void Weak_is_support_under_the_line(double support, bool weak) =>
        Assert.Equal(weak, Propose("brazo articulado", support).IsWeaklySupported);

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void A_page_outside_the_contract_is_refused(int page, int pageSize) =>
        Assert.False(new ListClaimsForReviewValidator().Validate(new ListClaimsForReviewQuery(Page: page, PageSize: pageSize)).IsValid);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 100)]
    public void A_page_inside_the_contract_is_accepted(int page, int pageSize) =>
        Assert.True(new ListClaimsForReviewValidator().Validate(new ListClaimsForReviewQuery(Page: page, PageSize: pageSize)).IsValid);

    [Fact]
    public void A_support_filter_that_is_not_one_is_refused() =>
        Assert.False(new ListClaimsForReviewValidator().Validate(new ListClaimsForReviewQuery((ClaimSupportFilter)7)).IsValid);

    private Task<ClaimsForReviewPage> Queue(ListClaimsForReviewQuery query) =>
        new ListClaimsForReviewHandler(this.knowledge, Held.Nothing).HandleAsync(query, TestContext.Current.CancellationToken);

    private Task<ReviewClaimResult> Review(ProductClaim claim, ReviewDecision decision, string reviewer) =>
        new ReviewClaimHandler(this.knowledge, new Reviewer(reviewer), this.knowledge, this.clock, Held.Nothing)
            .HandleAsync(new ReviewClaimCommand(claim.Id, decision), TestContext.Current.CancellationToken);

    private sealed class Reviewer(string name) : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } = CommercePrincipal.Anonymous with { Subject = name.ToLowerInvariant(), DisplayName = name };
    }
}
