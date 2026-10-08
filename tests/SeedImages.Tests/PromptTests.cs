using ElGuerre.Tendero.SeedImages;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// What the model is actually asked for.
/// </summary>
public sealed class PromptTests
{
    private static ColourLexicon Lexicon =>
        ColourLexicon.Read(Path.Combine(RepositoryRoot.Find(), "seed", "attributes.sample.json"));

    private static SeedProduct Shoes => new(
        "B073WXYZ01",
        "Pulse Runner cushioned running shoes",
        "Neutral running shoes with responsive foam midsole, breathable mesh upper and reinforced heel counter. 8 mm drop.",
        "azul marino");

    private static SeedProduct SpiceRack => new(
        "B14ORG0901",
        "Two-tier bamboo spice rack",
        "Two-tier bamboo rack that lets you see the back row. It fits inside a standard cupboard.",
        null);

    /// <summary>
    /// **The subject goes first, and this is the load-bearing test.** CLIP takes
    /// 77 tokens and throws the rest away without saying so, and the style block
    /// is long — six hex codes, each of which the tokenizer splits digit by
    /// digit. With the subject last, what gets discarded is the colour and the
    /// product: ninety-two beautiful illustrations of nothing in particular.
    /// </summary>
    [Fact]
    public void The_subject_comes_first_so_truncation_costs_style_and_not_the_product()
    {
        var prompt = PromptTemplate.Positive(Shoes, Lexicon);

        var subject = prompt.IndexOf("running shoes", StringComparison.Ordinal);
        var style = prompt.IndexOf("warm grey background", StringComparison.Ordinal);

        Assert.True(subject >= 0 && style >= 0, prompt);
        Assert.True(subject < style, $"The subject must precede the style block.\n\n{prompt}");
    }

