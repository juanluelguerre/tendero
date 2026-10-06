using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>
/// Which of the shop's two languages a text is in, by its function words: what
/// Knowledge holds a claim's statement to, and what Steward holds its own answer to.
/// </summary>
public sealed class WrittenLanguageTests
{
    [Theory]
    [InlineData("Las ventas de la semana fueron de 120 € en 3 pedidos.", "es")]
    [InlineData("The week's sales were 120 € in 3 orders and there are no returns.", "en")]
    // A product's name is the catalogue's language; the words around it say the answer's.
    [InlineData("Propuse el nombre en inglés de la Pulse Runner cushioned road running shoes.", "es")]
    [InlineData("I proposed the English name of the Zapatillas de running amortiguadas, and it waits for you.", "en")]
    public void A_text_is_in_the_language_most_of_its_function_words_are(string text, string language)
    {
        Assert.Equal(language, WrittenLanguage.Of(text));
        Assert.True(WrittenLanguage.Is(text, language));
        Assert.False(WrittenLanguage.Is(text, language == "es" ? "en" : "es"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("HAPR45HY8N · 79.95 EUR")]
    // One word of each: nothing is plain.
    [InlineData("la casa of cards")]
    public void A_text_whose_words_do_not_say_is_in_no_language_and_passes_for_either(string? text)
    {
        Assert.Null(WrittenLanguage.Of(text));
        Assert.True(WrittenLanguage.Is(text, "es"));
        Assert.True(WrittenLanguage.Is(text, "en"));
    }
}
