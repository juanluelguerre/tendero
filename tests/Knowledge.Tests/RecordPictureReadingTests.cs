using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Features.RecordDescription;
using ElGuerre.Tendero.Knowledge.Features.ReadPictures;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// What a model read in a picture, recorded as documents (the phase 13a spec,
/// I-4): the fast half, with no model in it. The reading arrives as values — the
/// picture, who read it, and its words in each language — and is stored by what
/// it is, as a description is.
/// </summary>
public sealed class RecordPictureReadingTests
{
    private readonly TestClock clock = new();
    private readonly InMemoryKnowledge knowledge = new();
    private static readonly ProductId Pan = ProductId.New();
    private static readonly PictureReading Front = new(new ImageId("front"), "Qwen/Qwen3.5-4B", "picture-2026-10-07");

    private Task<RecordPictureReadingResult> Read(PictureReading reading, string spanish, string english) =>
        new RecordPictureReadingHandler(this.knowledge, this.knowledge, this.clock)
            .HandleAsync(new RecordPictureReadingCommand(Pan, reading, spanish, english), TestContext.Current.CancellationToken);

    private Task<RecordDescriptionResult> Describe(string culture, string text) =>
        new RecordDescriptionHandler(this.knowledge, this.knowledge, this.clock)
            .HandleAsync(new RecordDescriptionCommand(Pan, culture, text), TestContext.Current.CancellationToken);

    private ProductClaim ClaimOn(SourceDocument document)
    {
        var claim = ProductClaim.Propose(
            Pan,
            LocalizedText.From("es", "Tiene mango largo.").With("en", "It has a long handle."),
            null,
            null,
            [new Evidence(document.Id, new Quote(document.Text, 0, document.Text.Length), 0.9)],
            new Provenance("test", null, null, null, "run", this.clock.GetUtcNow()),
            this.clock);
        this.knowledge.Add(claim);
        return claim;
    }

    [Fact]
    public async Task A_reading_is_a_document_in_each_language_that_says_which_picture_and_who_read_it()
    {
        var result = await Read(Front, "Una sartén de hierro con mango largo.", "An iron pan with a long handle.");

        Assert.Equal(RecordPictureReadingOutcome.Recorded, result.Outcome);
        Assert.Equal(["en", "es"], this.knowledge.Documents.Select(document => document.Culture).Order());
        Assert.All(this.knowledge.Documents, document =>
        {
            Assert.Equal(SourceKind.ProductImage, document.Kind);
            Assert.Equal(Front, document.Reading);
            Assert.True(document.IsWaiting);
        });
        Assert.Equal(1, this.knowledge.SaveCount);
    }

    /// <summary>The job reads a picture again and the model says the same: nothing to store, and nothing superseded.</summary>
    [Fact]
    public async Task The_same_words_about_the_same_picture_again_are_nothing()
    {
        await Read(Front, "Una sartén de hierro.", "An iron pan.");

        var again = await Read(Front, "Una sartén de hierro.", "An iron pan.");

        Assert.Equal(RecordPictureReadingOutcome.Unchanged, again.Outcome);
        Assert.Equal(2, this.knowledge.Documents.Count);
    }

    /// <summary>A model with nothing to say about a picture has not read it.</summary>
    [Theory]
    [InlineData("", "An iron pan.")]
    [InlineData("Una sartén de hierro.", "  ")]
    public async Task A_reading_missing_a_language_is_not_recorded(string spanish, string english)
    {
        var result = await Read(Front, spanish, english);

        Assert.Equal(RecordPictureReadingOutcome.Unchanged, result.Outcome);
        Assert.Empty(this.knowledge.Documents);
    }

    /// <summary>
    /// Read again to other words — a new prompt, another model — the claims that
    /// rested on the old words fall, as a description's do. The claims resting on
    /// the description, or on another picture's reading, are not this picture's.
    /// </summary>
    [Fact]
    public async Task A_new_reading_of_a_picture_supersedes_only_what_rested_on_its_old_one()
    {
        await Describe("es", "Sartén de hierro fundido.");
        await Read(Front, "Una sartén con mango largo.", "A pan with a long handle.");
        var side = Front with { Picture = new ImageId("side") };
        await Read(side, "Una sartén vista de lado.", "A pan seen from the side.");

        var onDescription = ClaimOn(this.knowledge.Documents.Single(document => document.Kind == SourceKind.ProductDescription));
        var onFront = ClaimOn(this.knowledge.Documents.First(document => document.Reading?.Picture == Front.Picture && document.Culture == "en"));
        var onSide = ClaimOn(this.knowledge.Documents.First(document => document.Reading?.Picture == side.Picture));

        this.clock.Advance(TimeSpan.FromMinutes(1));
        var result = await Read(Front, "Una sartén negra con mango largo.", "A black pan with a long handle.");

        Assert.Equal(1, result.Superseded);
        Assert.Equal(ClaimStatus.Superseded, onFront.Status);
        Assert.Equal(ClaimStatus.Proposed, onDescription.Status);
        Assert.Equal(ClaimStatus.Proposed, onSide.Status);
    }

    /// <summary>
    /// **A picture's reading is not the product's description.** Both are the
    /// product's documents, and "the newest" among them without regard to kind
    /// would make the reading pass for the description: the same description
    /// arriving again would be stored a second time and take its own claims down.
    /// </summary>
    [Fact]
    public async Task A_reading_recorded_after_the_description_does_not_stand_in_for_it()
    {
        await Describe("es", "Sartén de hierro fundido.");
        var claim = ClaimOn(this.knowledge.Documents.Single());

        this.clock.Advance(TimeSpan.FromMinutes(1));
        await Read(Front, "Una sartén de hierro.", "An iron pan.");
        this.clock.Advance(TimeSpan.FromMinutes(1));
        var again = await Describe("es", "Sartén de hierro fundido.");

        Assert.Equal(RecordDescriptionOutcome.Unchanged, again.Outcome);
        Assert.Equal(ClaimStatus.Proposed, claim.Status);
        Assert.Equal(3, this.knowledge.Documents.Count);
    }
}