    [Fact]
    public void A_declared_colour_is_named_in_english()
    {
        var prompt = PromptTemplate.Positive(Shoes, Lexicon);

        Assert.Contains("navy blue", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("azul marino", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The thirty-five without a colour. A builder that interpolates a null
    /// writes "The object is : a bamboo rack", and a colon about nothing is a
    /// token the model spends on punctuation.
    /// </summary>
    [Fact]
    public void A_product_without_a_colour_carries_no_dangling_clause()
    {
        var prompt = PromptTemplate.Positive(SpiceRack, Lexicon);

        Assert.DoesNotContain("is :", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bamboo rack", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// A set of three knives came back with four and a set of six tumblers with
    /// seven, under a prompt that named the number and then said "One single
    /// object". A product that declares its pieces is asked for exactly that many,
    /// each one whole — and in a word, because a figure in a prompt is drawn as a label.
    /// </summary>
    [Fact]
    public void A_set_is_asked_for_exactly_its_pieces_and_never_for_one_single_object()
    {
        var tumblers = new SeedProduct(
            "B11TAB0504",
            "Set of 6 recycled glass tumblers",
            "Six 350 ml tumblers blown from recycled glass. Each one comes out slightly different.",
            null,
            6);

        var prompt = PromptTemplate.Positive(tumblers, Lexicon);

        Assert.Contains("Exactly six of them", prompt, StringComparison.Ordinal);
        Assert.Contains("each one whole", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("One single object", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("6", prompt, StringComparison.Ordinal);
        // The rest of the register is the catalogue's, set or not.
        Assert.Contains("warm grey background", prompt, StringComparison.Ordinal);
        Assert.Contains("at most 70% of the frame height", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// An angle is a measurement too: "350 degrees of rotation" reached the
    /// generator and came back as `350°` lettered beside the desk lamp, with a
    /// little dial drawn for it. What it turns through goes with the figure, or
    /// the sentence is left saying "of rotation" about nothing.
    /// </summary>
    [Fact]
    public void An_angle_never_reaches_the_model_and_takes_what_it_measured_with_it()
    {
        Assert.Equal(
            "Double-arm lamp with a desk clamp, and dimmable light at three temperatures.",
            PromptTemplate.WithoutMeasurements(
                "Double-arm lamp with a desk clamp, 350 degrees of rotation and dimmable light at three temperatures."));
    }

    /// <summary>
    /// A redraw can be told what was wrong with the last one: the knives came back
    /// twice with two blades standing up out of the block. The hint follows the
    /// description, so it is read as part of what the object is.
    /// </summary>
    [Fact]
    public void A_redraw_carries_what_was_wrong_with_the_last_one_after_the_description()
    {
        var prompt = PromptTemplate.Positive(SpiceRack, Lexicon, "Every jar stands upright.");

        var description = prompt.IndexOf("standard cupboard.", StringComparison.Ordinal);
        var hint = prompt.IndexOf("Every jar stands upright.", StringComparison.Ordinal);
        var style = prompt.IndexOf("Bold clean outlines", StringComparison.Ordinal);

        Assert.True(description >= 0 && description < hint && hint < style, prompt);
    }

    [Fact]
    public void A_drawing_with_no_hint_is_the_prompt_it_always_was()
    {
        Assert.Equal(PromptTemplate.Positive(SpiceRack, Lexicon), PromptTemplate.Positive(SpiceRack, Lexicon, null));
        Assert.Equal(PromptTemplate.Positive(SpiceRack, Lexicon), PromptTemplate.Positive(SpiceRack, Lexicon, "  "));
    }

    /// <summary>One cast iron pan declares one piece, and most products declare none: both are one single object.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public void What_is_one_piece_or_declares_none_is_one_single_object(int? pieces)
    {
        var prompt = PromptTemplate.Positive(SpiceRack with { Pieces = pieces }, Lexicon);

        Assert.Contains("One single object", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Exactly", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The register is published in <c>seed/IMAGES-TODO.md</c> so a person can
    /// read what the hundred images are supposed to look like. If the code and
    /// the document drift, the document is the one people believe.
    /// </summary>
    [Fact]
    public void Every_style_line_appears_in_the_published_specification()
    {
        var published = File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "seed", "IMAGES-TODO.md"));

        var missing = PromptTemplate.StyleLines
            .Where(line => !published.Contains(line, StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"These lines are in the code and not in seed/IMAGES-TODO.md:\n  {string.Join("\n  ", missing)}");
    }

    /// <summary>
    /// **The negations live here and not in the positive prompt.** CLIP has no
    /// reliable negation: "no text, no logos" puts "text" and "logo" into the
    /// embedding like any other word. What actually pushes away from a concept is
    /// classifier-free guidance against a separate negative.
    /// </summary>
    [Fact]
    public void The_negative_prompt_carries_what_the_positive_cannot_say()
    {
        Assert.Contains("text", PromptTemplate.Negative, StringComparison.Ordinal);
        Assert.Contains("logo", PromptTemplate.Negative, StringComparison.Ordinal);
        Assert.Contains("watermark", PromptTemplate.Negative, StringComparison.Ordinal);

        Assert.DoesNotContain("No text", PromptTemplate.Positive(Shoes, Lexicon), StringComparison.Ordinal);
    }

    /// <summary>
    /// **Bought with three identical failures on the same shoe.** A trail shoe
    /// described as having "4 mm lugs" came back three times with `4 mm`
    /// lettered onto its midsole, and a coffee maker with a "24-hour timer" came
    /// back with `24h` on its display — each time after the instruction had been
    /// told, in plain words, not to write numbers on the product. A measurement
    /// sitting beside a part reads as that part's label, and asking harder does
    /// not change that.
    ///
    /// So the figure never reaches the model. It was not earning its place
    /// anyway: 4 mm of lug is a moulded sole at any scale.
    /// </summary>
    [Fact]
    public void A_measurement_never_reaches_the_model_because_it_gets_drawn_as_a_label()
    {
        Assert.Equal(
            "Neutral running shoes with responsive foam midsole, breathable mesh upper "
            + "and reinforced heel counter.",
            PromptTemplate.WithoutMeasurements(Shoes.EnglishDescription));

        Assert.DoesNotContain("8 mm", PromptTemplate.Positive(Shoes, Lexicon), StringComparison.Ordinal);
    }

    /// <summary>
    /// **A run of figures sharing one unit is one measurement.** Only the last of
    /// them carries the unit, so matching them separately leaves `20, 24 and
    /// pans` — worse than the sentence it replaced, and the kind of damage a
    /// regular expression does quietly.
    /// </summary>
    [Fact]
    public void A_list_of_sizes_sharing_one_unit_goes_out_together()
    {
        Assert.Equal(
            "pans with a steel base.",
            PromptTemplate.WithoutMeasurements("20, 24 and 28 cm pans with a steel base."));
    }

    /// <summary>
    /// A count is not a measurement: the three pans in a set of three ARE the
    /// product, and a shade with no number left is a different drawing.
    /// </summary>
    [Fact]
    public void A_count_survives_because_it_is_part_of_the_object()
    {
        Assert.Equal(
            "Set of 3 nesting bowls.",
            PromptTemplate.WithoutMeasurements("Set of 3 nesting bowls."));
    }

    /// <summary>
    /// **Bought with two identical failures** (2026-10-06): a house slipper described
    /// as "Hand washable." came back twice with a round emblem on its side — the
    /// hand-wash symbol of a care label — against a prompt that says no labels and
    /// no badges. How a thing is washed is not something it looks like, so the
    /// sentence never reaches the model.
    /// </summary>
    [Theory]
    [InlineData("Wool-lined house slipper with a non-slip sole. Hand washable.", "Wool-lined house slipper with a non-slip sole.")]
    [InlineData("A cotton throw. Machine-washable at low temperature. Fringed ends.", "A cotton throw. Fringed ends.")]
    [InlineData("Wool coat with horn buttons. Dry clean only.", "Wool coat with horn buttons.")]
    public void How_a_thing_is_washed_never_reaches_the_model_because_it_gets_drawn_as_a_care_label(string description, string drawn)
    {
        Assert.Equal(drawn, PromptTemplate.WithoutCareInstructions(description));
    }

    /// <summary>
    /// Only the instruction goes. A wash bag is a bag, washed linen is a cloth, and
    /// a dish that goes in the dishwasher says so beside what it is made of: none of
    /// them is a care label, and a rule that took their sentences would take the product.
    /// </summary>
    [Theory]
    [InlineData("Wash bag with a hanging hook, a waterproof lining and a removable mirror.")]
    [InlineData("Washed linen shirt with a soft collar and a single pocket.")]
    [InlineData("Glazed ceramic dish for the oven, the microwave and the dishwasher.")]
    [InlineData("Pre-seasoned cast iron pan for induction, oven and open flame.")]
    public void What_only_mentions_washing_is_still_the_product(string description)
    {
        Assert.Equal(description, PromptTemplate.WithoutCareInstructions(description));
    }

    /// <summary>
    /// Removing "8 mm" from "8 mm drop." leaves the sentence "drop.", which is
    /// not a fact about anything. A sentence down to one word was the
    /// measurement's own sentence, so it leaves with it.
    /// </summary>
    [Fact]
    public void A_sentence_that_was_only_its_measurement_leaves_with_it()
    {
        Assert.Equal(
            "An abrasion-resistant upper.",
            PromptTemplate.WithoutMeasurements("An abrasion-resistant upper. 8 mm drop."));
    }
    /// <summary>
    /// **A variant's picture is its product's cover in another colour** (phase
    /// 13a, I-4d): the instruction names the two colours, names everything that
    /// must stay — or it gets redrawn — and says the one thing a dark colour
    /// needs said, because the first black pannier came back as a silhouette.
    /// </summary>
    [Fact]
    public void A_recolour_names_the_two_colours_and_everything_that_must_stay()
    {
        var instruction = PromptTemplate.Recolour("olive", "black");

        Assert.Contains("which is olive, becomes black", instruction, StringComparison.Ordinal);
        Assert.Contains("Only the surfaces that are olive change", instruction, StringComparison.Ordinal);
        foreach (var kept in new[] { "the same object", "the same shape", "the same viewing angle", "the same size and", "the same plain background" })
            Assert.Contains(kept, instruction, StringComparison.Ordinal);
        Assert.Contains("never as a flat silhouette", instruction, StringComparison.Ordinal);
        Assert.Contains("do not add", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void A_recolour_of_a_picture_whose_colour_nobody_declared_still_says_what_it_becomes()
    {
        var instruction = PromptTemplate.Recolour(null, "black", "The handle stays black.");

        Assert.Contains("the product's main colour becomes black", instruction, StringComparison.Ordinal);
        Assert.Contains("Only the surfaces that are the main colour change", instruction, StringComparison.Ordinal);
        Assert.EndsWith("The handle stays black.", instruction.TrimEnd(), StringComparison.Ordinal);
    }
}
