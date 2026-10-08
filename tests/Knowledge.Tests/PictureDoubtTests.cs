using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// A doubt about a picture (the phase 13a spec, I-4d): a picture shows another
/// colour than the variants it is of hold. **Not a claim** — approving a claim
/// would rewrite the catalogue's colour to match a drawing. A doubt says
/// something of the picture, and a person decides what: it is fine, it is to be
/// replaced, or it belongs to another variant. It informs and blocks nothing.
/// </summary>
public sealed class PictureDoubtTests
{
    private static readonly TestClock Clock = new();
    private static readonly ProductId Sleeve = ProductId.New();
    private static readonly PictureReading Front = new(new ImageId("sleeve-front"), "qwen3.5-4b-q8_0", "picture-reading-test");

    private static ColourSeen Seen(string code, double? confidence, bool related = false) =>
        new("COLOR", code, LocalizedText.From("es", "gris").With("en", "grey"), "BLACK", LocalizedText.From("es", "negro").With("en", "black"), confidence, related);

    [Fact]
    public void A_picture_the_model_is_sure_shows_another_colour_raises_a_doubt()
    {
        var doubt = PictureDoubt.Raise(Sleeve, Front, Seen("GREY", 0.94), Clock);

        Assert.NotNull(doubt);
        Assert.Equal(
            (Sleeve, "sleeve-front", "COLOR", "GREY", "BLACK", 0.94, PictureDoubtStatus.Open),
            (doubt.ProductId, doubt.PictureId, doubt.AttributeCode, doubt.Seen, doubt.Held, doubt.Confidence, doubt.Status));
        Assert.Equal(("gris", "black"), (doubt.SeenLabel.In("es"), doubt.HeldLabel.In("en")));
        Assert.Equal((Front.Model, Front.PromptVersion), (doubt.ReadByModel, doubt.ReadWithPrompt));
    }

    /// <summary>
    /// What raises none. The colour the catalogue holds; an answer the model was
    /// not sure of (it does not know — *olive* at 0.63 for forest green); one that
    /// came with no probability; and two names the catalogue says are one colour
    /// (*grey* for stainless steel).
    /// </summary>
    [Theory]
    [InlineData("BLACK", 0.99, false)]
    [InlineData("GREY", 0.89, false)]
    [InlineData("GREY", null, false)]
    [InlineData("GREY", 0.99, true)]
    public void No_doubt_is_raised_without_a_sure_difference(string seen, double? confidence, bool related)
    {
        Assert.Null(PictureDoubt.Raise(Sleeve, Front, Seen(seen, confidence, related), Clock));
    }

    [Fact]
    public void The_line_is_part_of_the_rule_and_at_the_line_a_doubt_is_raised()
    {
        Assert.NotNull(PictureDoubt.Raise(Sleeve, Front, Seen("GREY", PictureDoubt.SureDifference), Clock));
    }

    [Theory]
    [InlineData(PictureDoubtDecision.PictureIsFine)]
    [InlineData(PictureDoubtDecision.ReplaceThePicture)]
    [InlineData(PictureDoubtDecision.BelongsToAnotherVariant)]
    public void A_person_decides_a_doubt_and_it_says_who_and_what(PictureDoubtDecision decision)
    {
        var doubt = PictureDoubt.Raise(Sleeve, Front, Seen("GREY", 0.94), Clock)!;

        doubt.Decide(decision, "Ana Tendera", Clock);

        Assert.Equal(
            (PictureDoubtStatus.Decided, (PictureDoubtDecision?)decision, "Ana Tendera", (DateTimeOffset?)Clock.GetUtcNow()),
            (doubt.Status, doubt.Decision, doubt.DecidedBy, doubt.DecidedAt));
    }

    /// <summary>The picture was read again and no longer says it: nobody decided, the reading did.</summary>
    [Fact]
    public void A_doubt_the_next_reading_does_not_repeat_is_withdrawn()
    {
        var doubt = PictureDoubt.Raise(Sleeve, Front, Seen("GREY", 0.94), Clock)!;

        doubt.Withdraw(Clock);

        Assert.Equal((PictureDoubtStatus.Withdrawn, (PictureDoubtDecision?)null, (string?)null), (doubt.Status, doubt.Decision, doubt.DecidedBy));
    }

    /// <summary>The edges are the table's: what was decided is not decided again, and not withdrawn.</summary>
    [Fact]
    public void A_decided_doubt_is_neither_decided_again_nor_withdrawn()
    {
        var doubt = PictureDoubt.Raise(Sleeve, Front, Seen("GREY", 0.94), Clock)!;
        doubt.Decide(PictureDoubtDecision.PictureIsFine, "Ana Tendera", Clock);

        Assert.Throws<InvalidOperationException>(() => doubt.Decide(PictureDoubtDecision.ReplaceThePicture, "Luis", Clock));
        Assert.Throws<InvalidOperationException>(() => doubt.Withdraw(Clock));
        Assert.Equal(PictureDoubtDecision.PictureIsFine, doubt.Decision);
    }

    /// <summary>
    /// A person already answered this: the same picture, the same colour seen,
    /// the same colour held. Read again under a new prompt, it is not asked of
    /// them a second time.
    /// </summary>
    [Fact]
    public void A_doubt_says_whether_it_is_the_one_a_new_answer_would_raise()
    {
        var doubt = PictureDoubt.Raise(Sleeve, Front, Seen("GREY", 0.94), Clock)!;

        Assert.True(doubt.IsAbout(Seen("GREY", 0.99)));
        Assert.False(doubt.IsAbout(Seen("OLIVE", 0.99)));
        Assert.False(doubt.IsAbout(Seen("GREY", 0.99) with { Held = "WHITE" }));
    }
}
