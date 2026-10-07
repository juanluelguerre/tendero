using ElGuerre.Tendero.SearchEval.Suites;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// The parts of the ImplicitAVE suite that are arithmetic and parsing (phase 13a):
/// the benchmark's own file format, the closed question each example becomes, and
/// the score — so that a number it prints is the model's and not the reader's.
/// </summary>
public sealed class ImplicitAveTests
{
    private const string Header = "id\timages\tcategory\ttexts\tattribute_names\tattribute_values";

    [Fact]
    public void A_row_is_an_example_of_its_domain()
    {
        var examples = ImplicitAveSet.Parse("Home", $"{Header}\r\nB01\tB01.jpg\tCandles\t[Title] A lavender candle\tSeason\tAll Seasons\r\n");

        Assert.Equal(
            [new ImplicitAveExample("Home", "B01", "B01.jpg", "Candles", "[Title] A lavender candle", "Season", "All Seasons")],
            examples);
    }

    /// <summary>The files are written as a spreadsheet writes them: a title with an inch mark is quoted, and its quote doubled.</summary>
    [Fact]
    public void A_quoted_field_is_read_as_what_it_says()
    {
        var example = Assert.Single(ImplicitAveSet.Parse(
            "Footwear", $"{Header}\nB02\tB02.jpg\tShoes\t\"[Title] Men's 8\"\" Lite Work Boot\"\tBoot Style\tCombat\n"));

        Assert.Equal("[Title] Men's 8\" Lite Work Boot", example.Text);
    }

