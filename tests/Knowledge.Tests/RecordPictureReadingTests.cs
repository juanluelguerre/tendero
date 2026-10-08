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

    private Task<RecordPictureReadingResult> Read(PictureReading reading, string spanish, string english, int? count = 1) =>
        new RecordPictureReadingHandler(this.knowledge, this.knowledge, this.knowledge, this.clock)
            .HandleAsync(new RecordPictureReadingCommand(Pan, reading, spanish, english, count), TestContext.Current.CancellationToken);

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

    /// <summary>
    /// What the picture answered is part of the document: the count is written
    /// under the model's words in each language, where a claim can quote it and a
    /// reviewer reads it beside the picture.
    /// </summary>
    [Fact]
    public async Task What_the_picture_answered_is_written_under_the_words_in_each_language()
    {
        await Read(Front, "Tres sartenes negras.", "Three black pans.", count: 3);

        Assert.Equal(
            ["Three black pans.\nItems counted in the picture: 3.", "Tres sartenes negras.\nElementos contados en la imagen: 3."],
            this.knowledge.Documents.Select(document => document.Text).Order(StringComparer.Ordinal));
    }

    /// <summary>A picture that was not asked how many has only its words: no line is written for an answer nobody gave.</summary>
    [Fact]
    public async Task A_picture_that_was_not_asked_how_many_is_recorded_as_its_words_alone()
    {
        await Read(Front, "Una zapatilla gris.", "A grey shoe.", count: null);

        Assert.Equal(
            ["A grey shoe.", "Una zapatilla gris."],
            this.knowledge.Documents.Select(document => document.Text).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The same words and another count is another reading: the answer is in the
    /// document's identity, so the claim that rested on the old count falls.
    /// </summary>
    [Fact]
    public async Task The_same_words_with_another_count_are_a_new_reading_and_supersede_the_old_ones_claims()
    {
        await Read(Front, "Sartenes negras.", "Black pans.", count: 2);
        var claim = ClaimOn(this.knowledge.Documents.First(document => document.Culture == "es"));

        this.clock.Advance(TimeSpan.FromMinutes(1));
        var again = await Read(Front, "Sartenes negras.", "Black pans.", count: 3);

        Assert.Equal((RecordPictureReadingOutcome.Recorded, 1), (again.Outcome, again.Superseded));
        Assert.Equal(ClaimStatus.Superseded, claim.Status);
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
    /// <summary>The probability the model gave its count is part of what the picture answered, and of the document.</summary>
    [Fact]
    public async Task How_sure_the_model_was_of_its_count_is_recorded_with_it()
    {
        await new RecordPictureReadingHandler(this.knowledge, this.knowledge, this.knowledge, this.clock).HandleAsync(
            new RecordPictureReadingCommand(Pan, Front, "Tres sartenes.", "Three pans.", 3, 0.981), TestContext.Current.CancellationToken);

        Assert.Contains(this.knowledge.Documents, document => document.Text == "Three pans.\nItems counted in the picture: 3 (confidence 98 %).");
        Assert.Contains(this.knowledge.Documents, document => document.Text == "Tres sartenes.\nElementos contados en la imagen: 3 (confianza 98 %).");
    }
    // ---------- a colour that differs is a doubt about the picture (I-4d) ----------

    private static ColourSeen Grey(double? confidence = 0.94, string held = "BLACK", bool related = false) =>
        new("COLOR", "GREY", LocalizedText.From("es", "gris").With("en", "grey"), held, LocalizedText.From("es", "negro").With("en", "black"), confidence, related);

    private Task<RecordPictureReadingResult> ReadWithColour(string spanish, string english, ColourSeen? colour) =>
        new RecordPictureReadingHandler(this.knowledge, this.knowledge, this.knowledge, this.clock).HandleAsync(
            new RecordPictureReadingCommand(Pan, Front, spanish, english, null, null, colour), TestContext.Current.CancellationToken);

    /// <summary>
    /// The model is sure the picture shows grey and the catalogue holds black: a
    /// doubt, open, about that picture — and the colour is written under the
    /// reading in each language. No claim: nothing here would change the catalogue.
    /// </summary>
    [Fact]
    public async Task A_sure_colour_that_differs_raises_a_doubt_about_the_picture_and_no_claim()
    {
        var result = await ReadWithColour("Una funda de fieltro.", "A felt sleeve.", Grey());

        Assert.True(result.DoubtRaised);
        var doubt = Assert.Single(this.knowledge.Doubts);
        Assert.Equal((Pan, "front", "GREY", "BLACK", PictureDoubtStatus.Open), (doubt.ProductId, doubt.PictureId, doubt.Seen, doubt.Held, doubt.Status));
        Assert.Contains(this.knowledge.Documents, document => document.Text == "A felt sleeve.\nColour seen in the picture: grey (confidence 94 %).");
        Assert.Contains(this.knowledge.Documents, document => document.Text == "Una funda de fieltro.\nColor visto en la imagen: gris (confianza 94 %).");
        Assert.Empty(this.knowledge.Claims);
    }

    /// <summary>The colour the catalogue holds, an unsure answer, or two names for one colour: the answer is kept in the reading and nothing is raised.</summary>
    [Theory]
    [InlineData(0.99, "GREY", false)]
    [InlineData(0.63, "BLACK", false)]
    [InlineData(0.99, "STAINLESS_STEEL", true)]
    public async Task What_is_no_sure_difference_is_kept_in_the_reading_and_raises_nothing(double confidence, string held, bool related)
    {
        var result = await ReadWithColour("Una funda.", "A sleeve.", Grey(confidence, held, related));

        Assert.False(result.DoubtRaised);
        Assert.Empty(this.knowledge.Doubts);
        Assert.Contains(this.knowledge.Documents, document => document.Text.Contains("Colour seen in the picture: grey", StringComparison.Ordinal));
    }

    /// <summary>Read again to the colour the catalogue holds: the doubt is what the picture said, and it no longer says it.</summary>
    [Fact]
    public async Task A_doubt_the_next_reading_does_not_repeat_is_withdrawn()
    {
        await ReadWithColour("Una funda.", "A sleeve.", Grey());
        this.clock.Advance(TimeSpan.FromMinutes(1));

        await ReadWithColour("Una funda negra.", "A black sleeve.", Grey(0.99, held: "GREY"));

        Assert.Equal(PictureDoubtStatus.Withdrawn, Assert.Single(this.knowledge.Doubts).Status);
    }

    /// <summary>Read again to the same colour, in other words: the doubt that is open stays the one doubt.</summary>
    [Fact]
    public async Task The_same_doubt_is_not_raised_twice()
    {
        await ReadWithColour("Una funda.", "A sleeve.", Grey());
        this.clock.Advance(TimeSpan.FromMinutes(1));

        var again = await ReadWithColour("Una funda de fieltro.", "A felt sleeve.", Grey(0.97));

        Assert.False(again.DoubtRaised);
        Assert.Equal(PictureDoubtStatus.Open, Assert.Single(this.knowledge.Doubts).Status);
    }

    /// <summary>
    /// A person said the picture is fine. A new prompt reads it again and sees
    /// grey again: they are not asked a second time what they already answered.
    /// </summary>
    [Fact]
    public async Task A_doubt_a_person_already_decided_is_not_put_to_them_again()
    {
        await ReadWithColour("Una funda.", "A sleeve.", Grey());
        this.knowledge.Doubts.Single().Decide(PictureDoubtDecision.PictureIsFine, "Ana Tendera", this.clock);
        this.clock.Advance(TimeSpan.FromMinutes(1));

        var again = await ReadWithColour("Una funda de fieltro.", "A felt sleeve.", Grey(0.97));

        Assert.False(again.DoubtRaised);
        Assert.Equal(PictureDoubtStatus.Decided, Assert.Single(this.knowledge.Doubts).Status);
    }

    /// <summary>And when it now sees another colour again — not the one decided — that is a new doubt.</summary>
    [Fact]
    public async Task Another_colour_than_the_one_decided_is_a_new_doubt()
    {
        await ReadWithColour("Una funda.", "A sleeve.", Grey());
        this.knowledge.Doubts.Single().Decide(PictureDoubtDecision.PictureIsFine, "Ana Tendera", this.clock);
        this.clock.Advance(TimeSpan.FromMinutes(1));

        var olive = Grey() with { Seen = "OLIVE", SeenLabel = LocalizedText.From("es", "oliva").With("en", "olive") };
        var again = await ReadWithColour("Una funda verde.", "A green sleeve.", olive);

        Assert.True(again.DoubtRaised);
        Assert.Equal(["GREY", "OLIVE"], this.knowledge.Doubts.Select(doubt => doubt.Seen));
    }

    /// <summary>A picture read again and not asked its colour — it is no longer of one — has nothing left in doubt.</summary>
    [Fact]
    public async Task A_picture_no_longer_asked_its_colour_has_its_open_doubt_withdrawn()
    {
        await ReadWithColour("Una funda.", "A sleeve.", Grey());
        this.clock.Advance(TimeSpan.FromMinutes(1));

        await ReadWithColour("Una funda de fieltro.", "A felt sleeve.", null);

        Assert.Equal(PictureDoubtStatus.Withdrawn, Assert.Single(this.knowledge.Doubts).Status);
    }
}
