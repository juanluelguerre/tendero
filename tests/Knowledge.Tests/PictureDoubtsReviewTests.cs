using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Features.PictureDoubts;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// The doubts about pictures as a person meets and decides them (the phase 13a
/// spec, I-4d): a queue of the open ones, oldest first, worded in the reviewer's
/// language; and a decision that is theirs, said once, and changes nothing but
/// the doubt.
/// </summary>
public sealed class PictureDoubtsReviewTests
{
    private readonly TestClock clock = new();
    private readonly InMemoryKnowledge knowledge = new();
    private static readonly ProductId Sleeve = ProductId.New();

    private sealed class Reviewer(string name) : IPrincipalAccessor
    {
        public CommercePrincipal Current { get; } = new(null, null, name.ToLowerInvariant(), name, ["shopkeeper"]);
    }

    private PictureDoubt Doubted(string picture, string seen = "GREY")
    {
        var doubt = PictureDoubt.Raise(
            Sleeve,
            new PictureReading(new ImageId(picture), "qwen3.5-4b-q8_0", "picture-reading-test"),
            new ColourSeen("COLOR", seen, LocalizedText.From("es", "gris").With("en", "grey"), "BLACK", LocalizedText.From("es", "negro").With("en", "black"), 0.94, false),
            this.clock)!;
        this.knowledge.Add(doubt);
        return doubt;
    }

    private Task<PictureDoubtsPage> Queue(string culture = "es", int page = 1, int pageSize = 20) =>
        new ListPictureDoubtsHandler(this.knowledge).HandleAsync(new ListPictureDoubtsQuery(page, pageSize, culture), TestContext.Current.CancellationToken);

    private Task<DecidePictureDoubtResult> Decide(PictureDoubtId id, PictureDoubtDecision decision, string by = "Ana Tendera") =>
        new DecidePictureDoubtHandler(this.knowledge, new Reviewer(by), this.knowledge, this.clock)
            .HandleAsync(new DecidePictureDoubtCommand(id, decision), TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_queue_says_which_picture_what_was_seen_and_what_the_catalogue_holds_in_the_reviewers_language()
    {
        var doubt = Doubted("sleeve-front");

        var spanish = Assert.Single((await Queue("es")).Items);
        var english = Assert.Single((await Queue("en")).Items);

        Assert.Equal(
            (doubt.Id.ToString(), Sleeve.ToString(), "sleeve-front", "COLOR", "GREY", "gris", "BLACK", "negro", 0.94, "qwen3.5-4b-q8_0"),
            (spanish.DoubtId, spanish.ProductId, spanish.ImageId, spanish.AttributeCode, spanish.Seen, spanish.SeenLabel, spanish.Held, spanish.HeldLabel, spanish.Confidence, spanish.ReadBy));
        Assert.Equal(("grey", "black"), (english.SeenLabel, english.HeldLabel));
        Assert.False(spanish.SeenOnAnotherVariant);
    }

    /// <summary>Only what waits, the oldest first, a page at a time — and the total is of all that wait.</summary>
    [Fact]
    public async Task The_queue_is_the_open_doubts_oldest_first_a_page_at_a_time()
    {
        var first = Doubted("a");
        this.clock.Advance(TimeSpan.FromMinutes(1));
        var decided = Doubted("b");
        decided.Decide(PictureDoubtDecision.PictureIsFine, "Ana Tendera", this.clock);
        this.clock.Advance(TimeSpan.FromMinutes(1));
        var withdrawn = Doubted("c");
        withdrawn.Withdraw(this.clock);
        var last = Doubted("d");

        var page = await Queue(pageSize: 1);
        var next = await Queue(page: 2, pageSize: 1);

        Assert.Equal((2, first.Id.ToString()), (page.Total, Assert.Single(page.Items).DoubtId));
        Assert.Equal(last.Id.ToString(), Assert.Single(next.Items).DoubtId);
    }

    [Fact]
    public async Task The_queue_says_when_the_colour_seen_is_another_variants()
    {
        var doubt = PictureDoubt.Raise(
            Sleeve,
            new PictureReading(new ImageId("pannier-olive"), "qwen3.5-4b-q8_0", "picture-reading-test"),
            new ColourSeen("COLOR", "BLACK", LocalizedText.From("es", "negro").With("en", "black"), "OLIVE", LocalizedText.From("es", "oliva").With("en", "olive"), 0.97, false, OfAnotherVariant: true),
            this.clock)!;
        this.knowledge.Add(doubt);

        Assert.True(Assert.Single((await Queue()).Items).SeenOnAnotherVariant);
    }

    [Fact]
    public async Task A_person_decides_a_doubt_and_it_is_theirs()
    {
        var doubt = Doubted("sleeve-front");
        using var metrics = new InstrumentRecorder("tendero.knowledge.picture_doubts.decided");

        var result = await Decide(doubt.Id, PictureDoubtDecision.BelongsToAnotherVariant);

        Assert.Equal(DecidePictureDoubtOutcome.Decided, result.Outcome);
        Assert.Equal(
            (PictureDoubtStatus.Decided, (PictureDoubtDecision?)PictureDoubtDecision.BelongsToAnotherVariant, "Ana Tendera"),
            (doubt.Status, doubt.Decision, doubt.DecidedBy));
        Assert.Equal((AuditOutcome.Allowed, (string?)null), (((IAuditedResult)result).AuditOutcome, ((IAuditedResult)result).AuditReason));
        Assert.Equal("belongs_to_another_variant", Assert.Single(metrics.Of("tendero.knowledge.picture_doubts.decided"))["decision"]);
        Assert.Empty((await Queue()).Items);
    }

    /// <summary>Decided once: a second answer is refused, on its own audit row, and the first stands.</summary>
    [Fact]
    public async Task A_doubt_already_decided_is_not_decided_again()
    {
        var doubt = Doubted("sleeve-front");
        await Decide(doubt.Id, PictureDoubtDecision.PictureIsFine);
        var saves = this.knowledge.SaveCount;

        var again = await Decide(doubt.Id, PictureDoubtDecision.ReplaceThePicture, "Luis");

        Assert.Equal(DecidePictureDoubtOutcome.NotWaiting, again.Outcome);
        Assert.Equal((AuditOutcome.Denied, "NotWaiting"), (((IAuditedResult)again).AuditOutcome, ((IAuditedResult)again).AuditReason));
        Assert.Equal((PictureDoubtDecision?)PictureDoubtDecision.PictureIsFine, doubt.Decision);
        Assert.Equal(saves, this.knowledge.SaveCount);
    }

    [Fact]
    public async Task A_withdrawn_doubt_and_one_that_is_not_there_are_not_decided()
    {
        var withdrawn = Doubted("sleeve-front");
        withdrawn.Withdraw(this.clock);

        Assert.Equal(DecidePictureDoubtOutcome.NotWaiting, (await Decide(withdrawn.Id, PictureDoubtDecision.PictureIsFine)).Outcome);
        Assert.Equal(DecidePictureDoubtOutcome.NotFound, (await Decide(PictureDoubtId.New(), PictureDoubtDecision.PictureIsFine)).Outcome);
    }
}
