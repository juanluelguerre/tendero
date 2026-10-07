using ElGuerre.Tendero.Knowledge.Domain;
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

    private ProductClaim FromThePicture(string quote = "base redonda", double support = 0.95) =>
        Propose(SourceDocument.Depict(this.lamp, "es", "Una lámpara negra con base redonda y brazo articulado.", Front, this.clock), quote, support);

    private ProductClaim FromTheDescription(string quote = "pinza de escritorio", double support = 0.95) =>
        Propose(SourceDocument.Describe(this.lamp, "es", "Flexo de doble brazo con pinza de escritorio.", this.clock), quote, support);

    private async Task<IReadOnlyList<ClaimForReview>> Queue() =>
        (await new ListClaimsForReviewHandler(this.knowledge).HandleAsync(new ListClaimsForReviewQuery(), TestContext.Current.CancellationToken)).Items;

    private Task<ReviewClaimResult> Decide(ProductClaim claim, bool inBulk, ReviewDecision decision = ReviewDecision.Approve) =>
        new ReviewClaimHandler(this.knowledge, new Reviewer("Ana Tendera"), this.knowledge, this.clock)
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
}
