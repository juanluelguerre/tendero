using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// What code checks before a claim reaches a person (ADR 0018, 4) — each case a
/// failure P10-0 measured in a real model's output.
/// </summary>
public sealed class ClaimVettingTests
{
    private static readonly TestClock Clock = new();
    private static readonly ProductId Windbreaker = ProductId.New();

    private static readonly SourceDocument Spanish = SourceDocument.Describe(
        Windbreaker, "es", "Cortavientos de 140 g que cabe en su propio bolsillo. Tazón de 450 ml.", Clock);

    private static readonly SourceDocument English = SourceDocument.Describe(
        Windbreaker, "en", "A 140 g windbreaker that folds into its own pocket. A 450 ml bowl.", Clock);

    private static readonly AttributeSpec[] Vocabulary =
    [
        new("CAPACITY_L", "Number", "L", "capacity", [], "capacidad"),
        new("MATERIAL", "Option", null, "material", ["CAST_IRON", "LINEN"], "material"),
    ];

    private static ProposedClaim Claim(string quote, string es = "", string en = "", string? attribute = null, string? value = null, string quoteEn = "") =>
        new(quote, quoteEn, es, en, attribute, value, null);

    private static (IReadOnlyList<VettedClaim> Kept, IReadOnlyList<DropReason> Dropped) Vet(params ProposedClaim[] claims) =>
        ClaimVetting.Vet(claims, Spanish, English, Vocabulary);

    [Fact]
    public void A_quote_found_regardless_of_case_is_kept_in_the_texts_own_spelling_and_matched_in_english()
    {
        var (kept, _) = Vet(Claim("Cabe en su propio bolsillo", "Cabe en su propio bolsillo.", "It folds into its own pocket.", quoteEn: "folds into its own pocket"));

        var claim = Assert.Single(kept);
        Assert.Equal("cabe en su propio bolsillo", claim.QuoteSpanish.Text);
        Assert.Equal("folds into its own pocket", claim.QuoteEnglish!.Text);
    }

    [Fact]
    public void A_quote_the_description_does_not_contain_never_reaches_a_person()
    {
        var (kept, dropped) = Vet(Claim("es impermeable", "Es impermeable.", "It is waterproof."));

        Assert.Empty(kept);
        Assert.Equal([DropReason.NotInTheText], dropped);
    }

    /// <summary>"The pan has a cool handle." in the Spanish statement: one in six at 4 bits.</summary>
    [Fact]
    public void A_statement_in_the_wrong_language_is_refused()
    {
        var (kept, dropped) = Vet(Claim("cabe en su propio bolsillo", "It folds into its own pocket.", "It folds into its own pocket."));

        Assert.Empty(kept);
        Assert.Equal([DropReason.WrongLanguage], dropped);
    }

    /// <summary><c>MATERIAL = WOOD</c>: the claim stays, its typing does not.</summary>
    [Fact]
    public void A_value_outside_its_attribute_is_refused_and_the_claim_kept_untyped()
    {
        var (kept, _) = Vet(Claim("cabe en su propio bolsillo", "Cabe en su propio bolsillo.", "It folds into its own pocket.", "MATERIAL", "WOOD"));

        var claim = Assert.Single(kept);
        Assert.Null(claim.AttributeCode);
        Assert.True(claim.TypingRefused);
    }

    [Fact]
    public void A_number_is_normalised_whatever_the_decimal_mark()
    {
        var (kept, _) = Vet(Claim("450 ml", "Tiene una capacidad de 0,45 litros.", "It holds 0.45 litres.", "CAPACITY_L", "0,45"));

        Assert.Equal(("CAPACITY_L", "0.45"), (kept[0].AttributeCode, kept[0].Value));
    }

    [Fact]
    public void Two_claims_on_the_same_words_and_attribute_are_one()
    {
        var (kept, dropped) = Vet(
            Claim("140 g", "Pesa 140 g.", "It weighs 140 g."),
            Claim("140 g", "Es ligero: 140 g.", "It is light: 140 g."));

        Assert.Single(kept);
        Assert.Equal([DropReason.Duplicate], dropped);
    }

    /// <summary>The rules write no statement: a typed one is said from the attribute's labels, an untyped measure reads the same in both languages.</summary>
    [Fact]
    public void A_rules_claim_is_stated_from_its_attribute_or_from_its_own_words()
    {
        var (kept, _) = Vet(Claim("450 ml", attribute: "CAPACITY_L", value: "0.45"), Claim("140 g"));

        Assert.Equal("capacidad: 0.45 L", kept[0].Statement.In("es"));
        Assert.Equal("capacity: 0.45 L", kept[0].Statement.In("en"));
        Assert.Equal("140 g", kept[1].Statement.In("en"));
    }
}
