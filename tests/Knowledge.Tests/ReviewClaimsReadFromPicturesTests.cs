using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.Knowledge.Features.ReadPictures;
using ElGuerre.Tendero.Knowledge.Features.ReviewClaims;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// A claim read from a picture, in front of the person who reviews it (the phase
/// 13a spec, I-4). Its quote is a model's description of the picture and its
/// support a model's sentence held to those words: the person is the only
/// independent check. So the queue says which picture it was read from and who
/// read it, for the screen to show the picture — and the server never approves
/// one together with others, whatever a screen ticked.
/// </summary>
public sealed class ReviewClaimsReadFromPicturesTests
{
    private readonly TestClock clock = new();
    private readonly InMemoryKnowledge knowledge = new();
    private readonly ProductId lamp = ProductId.New();
    private static readonly PictureReading Front = new(new ImageId("lamp-front"), "qwen3.5-4b-q8_0", PictureReadingPrompt.Version);

    private ProductClaim Propose(SourceDocument document, string quote, double support)
    {
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

    /// <summary>
    /// A picture's own answer: built by code from what the picture answered to a
    /// closed question, its support the probability the model gave that answer.
    /// </summary>
    private ProductClaim AnsweredByThePicture(double confidence)
    {
        var document = SourceDocument.Depict(this.lamp, "es", PictureAnswers.Under("Dos lámparas negras.", "es", 2, confidence), Front, this.clock);
        this.knowledge.Add(document);
        var line = PictureAnswers.CountIn(document.Text, "es")!.Value;
        var claim = ProductClaim.Propose(
            this.lamp,
            LocalizedText.From("es", "piezas: 2").With("en", "pieces: 2"),
            "PIECES", "2",
            [new Evidence(document.Id, document.Locate(line.Line)!, line.Confidence!.Value)],
            new Provenance(ClaimExtractors.Picture, Front.Model, null, Front.PromptVersion, "run", this.clock.GetUtcNow()),
            this.clock);
        this.knowledge.Add(claim);
        return claim;
    }

    private ProductClaim FromThePicture(string quote = "base redonda", double support = 0.95) =>
        Propose(SourceDocument.Depict(this.lamp, "es", "Una lámpara negra con base redonda y brazo articulado.", Front, this.clock), quote, support);

    private ProductClaim FromTheDescription(string quote = "pinza de escritorio", double support = 0.95) =>
        Propose(SourceDocument.Describe(this.lamp, "es", "Flexo de doble brazo con pinza de escritorio.", this.clock), quote, support);

    private async Task<IReadOnlyList<ClaimForReview>> Queue() =>
        (await new ListClaimsForReviewHandler(this.knowledge, Held.Nothing).HandleAsync(new ListClaimsForReviewQuery(), TestContext.Current.CancellationToken)).Items;

    private Task<ReviewClaimResult> Decide(ProductClaim claim, bool inBulk, ReviewDecision decision = ReviewDecision.Approve) =>
        new ReviewClaimHandler(this.knowledge, new Reviewer("Ana Tendera"), this.knowledge, this.clock, Held.Nothing)
            .HandleAsync(new ReviewClaimCommand(claim.Id, decision, inBulk), TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_queue_says_which_picture_a_claim_was_read_from_and_who_read_it()
    {
        var read = FromThePicture();
        var written = FromTheDescription();

        var queue = await Queue();

        var fromPicture = queue.Single(claim => claim.ClaimId == read.Id.ToString());
        Assert.Equal(new PictureForReview("lamp-front", "qwen3.5-4b-q8_0", PictureReadingPrompt.Version), fromPicture.Picture);
        Assert.Null(queue.Single(claim => claim.ClaimId == written.Id.ToString()).Picture);
    }

    /// <summary>
    /// Why a claim is decided on its own, said by the server so no screen keeps a
    /// copy of the rule: weakly supported, read from a picture — or neither, and
    /// it may go with others. A weak one read from a picture is weak first: that
    /// is the reason to read it before the rest.
    /// </summary>
    [Fact]
    public async Task The_queue_says_why_a_claim_is_decided_on_its_own()
    {
        var read = FromThePicture();
        var weakRead = FromThePicture("brazo articulado", 0.41);
        var written = FromTheDescription();
        var weakWritten = FromTheDescription("doble brazo", 0.41);

        var alone = (await Queue()).ToDictionary(claim => claim.ClaimId, claim => claim.DecidedAlone);

        Assert.Equal(AloneReason.ReadFromAPicture, alone[read.Id.ToString()]);
        Assert.Equal(AloneReason.WeakSupport, alone[weakRead.Id.ToString()]);
        Assert.Null(alone[written.Id.ToString()]);
        Assert.Equal(AloneReason.WeakSupport, alone[weakWritten.Id.ToString()]);
    }

    /// <summary>The server's rule, whatever a screen sent: nobody approves it without having it open.</summary>
    [Fact]
    public async Task A_claim_read_from_a_picture_is_never_approved_together_with_others()
    {
        var read = FromThePicture();

        var result = await Decide(read, inBulk: true);

        Assert.Equal(ReviewOutcome.ReadFromAPicture, result.Outcome);
        Assert.Equal(ClaimStatus.Proposed, read.Status);
        Assert.Equal((AuditOutcome.Denied, "ReadFromAPicture"), (((IAuditedResult)result).AuditOutcome, ((IAuditedResult)result).AuditReason));
    }

    [Fact]
    public async Task Opened_on_its_own_a_claim_read_from_a_picture_is_approved_by_the_person()
    {
        var read = FromThePicture();

        var result = await Decide(read, inBulk: false);

        Assert.Equal(ReviewOutcome.Approved, result.Outcome);
        Assert.Equal((ClaimStatus.Approved, "Ana Tendera"), (read.Status, read.ReviewedBy));
    }

    /// <summary>Rejecting needs no look at the picture to be safe: nothing reaches the catalogue.</summary>
    [Fact]
    public async Task A_claim_read_from_a_picture_can_be_rejected_together_with_others()
    {
        var read = FromThePicture();

        Assert.Equal(ReviewOutcome.Rejected, (await Decide(read, inBulk: true, ReviewDecision.Reject)).Outcome);
    }

    /// <summary>In a batch it is refused on its row, and the claims from the description beside it go through.</summary>
    [Fact]
    public async Task A_batch_approves_what_was_written_and_refuses_what_was_read_from_a_picture()
    {
        var read = FromThePicture();
        var written = FromTheDescription();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(typeof(ApproveClaimsCommand).Assembly);
        services.AddSingleton<IKnowledgeRepository>(this.knowledge);
        services.AddSingleton<IUnitOfWork>(this.knowledge);
        services.AddSingleton<ICatalogueValues>(Held.Nothing);
        services.AddSingleton<TimeProvider>(this.clock);
        services.AddSingleton<IPrincipalAccessor>(new Reviewer("Ana Tendera"));
        services.AddSingleton<IAuditWriter>(new NoAudit());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var approvals = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
            .SendAsync(new ApproveClaimsCommand([read.Id, written.Id]), TestContext.Current.CancellationToken);

        Assert.Equal(
            [(read.Id, BulkApprovalOutcome.ReadFromAPicture), (written.Id, BulkApprovalOutcome.Approved)],
            approvals.Rows.Select(row => (row.ClaimId, row.Outcome)));
    }

    private sealed class Reviewer(string name) : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } = CommercePrincipal.Anonymous with { Subject = name.ToLowerInvariant(), DisplayName = name };
    }

