using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

public sealed class SentencesTests
{
    [Fact]
    public void A_description_is_its_sentences_each_whole()
    {
        var sentences = Sentences.Of(
            "Sartén de hierro fundido precurada, apta para inducción, horno y fuego directo. Gana antiadherencia con el uso.", 12);

        Assert.Equal(
            ["Sartén de hierro fundido precurada, apta para inducción, horno y fuego directo.", "Gana antiadherencia con el uso."],
            sentences);
    }

    [Theory]
    [InlineData("Pesa 2.3 kg. Mide 26 cm.", "Pesa 2.3 kg.|Mide 26 cm.")]
    [InlineData("Pesa aprox. 2 kg y no más.", "Pesa aprox. 2 kg y no más.")]
    [InlineData("Is it heavy? No. It is light!", "Is it heavy?|No.|It is light!")]
    [InlineData("Apta para horno. ¿Y lavavajillas? También.", "Apta para horno.|¿Y lavavajillas?|También.")]
    [InlineData("No final stop", "No final stop")]
    [InlineData("  Spaces around.   And between.  ", "Spaces around.|And between.")]
    public void A_sentence_ends_at_a_stop_before_a_capital_and_nowhere_else(string text, string expected) =>
        Assert.Equal(expected.Split('|'), Sentences.Of(text, 12));

    [Fact]
    public void No_more_than_asked_and_the_first_ones()
    {
        Assert.Equal(["One.", "Two."], Sentences.Of("One. Two. Three. Four.", 2));
        Assert.Empty(Sentences.Of("One. Two.", 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_has_no_sentences(string? text) => Assert.Empty(Sentences.Of(text, 12));
}
