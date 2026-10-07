using ElGuerre.Tendero.SeedImages;
using SkiaSharp;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// The model reads and code compares (the phase 13a spec, I-2b; invariant 12).
/// A question whose answer is a value — how many, which colour — is held to the
/// product's own attribute here, by code, and the model is never asked whether
/// the two agree. The model is a fake that answers each question by its schema.
/// </summary>
public sealed class PictureExaminerTests
{
    /// <summary>Answers each question with what a test says the picture shows, and keeps what was asked.</summary>
    private sealed class Sees : IVisionModel
    {
        public bool Text { get; init; }
        public bool Scene { get; init; }
        public string Colour { get; init; } = "grey";
        public bool Mark { get; init; }
        public int Count { get; init; } = 1;
        public bool Matches { get; init; } = true;
        public bool SameRegister { get; init; } = true;
        public string[] Others { get; init; } = [];

        public List<string> Asked { get; } = [];

        public string Name => "sees";

        public void EnsureReachable()
        {
        }

        public string Ask(IReadOnlyList<byte[]> pngs, string question, string schema)
        {
            Asked.Add(schema);

            if (schema.Contains("\"has_mark\"", StringComparison.Ordinal))
                return $$"""{"has_mark": {{Json(Mark)}}}""";
            if (schema.Contains("\"others\"", StringComparison.Ordinal))
                return $$"""{"product": "a thing", "others": [{{string.Join(", ", Others.Select(other => $"\"{other}\""))}}]}""";
            if (schema.Contains("\"count\"", StringComparison.Ordinal))
                return $$"""{"count": {{Count}}}""";
            if (schema.Contains("\"matches\"", StringComparison.Ordinal))
                return $$"""{"shows": "something", "matches": {{Json(Matches)}}, "differs": "it differs"}""";
            if (schema.Contains("\"same_style\"", StringComparison.Ordinal))
                return $$"""{"same_style": {{Json(SameRegister)}}, "same_background": {{Json(SameRegister)}}, "differs": "a gradient"}""";

            return $$"""{"has_text": {{Json(Text)}}, "dominant_colour": "{{Colour}}", "has_scene": {{Json(Scene)}}}""";
        }

        public string Describe(byte[] png) => "";

        private static string Json(bool value) => value ? "true" : "false";
    }

    private static ColourLexicon Colours =>
        ColourLexicon.Read(Path.Combine(RepositoryRoot.Find(), "seed", "attributes.sample.json"));

