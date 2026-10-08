using ElGuerre.Tendero.Knowledge.Extraction;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// What a picture answered, written under its reading and read back (the phase
/// 13a spec, I-4): the model is asked a closed question and answers a number;
/// code writes that number as a line of the reading, in each language, and code
/// reads the line back as a typed claim. No model reads the reading's prose.
/// </summary>
public sealed class PictureAnswersTests
{
    private static readonly AttributeSpec Pieces = new("PIECES", "Number", null, "pieces", [], "piezas");
    private static readonly AttributeSpec Material = new("MATERIAL", "Option", null, "material", ["STEEL"], "material");

    private static async Task<IReadOnlyList<ProposedClaim>> Read(string spanish, string english, params AttributeSpec[] offered) =>
        await new PictureAnswersExtractor().ExtractAsync(new ProductText(spanish, english), offered, TestContext.Current.CancellationToken);

    [Fact]
    public void The_count_is_a_line_of_its_own_under_the_prose_in_the_readings_language()
    {
        Assert.Equal("Tres sartenes negras.\nElementos contados en la imagen: 3.", PictureAnswers.Under("Tres sartenes negras. ", "es", 3));
        Assert.Equal("Three black pans.\nItems counted in the picture: 3.", PictureAnswers.Under("Three black pans.", "en", 3));
    }

    /// <summary>The claim quotes the line in each language and types the number: the line is what a reviewer reads beside the picture.</summary>
    [Fact]
    public async Task The_line_is_read_back_as_a_typed_claim_that_quotes_it_in_both_languages()
    {
        var claim = Assert.Single(await Read(
            PictureAnswers.Under("Tres sartenes negras.", "es", 3), PictureAnswers.Under("Three black pans.", "en", 3), Pieces));

        Assert.Equal(
            ("Elementos contados en la imagen: 3.", "Items counted in the picture: 3.", "PIECES", "3"),
            (claim.QuoteSpanish, claim.QuoteEnglish, claim.AttributeCode, claim.Value));
        // No sentence of its own: the vetting states it from the attribute's labels, as it does a rule's.
        Assert.Equal(("", ""), (claim.StatementSpanish, claim.StatementEnglish));
    }

    /// <summary>The prose is never read: "tres" and "three" in it type nothing.</summary>
    [Fact]
    public async Task A_reading_with_no_answer_under_it_proposes_nothing_whatever_its_prose_says()
    {
        Assert.Empty(await Read("Tres sartenes negras apiladas.", "Three stacked black pans.", Pieces));
    }

    /// <summary>The answer is the last such line: a model's prose that happens to say the same words is not it.</summary>
    [Fact]
    public async Task The_answer_is_the_line_code_wrote_last()
    {
        var prose = "Una etiqueta dice:\nElementos contados en la imagen: 9.";

        var claim = Assert.Single(await Read(PictureAnswers.Under(prose, "es", 2), PictureAnswers.Under("A label.", "en", 2), Pieces));

        Assert.Equal(("Elementos contados en la imagen: 2.", "2"), (claim.QuoteSpanish, claim.Value));
    }

    /// <summary>Nothing counted is nothing seen, not a product of no pieces.</summary>
    [Fact]
    public async Task A_count_of_none_proposes_nothing()
    {
        Assert.Empty(await Read(PictureAnswers.Under("Un fondo claro.", "es", 0), PictureAnswers.Under("A light background.", "en", 0), Pieces));
    }

    /// <summary>Typed only with what it was offered: a shop whose pictures are not offered the count gets no claim from it.</summary>
    [Fact]
    public async Task A_count_types_nothing_when_the_pieces_are_not_offered()
    {
        Assert.Empty(await Read(PictureAnswers.Under("Tres sartenes.", "es", 3), PictureAnswers.Under("Three pans.", "en", 3), Material));
    }