    private sealed class NoAudit : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    // ---------- the tray: a picture's own answer, and a sure one ----------

    /// <summary>
    /// **What may be approved together** (the author's target, 2026-10-08: at
    /// least 90 % right among what is approved in bulk). A picture's own answer
    /// to a closed question, given with a probability at or above the line, has
    /// no reason to be decided alone — and one under the line still has.
    /// </summary>
    [Theory]
    [InlineData(0.97, null)]
    [InlineData(0.95, null)]
    [InlineData(0.94, AloneReason.ReadFromAPicture)]
    [InlineData(0.50, AloneReason.WeakSupport)]
    public async Task A_pictures_own_answer_goes_with_others_only_when_the_model_was_sure(double confidence, AloneReason? alone)
    {
        var answered = AnsweredByThePicture(confidence);

        var queued = (await Queue()).Single(claim => claim.ClaimId == answered.Id.ToString());

        Assert.Equal(alone, queued.DecidedAlone);
        // It is still shown with its picture: sure is not unseen.
        Assert.NotNull(queued.Picture);
    }

    /// <summary>
    /// **The number that says whether the tray is worth having** (ADR 0036): each
    /// decision says whether it was on a sure answer, so the share of them a
    /// person rejects can be read — the tray promises at most one in ten.
    /// </summary>
    [Fact]
    public async Task A_decision_says_whether_it_was_on_a_sure_answer()
    {
        var (sure, extracted) = (AnsweredByThePicture(0.97), FromThePicture());
        using var metrics = new InstrumentRecorder("tendero.knowledge.claims.reviewed");

        await Decide(sure, inBulk: true, ReviewDecision.Reject);
        await Decide(extracted, inBulk: false);

        Assert.Equal(
            [("reject", "true"), ("approve", "false")],
            metrics.Of("tendero.knowledge.claims.reviewed").Select(tags => ((string)tags["decision"]!, (string)tags["sure_answer"]!)));
    }

    [Fact]
    public async Task A_sure_answer_is_approved_in_bulk_by_the_person_and_an_unsure_one_is_refused()
    {
        var (sure, unsure) = (AnsweredByThePicture(0.97), AnsweredByThePicture(0.94));

        var approved = await Decide(sure, inBulk: true);
        var refused = await Decide(unsure, inBulk: true);

        Assert.Equal((ReviewOutcome.Approved, ClaimStatus.Approved, "Ana Tendera"), (approved.Outcome, sure.Status, sure.ReviewedBy));
        Assert.Equal((ReviewOutcome.ReadFromAPicture, ClaimStatus.Proposed), (refused.Outcome, unsure.Status));
    }

    /// <summary>
    /// Sure of an answer is the picture's answer, not any claim that happens to
    /// rest on a reading with a high score: a sentence a model extracted from
    /// prose about a picture is decided alone, however well a scorer liked it.
    /// </summary>
    [Fact]
    public async Task A_sentence_extracted_from_a_readings_prose_is_decided_alone_however_well_supported()
    {
        var extracted = FromThePicture(support: 0.99);

        Assert.Equal(AloneReason.ReadFromAPicture, (await Queue()).Single().DecidedAlone);
        Assert.Equal(ReviewOutcome.ReadFromAPicture, (await Decide(extracted, inBulk: true)).Outcome);
    }
}