    [Fact]
    public void A_row_that_is_not_six_fields_is_refused_with_its_line()
    {
        var refused = Assert.Throws<InvalidDataException>(() => ImplicitAveSet.Parse("Home", $"{Header}\nB03\tB03.jpg\tCandles\n"));

        Assert.Contains("line 2", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>Which options file a domain's attributes are in: the authors keep three for five domains.</summary>
    [Theory]
    [InlineData("Food_annotated_final.tsv", "Food", "options_Food.json")]
    [InlineData("Home_annotated_final.tsv", "Home", "options_Home.json")]
    [InlineData("Footwear_annotated_final.tsv", "Footwear", "options_Clothing_Shoes_and_Jewelry.json")]
    [InlineData("Jewlery_and_General_Apparel_annotated_final.tsv", "Jewlery_and_General_Apparel", "options_Clothing_Shoes_and_Jewelry.json")]
    public void A_file_names_its_domain_and_its_options(string file, string domain, string options)
    {
        Assert.Equal((domain, options), (ImplicitAveSet.DomainOf(file), ImplicitAveSet.OptionsFileOf(domain)));
    }

    /// <summary>
    /// The model is asked for a value and held to the attribute's options by the
    /// schema — a closed question, as the shop asks its own — so what comes back
    /// is compared exactly, and nothing has to be found inside a sentence.
    /// </summary>
    [Fact]
    public void The_question_names_the_attribute_and_the_schema_holds_the_answer_to_its_options()
    {
        var example = new ImplicitAveExample("Home", "B01", "B01.jpg", "Candles", "[Title] A lavender candle", "Season", "All Seasons");

        var question = ImplicitAveQuestion.Ask(example, ImplicitAveModality.Both);
        var schema = ImplicitAveQuestion.Schema(["All Seasons", "Winter \"Holiday\""]);

        Assert.Contains("Season", question, StringComparison.Ordinal);
        Assert.Contains("Candles", question, StringComparison.Ordinal);
        Assert.Contains("[Title] A lavender candle", question, StringComparison.Ordinal);
        Assert.Equal(
            ["All Seasons", "Winter \"Holiday\""],
            schema.GetProperty("properties").GetProperty("value").GetProperty("enum").EnumerateArray().Select(option => option.GetString()));
    }

    /// <summary>Asked of the picture alone, the question carries none of the product's words: that run measures what the picture says.</summary>
    [Fact]
    public void Asked_of_the_picture_alone_the_question_carries_no_text_of_the_product()
    {
        var example = new ImplicitAveExample("Home", "B01", "B01.jpg", "Candles", "[Title] A lavender candle", "Season", "All Seasons");

        var question = ImplicitAveQuestion.Ask(example, ImplicitAveModality.Image);

        Assert.Contains("Season", question, StringComparison.Ordinal);
        Assert.DoesNotContain("lavender", question, StringComparison.Ordinal);
        Assert.DoesNotContain("Candles", question, StringComparison.Ordinal);
    }

    [Fact]
    public void The_score_is_the_share_answered_as_annotated_overall_and_by_domain_and_attribute()
    {
        ImplicitAveExample Example(string domain, string attribute, string value) => new(domain, "id", "id.jpg", "c", "t", attribute, value);
        ImplicitAveAnswer[] answers =
        [
            new(Example("Home", "Season", "Winter"), "Winter", 1.0),
            new(Example("Home", "Season", "Summer"), "Winter", 3.0),
            new(Example("Home", "Shape", "Round"), "round", 2.0),
            // No answer that could be read: wrong, and counted.
            new(Example("Food", "Flavor", "Vanilla"), null, 4.0),
        ];

        var score = ImplicitAveScore.Of(answers);

        Assert.Equal((4, 2, 0.5), (score.Examples, score.Right, score.Accuracy));
        Assert.Equal(1, score.Unanswered);
        Assert.Equal([("Food", 1, 0.0), ("Home", 3, 2 / 3.0)], score.ByDomain.Select(part => (part.Name, part.Examples, part.Accuracy)));
        Assert.Equal(
            [("Food · Flavor", 0.0), ("Home · Season", 0.5), ("Home · Shape", 1.0)],
            score.ByAttribute.Select(part => (part.Name, part.Accuracy)));
        Assert.Equal(3.0, score.LatencyP50Seconds);
    }

    /// <summary>Asked in plain text, the question stops at the question: the options are the closed question's to list.</summary>
    [Fact]
    public void The_plain_question_is_the_question_alone_and_the_picture_only_one_carries_no_text_of_the_product()
    {
        var example = new ImplicitAveExample("Home", "B01", "B01.jpg", "Candles", "[Title] A lavender candle", "Season", "All Seasons");

        Assert.Equal("Look at this product photograph. What is the Season of the product?", ImplicitAveQuestion.Plain(example, ImplicitAveModality.Image));
        Assert.Contains("lavender", ImplicitAveQuestion.Plain(example, ImplicitAveModality.Both), StringComparison.Ordinal);
        Assert.DoesNotContain("photograph", ImplicitAveQuestion.Plain(example, ImplicitAveModality.Text), StringComparison.Ordinal);
    }

    private static ImplicitAveAnswer Answered(string value, string? said, double? confidence) =>
        new(new ImplicitAveExample("Home", "id", "id.jpg", "c", "t", "Season", value), said, 1.0, confidence);

    /// <summary>
    /// What a threshold accepts and how much of it is right. Coverage is of every
    /// example; an answer that is no option, or that came with no confidence, is
    /// never accepted, however sure.
    /// </summary>
    [Fact]
    public void A_threshold_accepts_the_answers_at_or_above_it_and_precision_is_of_those()
    {
        ImplicitAveAnswer[] answers =
        [
            Answered("Winter", "Winter", 0.99),
            Answered("Winter", "Summer", 0.95),
            Answered("Winter", "Winter", 0.90),
            Answered("Winter", "Winter", 0.40),
            Answered("Winter", null, 0.99),
            Answered("Winter", "Winter", null),
        ];

        var at = ImplicitAveAcceptance.At(answers, [0.95, 0.90]);

        Assert.Equal([(0.95, 6, 2, 1), (0.90, 6, 3, 2)], at.Select(one => (one.Threshold, one.Examples, one.Accepted, one.Right)));
        Assert.Equal((2 / 6.0, 0.5), (at[0].Coverage, at[0].Precision));
    }

    /// <summary>The lowest confidence that still keeps the target, and none when nothing does.</summary>
    [Fact]
    public void The_threshold_for_a_target_is_the_lowest_confidence_that_still_keeps_it()
    {
        ImplicitAveAnswer[] answers =
        [
            Answered("Winter", "Winter", 0.99),
            Answered("Winter", "Winter", 0.95),
            Answered("Winter", "Winter", 0.90),
            Answered("Winter", "Summer", 0.80),
            Answered("Winter", "Winter", 0.70),
            Answered("Winter", "Summer", 0.60),
        ];

        Assert.Equal(0.70, ImplicitAveAcceptance.ThresholdFor(answers, 0.80, atLeast: 2));
        Assert.Equal(0.90, ImplicitAveAcceptance.ThresholdFor(answers, 0.95, atLeast: 2));
        // Too few answers above it to trust the share.
        Assert.Null(ImplicitAveAcceptance.ThresholdFor(answers, 0.95, atLeast: 4));
    }

    [Fact]
    public void A_threshold_is_chosen_on_every_other_answer_and_measured_on_the_rest()
    {
        ImplicitAveAnswer[] answers = [.. Enumerable.Range(0, 5).Select(index => Answered("Winter", "Winter", index / 10.0))];

        var (tune, held) = ImplicitAveAcceptance.Halves(answers);

        Assert.Equal([0.0, 0.2, 0.4], tune.Select(answer => answer.Confidence!.Value));
        Assert.Equal([0.1, 0.3], held.Select(answer => answer.Confidence!.Value));
    }
}
