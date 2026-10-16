using ElGuerre.Tendero.SearchEval.Voice;
using Xunit;

namespace ElGuerre.Tendero.SearchEval.Tests;

/// <summary>
/// How the voice suite counts (phase 14, V-5; docs/analysis/voice-recordings.md):
/// what was said and what an engine heard are both normalised — lowercased,
/// punctuation removed, numbers as figures — and the word error rate is the words
/// changed, dropped and added over the words said.
/// </summary>
public sealed class VoiceScoringTests
{
    [Theory]
    [InlineData("¿Qué puedo regalar a un corredor?", "es", "que puedo regalar a un corredor")]
    [InlineData("Flexo con USB", "es", "flexo con usb")]
    [InlineData("Cafetera de doce tazas", "es", "cafetera de 12 tazas")]
    [InlineData("Mochila por menos de setenta euros", "es", "mochila por menos de 70 euros")]
    [InlineData("Un kit por menos de doscientos euros", "es", "un kit por menos de 200 euros")]
    [InlineData("Mochila de veintidós litros", "es", "mochila de 22 litros")]
    [InlineData("Unos treinta y cinco euros", "es", "unos 35 euros")]
    [InlineData("Twelve cup coffee maker", "en", "12 cup coffee maker")]
    [InlineData("A kitchen kit under two hundred euros", "en", "a kitchen kit under 200 euros")]
    [InlineData("Backpack under seventy-five euros.", "en", "backpack under 75 euros")]
    [InlineData("Women's running shoes", "en", "womens running shoes")]
    [InlineData("Laptop backpack for 15 inches", "en", "laptop backpack for 15 inches")]
    // The sign is the word written as a symbol: whisper-small writes "200€" for "doscientos euros".
    [InlineData("por menos de 200€.", "es", "por menos de 200 euros")]
    [InlineData("Backpack under 70€", "en", "backpack under 70 euros")]
    public void What_is_said_and_what_is_heard_are_normalised_alike(string text, string culture, string normalised) =>
        Assert.Equal(normalised, Transcripts.Normalise(text, culture));

    /// <summary>
    /// A written accent is not a word heard differently: "qué" and "que" are one
    /// word to a listener, and an engine that drops the accent has heard right. The
    /// ñ stays, since "caña" and "cana" are different words.
    /// </summary>
    [Theory]
    [InlineData("¿Qué?", "que")]
    [InlineData("Tetera eléctrica", "tetera electrica")]
    [InlineData("Pingüino", "pinguino")]
    [InlineData("Una caña de pescar", "una caña de pescar")]
    public void Accents_are_folded_and_the_eñe_is_kept(string text, string normalised) =>
        Assert.Equal(normalised, Transcripts.Normalise(text, "es"));

    /// <summary>"un" and "a" are articles before a noun, not the number one: only a number word in a run of them is a figure.</summary>
    [Theory]
    [InlineData("Una sartén que no se pegue", "es", "una sarten que no se pegue")]
    [InlineData("A desk lamp", "en", "a desk lamp")]
    public void An_article_is_not_a_number(string text, string culture, string normalised) =>
        Assert.Equal(normalised, Transcripts.Normalise(text, culture));

    [Theory]
    [InlineData("cafetera de 12 tazas", "cafetera de 12 tazas", 0.0)]
    [InlineData("cafetera de 12 tazas", "cafetera 12 tazas", 0.25)]
    [InlineData("cafetera de 12 tazas", "cafetera de doce tazas extra", 0.5)]
    [InlineData("flexo con usb", "", 1.0)]
    public void The_word_error_rate_is_the_edits_over_the_words_said(string said, string heard, double rate)
    {
        var (edits, words) = Transcripts.Errors(said, heard);

        Assert.Equal(rate, (double)edits / words, 3);
    }

    [Fact]
    public void The_lines_are_read_from_the_recordings_list()
    {
        const string list = """
            ## Español

            | id | Say |
            |---|---|
            | es-01 | Zapatillas para correr por montaña |
            | es-02 | ¿Tenéis alguna oferta para hoy? |

            ## English

            | id | Say |
            |---|---|
            | en-01 | Running shoes |
            """;

        Assert.Equal(
            [("es-01", "es", "Zapatillas para correr por montaña"), ("es-02", "es", "¿Tenéis alguna oferta para hoy?"), ("en-01", "en", "Running shoes")],
            VoiceLines.Parse(list).Select(line => (line.Id, line.Culture, line.Said)));
    }
}
