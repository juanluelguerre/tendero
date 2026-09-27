using ElGuerre.Tendero.Knowledge.Extraction;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// The non-AI path (invariant 8) on the seed's own sentences: what it finds, the
/// value it types, and the unit it converts to. Exact about the little it sees —
/// P10-0 measured it at precision 1.00 — so each case here is one it must keep.
/// </summary>
public sealed class RulesClaimExtractorTests
{
    private static async Task<IReadOnlyList<ProposedClaim>> Extract(string spanish) =>
        await new RulesClaimExtractor().ExtractAsync(new ProductText(spanish, ""), [], TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("Refuerzo en el talón. Drop de 8 mm.", "Drop de 8 mm", "DROP_MM", "8")]
    [InlineData("Hervidor de 1,7 litros con cinco temperaturas.", "1,7 litros", "CAPACITY_L", "1.7")]
    [InlineData("Cuatro tazones de gres de 450 ml con base sin esmaltar.", "450 ml", "CAPACITY_L", "0.45")]
    [InlineData("Lámpara con base de carga inalámbrica de 10 W.", "10 W", "POWER_W", "10")]
    [InlineData("Versión de 6 tazas de la Aroma.", "6 tazas", "CUPS", "6")]
    [InlineData("Sartenes de 20, 24 y 28 cm con revestimiento.", "de 20, 24 y 28 cm", "DIAMETERS_CM", "20, 24, 28")]
    [InlineData("Sartén de hierro fundido precurada.", "hierro fundido", "MATERIAL", "CAST_IRON")]
    public async Task A_number_with_its_unit_is_typed_in_the_attributes_unit(string spanish, string quote, string attribute, string value)
    {
        var claims = await Extract(spanish);

        Assert.Contains(claims, claim => claim.QuoteSpanish == quote && claim.AttributeCode == attribute && claim.Value == value);
    }

    [Fact]
    public async Task Induction_is_a_boolean_claim_quoting_what_leads_to_the_word()
    {
        var claims = await Extract("Wok de acero al carbono con fondo plano para inducción y mango de madera.");

        var induction = Assert.Single(claims, claim => claim.AttributeCode == "INDUCTION");
        Assert.Equal("true", induction.Value);
        Assert.EndsWith("inducción", induction.QuoteSpanish, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_weight_or_a_size_is_found_and_left_untyped_because_no_attribute_holds_it()
    {
        var claims = await Extract("Cortavientos de 140 g. Fuente de 33 x 22 cm.");

        Assert.Contains(claims, claim => claim.QuoteSpanish == "140 g" && claim.AttributeCode is null);
        Assert.Contains(claims, claim => claim.QuoteSpanish == "33 x 22 cm" && claim.AttributeCode is null);
    }

    [Fact]
    public async Task A_count_opening_the_description_is_the_number_of_pieces()
    {
        var claims = await Extract("Tres pares de calcetines de merino.");

        Assert.Contains(claims, claim => claim.AttributeCode == "PIECES" && claim.Value == "3");
    }

    /// <summary>What the rules cannot see is the reason a model is measured at all.</summary>
    [Fact]
    public async Task A_property_in_words_is_invisible_to_the_rules()
    {
        Assert.Empty(await Extract("Sandalia con suela de agarre. Se seca en una hora."));
    }

    /// <summary>Every quote the rules propose is in the text as written: they cannot invent one.</summary>
    [Fact]
    public async Task Every_quote_is_verbatim()
    {
        const string text = "Olla de acero inoxidable de 6 litros, apta para inducción, 1000 W. Pesa 1,4 kg.";

        Assert.All(await Extract(text), claim => Assert.Contains(claim.QuoteSpanish, text, StringComparison.Ordinal));
    }

    /// <summary>
    /// The whole vocabulary the rules know, word by word: a table in a static
    /// field, whose entries Stryker cannot see killed (a runner artefact,
    /// docs/testing.md) — so this is where losing one of them turns red.
    /// </summary>
    [Theory]
    [InlineData("hierro fundido", "CAST_IRON")]
    [InlineData("acero al carbono", "CARBON_STEEL")]
    [InlineData("acero inoxidable", "STAINLESS_STEEL")]
    [InlineData("aluminio", "ALUMINIUM")]
    [InlineData("lino", "LINEN")]
    [InlineData("cerámica", "CERAMIC")]
    [InlineData("bambú", "BAMBOO")]
    [InlineData("algodón", "COTTON")]
    [InlineData("merino", "MERINO_WOOL")]
    [InlineData("cristal", "GLASS")]
    public async Task Every_material_the_rules_know_is_typed_as_its_option(string word, string option)
    {
        var claims = await Extract($"Una pieza de {word} hecha a mano.");

        Assert.Contains(claims, claim => claim.QuoteSpanish == word && claim.AttributeCode == "MATERIAL" && claim.Value == option);
    }

    [Theory]
    [InlineData("Dos", "2")]
    [InlineData("Tres", "3")]
    [InlineData("Cuatro", "4")]
    [InlineData("Cinco", "5")]
    [InlineData("Seis", "6")]
    public async Task A_count_that_opens_the_description_is_the_number_of_pieces(string word, string pieces)
    {
        var claims = await Extract($"{word} cuencos apilables.");

        Assert.Contains(claims, claim => claim.AttributeCode == "PIECES" && claim.Value == pieces);
    }

    /// <summary>
    /// The rules quote Spanish and write no statement in either language: vetting
    /// states a typed claim from its attribute and an untyped one from its own
    /// words. A rule that wrote a statement would be judged as a model's.
    /// </summary>
    [Fact]
    public async Task The_rules_write_no_statement_and_no_english_quote()
    {
        var claims = await Extract("Cazo de 20 cm con asa de 30 x 12 cm y 1,2 kg de aluminio.");

        Assert.NotEmpty(claims);
        Assert.All(claims, claim => Assert.Equal(("", "", ""), (claim.QuoteEnglish, claim.StatementSpanish, claim.StatementEnglish)));
    }
}