    /// <summary>A reading kept in Spanish only still answers: the English quote is simply not there.</summary>
    [Fact]
    public async Task With_no_english_reading_the_claim_quotes_the_spanish_line_alone()
    {
        var claim = Assert.Single(await Read(PictureAnswers.Under("Tres sartenes.", "es", 3), "", Pieces));

        Assert.Equal("", claim.QuoteEnglish);
    }
    /// <summary>
    /// How sure the model was is written beside the number, as a whole percentage
    /// **rounded down** — 0.949 is 94 %, never 95 — and read back with it, so the
    /// claim carries it and no answer reads surer than it was.
    /// </summary>
    [Fact]
    public async Task How_sure_the_model_was_is_written_beside_the_count_rounded_down_and_read_back()
    {
        var spanish = PictureAnswers.Under("Tres sartenes.", "es", 3, 0.949);
        var english = PictureAnswers.Under("Three pans.", "en", 3, 0.949);

        Assert.Equal("Tres sartenes.\nElementos contados en la imagen: 3 (confianza 94 %).", spanish);
        Assert.Equal("Three pans.\nItems counted in the picture: 3 (confidence 94 %).", english);

        var claim = Assert.Single(await Read(spanish, english, Pieces));
        Assert.Equal(
            ("Elementos contados en la imagen: 3 (confianza 94 %).", "Items counted in the picture: 3 (confidence 94 %).", "3", 0.94),
            (claim.QuoteSpanish, claim.QuoteEnglish, claim.Value, claim.Confidence));
    }

    /// <summary>An answer whose server gave no probability says no confidence: not one of 100 %.</summary>
    [Fact]
    public async Task A_count_with_no_probability_carries_no_confidence()
    {
        var claim = Assert.Single(await Read(PictureAnswers.Under("Tres sartenes.", "es", 3), PictureAnswers.Under("Three pans.", "en", 3), Pieces));

        Assert.Null(claim.Confidence);
    }

    /// <summary>A line that is not the one code writes is no answer: nothing is guessed from half of it.</summary>
    [Theory]
    [InlineData("Tres sartenes.\nElementos contados en la imagen: 3 (confianza alta).")]
    [InlineData("Tres sartenes.\nElementos contados en la imagen: tres.")]
    [InlineData("Tres sartenes.\nElementos contados en la imagen: 3 (confianza 94 %)")]
    [InlineData("Doce sartenes.\nElementos contados en la imagen: 12")]
    public async Task A_line_code_did_not_write_is_no_answer(string spanish)
    {
        Assert.Empty(await Read(spanish, "", Pieces));
    }
    /// <summary>
    /// The colour a picture answered is written under the reading too, in its
    /// language — for a person, and for the document's identity. The count is
    /// still read from its own line above it, and nothing is read from the
    /// colour's: a colour is never a claim.
    /// </summary>
    [Fact]
    public async Task The_colour_seen_is_a_line_under_the_count_and_the_count_is_still_read()
    {
        var spanish = PictureAnswers.WithColour(PictureAnswers.Under("Tres sartenes.", "es", 3, 0.97), "es", "gris", 0.949);
        var english = PictureAnswers.WithColour(PictureAnswers.Under("Three pans.", "en", 3, 0.97), "en", "grey", 0.949);

        Assert.Equal("Tres sartenes.\nElementos contados en la imagen: 3 (confianza 97 %).\nColor visto en la imagen: gris (confianza 94 %).", spanish);
        Assert.Equal("Three pans.\nItems counted in the picture: 3 (confidence 97 %).\nColour seen in the picture: grey (confidence 94 %).", english);

        var claim = Assert.Single(await Read(spanish, english, Pieces));
        Assert.Equal(("PIECES", "3", "Items counted in the picture: 3 (confidence 97 %)."), (claim.AttributeCode, claim.Value, claim.QuoteEnglish));
    }

    [Fact]
    public async Task A_reading_with_a_colour_and_no_count_proposes_nothing()
    {
        var spanish = PictureAnswers.WithColour("Una zapatilla.", "es", "gris", null);

        Assert.Equal("Una zapatilla.\nColor visto en la imagen: gris.", spanish);
        Assert.Empty(await Read(spanish, PictureAnswers.WithColour("A shoe.", "en", "grey", null), Pieces));
    }
}
