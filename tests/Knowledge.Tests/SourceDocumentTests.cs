using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>A document is its content (ADR 0018): the same text is the same document, and a quote is found in it as it is written.</summary>
public sealed class SourceDocumentTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public void The_same_text_is_the_same_document_and_any_change_is_another()
    {
        var product = ProductId.New();

        var first = SourceDocument.Describe(product, "es", "Cortavientos de 140 g.", Clock);
        var again = SourceDocument.Describe(ProductId.New(), "es", "Cortavientos de 140 g.", Clock);
        var edited = SourceDocument.Describe(product, "es", "Cortavientos de 145 g.", Clock);

        Assert.Equal(first.Id, again.Id);
        Assert.NotEqual(first.Id, edited.Id);
        Assert.Equal(64, first.Id.Value.Length);
    }

    /// <summary>P10-0's one "not verbatim" quote was a capital letter: the text is found, and kept in the text's own spelling.</summary>
    [Fact]
    public void A_quote_is_found_regardless_of_case_and_kept_as_the_text_spells_it()
    {
        var document = SourceDocument.Describe(ProductId.New(), "es", "Cortavientos de 140 g que cabe en su propio bolsillo.", Clock);

        var quote = document.Locate("Cabe en su propio bolsillo");

        Assert.NotNull(quote);
        Assert.Equal("cabe en su propio bolsillo", quote.Text);
        Assert.Equal(quote.Text, document.Text[quote.Start..quote.End]);
    }

    [Fact]
    public void A_quote_the_text_does_not_contain_is_not_found()
    {
        var document = SourceDocument.Describe(ProductId.New(), "es", "Manta de lana virgen.", Clock);

        Assert.Null(document.Locate("lana merino"));
        Assert.Null(document.Locate(""));
    }
}