    private static byte[] Picture()
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, 90);
        return encoded.ToArray();
    }

    private static SeedProduct Product(string? colour = "gris", int? pieces = null) =>
        new("B00TEST0001", "A thing", "A thing, described.", colour, pieces);

    private static Examination Examine(Sees model, SeedProduct? product, bool beside = true) =>
        new PictureExaminer(new VisionChecks(model), Colours).Examine(Picture(), product, beside ? Picture() : null, "B10SHO0101");

    [Fact]
    public void A_picture_with_nothing_wrong_raises_no_doubt()
    {
        var examination = Examine(new Sees(), Product());

        Assert.Empty(examination.Doubts);
        Assert.Equal("grey", examination.Colour);
    }

    /// <summary>One pan for a set of three: the model says how many it sees, and the comparison with the catalogue is code's.</summary>
    [Theory]
    [InlineData(3, 3, false)]
    [InlineData(1, 3, true)]
    [InlineData(6, 3, true)]
    public void The_count_the_model_reads_is_compared_with_the_products_pieces_by_code(int seen, int pieces, bool doubted)
    {
        var examination = Examine(new Sees { Count = seen }, Product(pieces: pieces));

        Assert.Equal(doubted, examination.Raises(Question.Count));
        if (doubted)
            Assert.Contains($"{seen} drawn, the catalogue says {pieces}", examination.Doubts.Single().Says);
    }

    /// <summary>A pair of shoes declares no pieces: nothing to compare with, so the question is not asked at all.</summary>
    [Fact]
    public void A_product_that_declares_no_pieces_is_not_asked_how_many()
    {
        var model = new Sees { Count = 7 };

        var examination = Examine(model, Product(pieces: null));

        Assert.False(examination.Raises(Question.Count));
        Assert.DoesNotContain(model.Asked, schema => schema.Contains("\"count\"", StringComparison.Ordinal));
    }

    /// <summary>A symbol is not text: it has a question of its own, apart from the one about letters.</summary>
    [Fact]
    public void A_symbol_on_the_product_is_a_doubt_of_its_own()
    {
        var examination = Examine(new Sees { Mark = true }, Product());

        Assert.Equal([Question.Mark], examination.Doubts.Select(doubt => doubt.Question));
    }

    /// <summary>
    /// Asked yes or no, the model said no to all 28 pictures with a surface and a
    /// ball drawn in — and, asked what it saw, named both. So it lists what else is
    /// drawn, and that the list is not empty is code's to say.
    /// </summary>
    [Fact]
    public void What_the_model_lists_besides_the_product_is_a_scene_and_the_doubt_names_it()
    {
        var examination = Examine(new Sees { Others = ["red circle", "brown bar"] }, Product());

        var doubt = Assert.Single(examination.Doubts);
        Assert.Equal(Question.Scene, doubt.Question);
        Assert.Contains("red circle, brown bar", doubt.Says);
    }

    /// <summary>
    /// Every picture of the catalogue has a soft shadow under the product and a
    /// background behind it, and the model lists either as a thing drawn: two of
    /// the seven right pictures it doubted on the first measurement were doubted
    /// for "background" alone.
    /// </summary>
    [Theory]
    [InlineData("shadow")]
    [InlineData("soft Shadow under the lamp")]
    [InlineData("background")]
    [InlineData("plain Background")]
    public void The_products_own_shadow_and_the_background_are_not_a_scene(string listed)
    {
        Assert.Empty(Examine(new Sees { Others = [listed] }, Product()).Doubts);
    }

    /// <summary>The yes-or-no answer is still heard: either voice is enough for a person to look.</summary>
    [Fact]
    public void A_scene_the_model_only_says_yes_to_is_still_a_scene()
    {
        Assert.True(Examine(new Sees { Scene = true }, Product()).Raises(Question.Scene));
    }

    [Fact]
    public void The_colour_the_model_reads_is_compared_with_the_catalogues_by_code()
    {
        Assert.True(Examine(new Sees { Colour = "navy blue" }, Product(colour: "gris")).Raises(Question.Colour));
        Assert.False(Examine(new Sees { Colour = "grey" }, Product(colour: "gris")).Raises(Question.Colour));
        // "other" is the model saying it is none of the nine: nothing to hold against the catalogue.
        Assert.False(Examine(new Sees { Colour = "other" }, Product(colour: "gris")).Raises(Question.Colour));
        // Thirty-five products declare no colour.
        Assert.False(Examine(new Sees { Colour = "navy blue" }, Product(colour: null)).Raises(Question.Colour));
    }

    [Fact]
    public void Every_question_that_finds_something_says_so_under_its_own_name()
    {
        var everything = new Sees { Text = true, Scene = true, Others = ["a desk"], Colour = "black", Mark = true, Count = 2, Matches = false, SameRegister = false };

        var examination = Examine(everything, Product(pieces: 1));

        Assert.Equal(
            [Question.Text, Question.Mark, Question.Scene, Question.Colour, Question.Count, Question.Description, Question.Register],
            examination.Doubts.Select(doubt => doubt.Question).Order());
    }

    /// <summary>The reference picture is never held to itself, and a file that is no product of the catalogue is asked only what needs none.</summary>
    [Fact]
    public void What_has_nothing_to_be_compared_with_is_not_compared()
    {
        var model = new Sees { Matches = false, SameRegister = false, Count = 9, Colour = "black" };

        var examination = Examine(model, product: null, beside: false);

        Assert.Empty(examination.Doubts);
    }
}
