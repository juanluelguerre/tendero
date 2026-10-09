using System.Text.Json;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.SearchEval.Claims;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// How a candidate's claims are judged — the rules P10-0 declared, so every
/// candidate is judged the same way, and a change to them is a red test before it
/// is a different number in the analysis.
/// </summary>
public sealed class ClaimScoringTests
{
    private static readonly ProductText Pan = new(
        "Sartenes de 20, 24 y 28 cm con revestimiento antiadherente libre de PFOA y base de acero para inducción.",
        "20, 24 and 28 cm pans with PFOA-free non-stick coating and an induction steel base.");

    private static readonly GoldenProduct Golden = new(5, "B05DEFG606",
    [
        new("de 20, 24 y 28 cm", "DIAMETERS_CM", Json("\"20, 24, 28\""), Known: true),
        new("Sartenes de 20, 24 y 28 cm", "PIECES", Json("3"), Known: true),
        new("libre de PFOA", null, Json("null"), Known: false),
        new("base de acero para inducción", "INDUCTION", Json("true"), Known: true),
    ],
    [new("CUPS", Json("6"), "not stated")]);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private static ProposedClaim Claim(string quote, string? attribute = null, string? value = null, string statementEs = "", string statementEn = "") =>
        new(quote, "", statementEs, statementEn, attribute, value, null);

    [Fact]
    public void One_quote_can_carry_two_typed_claims_and_each_is_matched_to_its_own()
    {
        var (verdicts, missed) = ClaimScoring.Judge(Golden, Pan,
        [
            Claim("20, 24 y 28 cm", "DIAMETERS_CM", "20, 24, 28"),
            Claim("Sartenes de 20, 24 y 28 cm", "PIECES", "3"),
        ]);

        Assert.All(verdicts, verdict => Assert.Equal(Outcome.Match, verdict.Outcome));
        Assert.All(verdicts, verdict => Assert.True(verdict.TypedRight));
        Assert.Equal(["libre de PFOA", "base de acero para inducción"], missed);
    }

    [Fact]
    public void A_quote_not_in_the_text_as_written_is_not_verbatim_even_by_a_capital_letter()
    {
        var (verdicts, _) = ClaimScoring.Judge(Golden, Pan, [Claim("Libre de PFOA")]);

        Assert.Equal(Outcome.NotVerbatim, Assert.Single(verdicts).Outcome);
    }

    [Fact]
    public void Proposing_a_trap_is_counted_as_one_whatever_the_quote()
    {
        var (verdicts, _) = ClaimScoring.Judge(Golden, Pan, [Claim("base de acero", "CUPS", "6")]);

        Assert.Equal(Outcome.Trap, Assert.Single(verdicts).Outcome);
    }

    [Fact]
    public void A_second_claim_on_a_matched_quote_is_a_duplicate_and_a_quote_nobody_annotated_is_unannotated()
    {
        var (verdicts, _) = ClaimScoring.Judge(Golden, Pan,
        [
            Claim("libre de PFOA"),
            Claim("antiadherente libre de PFOA"),
            Claim("revestimiento"),
        ]);

        Assert.Equal([Outcome.Match, Outcome.Duplicate, Outcome.Unannotated], verdicts.Select(verdict => verdict.Outcome));
    }

    [Fact]
    public void An_attribute_on_a_claim_that_has_none_is_spurious_and_a_wrong_value_is_typed_wrong()
    {
        var (verdicts, _) = ClaimScoring.Judge(Golden, Pan,
        [
            Claim("libre de PFOA", "MATERIAL", "WOOD"),
            Claim("base de acero para inducción", "INDUCTION", "false"),
        ]);

        Assert.True(verdicts[0].Spurious);
        Assert.False(verdicts[1].TypedRight);
    }

    [Theory]
    [InlineData("true", "true", true)]
    [InlineData("sí", "true", true)]
    [InlineData("0.450", "0.45", true)]
    [InlineData("20,24,28", "\"20, 24, 28\"", true)]
    [InlineData("cast_iron", "\"CAST_IRON\"", true)]
    [InlineData("8", "6", false)]
    [InlineData(null, "true", false)]
    public void Values_are_compared_by_kind_not_by_spelling(string? predicted, string golden, bool same)
    {
        Assert.Equal(same, ClaimScoring.SameValue(predicted, Json(golden)));
    }

    /// <summary>Invariant 6 in one line of output: "The pan has a cool handle." in the Spanish statement.</summary>
    [Theory]
    [InlineData("El asa no se calienta.", "es", true)]
    [InlineData("The pan has a cool handle.", "es", false)]
    [InlineData("The handle stays cool.", "en", true)]
    public void A_statement_in_the_wrong_language_is_caught(string statement, string language, bool right)
    {
        Assert.Equal(right, ClaimScoring.InLanguage(statement, language));
    }

    [Fact]
    public void The_summary_counts_precision_recall_and_the_new_information_apart()
    {
        var golden = new ClaimGoldenSet([], [Golden]);
        var (verdicts, missed) = ClaimScoring.Judge(Golden, Pan,
        [
            Claim("libre de PFOA"),
            Claim("base de acero para inducción", "INDUCTION", "true"),
            Claim("revestimiento"),
        ]);

        var score = ClaimScoring.Summarise(golden, [new ProductVerdicts("B05DEFG606", null, 1, verdicts, missed)]);

        Assert.Equal(2 / 3.0, score.Precision, 6);
        Assert.Equal(2 / 4.0, score.Recall, 6);
        Assert.Equal(1.0, score.RecallNew, 6);
        Assert.Equal(1 / 3.0, score.TypedAccuracy, 6);
    }
    /// <summary>
    /// The vocabulary by category (ADR 0056): a product is offered what its
    /// branch declares, and one whose branch has not said is offered everything,
    /// as before there were declarations.
    /// </summary>
    [Fact]
    public void By_category_a_product_is_offered_what_its_branch_declares_and_everything_when_it_has_not_said()
    {
        var clock = TimeProvider.System;
        var name = ElGuerre.Tendero.SharedKernel.LocalizedText.From("es", "x").With("en", "x");
        var home = ElGuerre.Tendero.Catalog.Domain.Category.Define(clock, "HOME", name, attributes: ["MATERIAL"]);
        var cookware = ElGuerre.Tendero.Catalog.Domain.Category.Define(clock, "COOKWARE", name, home, attributes: ["PIECES"]);
        var gifts = ElGuerre.Tendero.Catalog.Domain.Category.Define(clock, "GIFTS", name);
        var tree = new ElGuerre.Tendero.Catalog.Domain.CategoryTree([home, cookware, gifts]);
        ElGuerre.Tendero.Knowledge.Extraction.AttributeSpec[] all =
        [
            new("MATERIAL", "Option", null, "material", []),
            new("PIECES", "Number", null, "pieces", []),
            new("CUPS", "Number", null, "cups", []),
        ];

        string[] Offered(string? category) =>
            [.. ElGuerre.Tendero.SearchEval.Suites.ClaimsSuite.Offered(all, tree, category).Select(attribute => attribute.Code)];

        Assert.Equal(["MATERIAL", "PIECES"], Offered("COOKWARE"));
        Assert.Equal(["MATERIAL"], Offered("HOME"));
        Assert.Equal(["MATERIAL", "PIECES", "CUPS"], Offered("GIFTS"));
        Assert.Equal(["MATERIAL", "PIECES", "CUPS"], Offered(null));
    }
}
