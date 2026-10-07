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
    public void The_same_text_of_the_same_product_is_the_same_document_and_any_change_is_another()
    {
        var product = ProductId.New();

        var first = SourceDocument.Describe(product, "es", "Cortavientos de 140 g.", Clock);
        var again = SourceDocument.Describe(product, "es", "Cortavientos de 140 g.", Clock);
        var edited = SourceDocument.Describe(product, "es", "Cortavientos de 145 g.", Clock);

        Assert.Equal(first.Id, again.Id);
        Assert.NotEqual(first.Id, edited.Id);
        Assert.Equal(64, first.Id.Value.Length);
    }

    /// <summary>Two products may share a sentence; they must not share a document, or one's claims would rest on the other's text.</summary>
    [Fact]
    public void Two_products_with_the_same_text_have_two_documents()
    {
        Assert.NotEqual(
            SourceDocument.Describe(ProductId.New(), "es", "Tres pares de calcetines.", Clock).Id,
            SourceDocument.Describe(ProductId.New(), "es", "Tres pares de calcetines.", Clock).Id);
    }

    private static readonly PictureReading Read = new(new ImageId("0a1b2c"), "Qwen/Qwen3.5-4B", "picture-2026-10-07");

    /// <summary>
    /// What a model wrote about a picture is a document too (the phase 13a spec,
    /// I-4): a claim read from a picture rests on that description, and the
    /// document says which picture, which model and which prompt wrote it —
    /// because here a model's words are held to a model's words, and a person
    /// reviewing the claim has to be told so.
    /// </summary>
    [Fact]
    public void What_a_model_read_in_a_picture_is_a_document_that_says_which_picture_and_who_read_it()
    {
        var document = SourceDocument.Depict(ProductId.New(), "es", "Una sartén de hierro con mango largo.", Read, Clock);

        Assert.Equal(SourceKind.ProductImage, document.Kind);
        Assert.Equal(Read, document.Reading);
        Assert.Equal("Una sartén de hierro con mango largo.", document.Text);
        Assert.True(document.IsWaiting);
    }

    [Fact]
    public void A_products_own_description_was_read_by_nobody()
    {
        var document = SourceDocument.Describe(ProductId.New(), "es", "Sartén de hierro fundido.", Clock);

        Assert.Equal(SourceKind.ProductDescription, document.Kind);
        Assert.Null(document.Reading);
    }

    /// <summary>
    /// Two pictures of one product may be described with the same sentence, and a
    /// description may say what a picture's reading says: each is its own
    /// document, or a claim would rest on text that came from somewhere else.
    /// </summary>
    [Fact]
    public void The_same_sentence_from_two_pictures_or_from_the_description_is_three_documents()
    {
        var product = ProductId.New();
        const string Text = "Una sartén de hierro.";

        var described = SourceDocument.Describe(product, "es", Text, Clock);
        var first = SourceDocument.Depict(product, "es", Text, Read, Clock);
        var second = SourceDocument.Depict(product, "es", Text, Read with { Picture = new ImageId("ffee00") }, Clock);

        Assert.Equal(3, new[] { described.Id, first.Id, second.Id }.Distinct().Count());
        // The same picture read again to the same words is the same document.
        Assert.Equal(first.Id, SourceDocument.Depict(product, "es", Text, Read, Clock).Id);
    }

    /// <summary>
    /// The description's identity is what it was before pictures had documents:
    /// every claim already stored points at one by that hash.
    /// </summary>
    [Fact]
    public void A_descriptions_identity_did_not_move_when_pictures_got_documents()
    {
        var product = new ProductId(Guid.Parse("11111111-2222-3333-4444-555555555555"));

        Assert.Equal(
            SourceDocumentId.Of(product, "es", "Cortavientos de 140 g."),
            SourceDocument.Describe(product, "es", "Cortavientos de 140 g.", Clock).Id);
        Assert.Equal("1603bc13",SourceDocumentId.Of(product, "es", "Cortavientos de 140 g.").Value[..8]);
    }

    [Fact]
    public void A_text_that_comes_back_waits_for_claims_again()
    {
        var document = SourceDocument.Describe(ProductId.New(), "es", "Manta de lana virgen.", Clock);
        document.MarkProposed(Clock);

        document.Reinstate(Clock);

        Assert.True(document.IsWaiting);
    }

    /// <summary>The first real run: a failure's reason had gone only to the worker's log, where nobody looked.</summary>
    [Fact]
    public void A_failed_attempt_keeps_its_reason_and_the_third_stops_the_retries()
    {
        var document = SourceDocument.Describe(ProductId.New(), "es", "Zapatillas de running neutras.", Clock);

        document.RecordFailedAttempt("HttpRequestException: the model is still loading");
        document.RecordFailedAttempt(new string('x', 900));
        Assert.True(document.IsWaiting);
        document.RecordFailedAttempt("third");

        Assert.False(document.IsWaiting);
        Assert.Equal("third", document.LastFailure);
        document.Reinstate(Clock);
        Assert.Null(document.LastFailure);
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

    /// <summary>The reason is kept for an operator, and kept to what its column holds: a stack trace does not fit, its first 500 characters do.</summary>
    [Fact]
    public void A_failure_reason_is_kept_to_its_column_and_a_reinstated_text_forgets_it()
    {
        var document = SourceDocument.Describe(ProductId.New(), "es", "Cortavientos de 140 g.", Clock);

        document.RecordFailedAttempt(new string('x', 501));
        Assert.Equal((1, 500), (document.FailedAttempts, document.LastFailure!.Length));

        document.Reinstate(Clock);
        Assert.Equal((0, (string?)null, true), (document.FailedAttempts, document.LastFailure, document.IsWaiting));
    }
}
