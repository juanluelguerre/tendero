using ElGuerre.Tendero.SeedImages;
using SkiaSharp;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// What a picture is asked and how the answer is read, whichever model answers
/// (the phase 13a spec, I-1). The model here is a fake that says what a test
/// tells it to: what a real one answers is measured by `pairs`, not asserted.
/// </summary>
public sealed class VisionChecksTests
{
    /// <summary>Answers what it is told, and keeps what it was asked.</summary>
    private sealed class Told(string answer) : IVisionModel
    {
        public List<(int Pictures, string Question, string Schema)> Asked { get; } = [];

        public string Name => "told";

        public void EnsureReachable()
        {
        }

        public string Ask(IReadOnlyList<byte[]> pngs, string question, string schema)
        {
            Asked.Add((pngs.Count, question, schema));
            Assert.All(pngs, png => Assert.Equal(PngSignature, png[..4]));
            return answer;
        }

        public string Answer(byte[] png, string question)
        {
            Answered = question;
            return answer;
        }

        public string? Answered { get; private set; }

        public string Describe(byte[] png) => answer;
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47];

    /// <summary>A picture as the tool writes it: WebP, which neither server decodes.</summary>
    private static byte[] WebP()
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, 90);
        return encoded.ToArray();
    }

    /// <summary>
    /// A picture is asked seven questions, and each sends it as a PNG: it is
    /// turned into one once. Over the whole board that was most of what a run
    /// with every answer remembered still cost — eleven minutes of encoding.
    /// The same picture is the same array; another array is another picture,
    /// even when its bytes are equal, and is converted for itself.
    /// </summary>
    [Fact]
    public void A_picture_is_turned_into_a_png_once_however_many_times_it_is_sent()
    {
        var picture = WebP();

        var first = VisionChecks.AsPng(picture);

        Assert.Same(first, VisionChecks.AsPng(picture));
        Assert.Equal(PngSignature, first[..4]);

        var twin = VisionChecks.AsPng([.. picture]);
        Assert.NotSame(first, twin);
        Assert.Equal(first, twin);
    }

    /// <summary>
    /// The schema's names are snake_case, and a parser that does not reach them
    /// does not fail: it binds nothing and turns a right answer into "no text, no
    /// scene". The first version of this tool did exactly that.
    /// </summary>
    [Fact]
    public void The_three_closed_questions_are_read_by_their_snake_case_names()
    {
        var model = new Told("""{"has_text": true, "dominant_colour": "navy blue", "has_scene": true}""");

        var verdict = new VisionChecks(model).Look(WebP(), ["navy blue", "grey"]);

        Assert.Equal(new VisionVerdict(true, "navy blue", true), verdict);
        // The colours offered are the catalogue's, and "other" beside them.
        Assert.Contains("\"enum\": [\"navy blue\", \"grey\", \"other\"]", model.Asked.Single().Schema);
    }

    /// <summary>The picture is sent as PNG, one of them, with the product's own name and description in the question.</summary>
    [Fact]
    public void A_picture_is_held_to_its_products_name_and_description()
    {
        var model = new Told("""{"shows": "a single frying pan", "matches": false, "differs": "one pan, not a set of three"}""");

        var verdict = new VisionChecks(model).Against(WebP(), "Set of 3 frying pans", "Three non-stick pans with a steel base.");

        Assert.Equal(new DescriptionVerdict("a single frying pan", false, "one pan, not a set of three"), verdict);
        var asked = model.Asked.Single();
        Assert.Equal(1, asked.Pictures);
        Assert.Contains("\"Set of 3 frying pans\"", asked.Question);
        Assert.Contains("\"Three non-stick pans with a steel base.\"", asked.Question);
        // What the model sees is asked before it is told what to expect.
        Assert.True(asked.Question.IndexOf("shows:", StringComparison.Ordinal) < asked.Question.IndexOf("The shop sells it as", StringComparison.Ordinal));
    }

    /// <summary>Two pictures go, the reference first: "the first" and "the second" in the question are that order.</summary>
    [Fact]
    public void A_candidate_is_shown_beside_the_reference_with_the_reference_first()
    {
        var model = new Told("""{"same_style": true, "same_background": false, "differs": "a grey gradient behind it"}""");

        var verdict = new VisionChecks(model).Beside(WebP(), WebP());

        Assert.Equal(new RegisterVerdict(true, false, "a grey gradient behind it"), verdict);
        Assert.Equal(2, model.Asked.Single().Pictures);
        Assert.Contains("the first is the reference, the second is the candidate", model.Asked.Single().Question);
    }

    /// <summary>A model's answer is data: when it is not the shape asked for, the failure says what came back and which model said it.</summary>
    [Theory]
    [InlineData("I'm sorry, I cannot see images.")]
    [InlineData("null")]
    public void An_answer_that_is_not_the_shape_asked_for_says_what_came_back(string answer)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => new VisionChecks(new Told(answer)).Look(WebP(), ["grey"]));

        Assert.Contains("'told' did not answer in the shape it was asked for", failure.Message);
        Assert.Contains(answer, failure.Message);
    }

    [Fact]
    public void What_is_not_a_picture_is_refused_before_any_model_is_asked()
    {
        var model = new Told("{}");

        Assert.Throws<InvalidDataException>(() => new VisionChecks(model).Look([1, 2, 3], ["grey"]));
        Assert.Empty(model.Asked);
    }
    // ---------- the colour, in plain text (phase 13a, I-4d) ----------

    /// <summary>The smallest picture there is: the checks decode what they are given before they send it.</summary>
    private static readonly byte[] APicture = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==");

    private static readonly string[] Colours = ["black", "navy blue", "terracotta"];

    /// <summary>
    /// The colour is the one of the catalogue's the model wrote, however it
    /// wrapped it — and it was asked the shop's own question, with the options
    /// listed, in plain text.
    /// </summary>
    [Theory]
    [InlineData("terracotta", "terracotta")]
    [InlineData(" \"Navy Blue\".\n", "navy blue")]
    public void The_colour_is_the_catalogues_colour_the_model_wrote(string said, string colour)
    {
        var model = new Told(said);

        Assert.Equal(colour, new VisionChecks(model).ColourOf(APicture, Colours));
        Assert.Contains("What is the main colour of the product itself?", model.Answered, StringComparison.Ordinal);
        Assert.Contains("black; navy blue; terracotta.", model.Answered, StringComparison.Ordinal);
    }

    /// <summary>What is none of the catalogue's colours is no colour: not the nearest, and not the first.</summary>
    [Theory]
    [InlineData("burnt orange")]
    [InlineData("black or navy blue")]
    [InlineData("")]
    public void What_is_none_of_the_catalogues_colours_is_no_colour(string said)
    {
        Assert.Null(new VisionChecks(new Told(said)).ColourOf(APicture, Colours));
    }
}
