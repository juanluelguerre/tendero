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

    /// <summary>
    /// Every number word the comparison knows is a figure, alone and with its value:
    /// a word missing from the table is a number the rate would charge as an error.
    /// The table is the documented vocabulary, written out here on purpose — the
    /// phase 14 close found every entry of it unheld by a test.
    /// </summary>
    [Theory]
    [InlineData("es", "cero", 0)]
    [InlineData("es", "dos", 2)]
    [InlineData("es", "tres", 3)]
    [InlineData("es", "cuatro", 4)]
    [InlineData("es", "cinco", 5)]
    [InlineData("es", "seis", 6)]
    [InlineData("es", "siete", 7)]
    [InlineData("es", "ocho", 8)]
    [InlineData("es", "nueve", 9)]
    [InlineData("es", "diez", 10)]
    [InlineData("es", "once", 11)]
    [InlineData("es", "doce", 12)]
    [InlineData("es", "trece", 13)]
    [InlineData("es", "catorce", 14)]
    [InlineData("es", "quince", 15)]
    [InlineData("es", "dieciséis", 16)]
    [InlineData("es", "diecisiete", 17)]
    [InlineData("es", "dieciocho", 18)]
    [InlineData("es", "diecinueve", 19)]
    [InlineData("es", "veinte", 20)]
    [InlineData("es", "veintiuno", 21)]
    [InlineData("es", "veintiún", 21)]
    [InlineData("es", "veintidós", 22)]
    [InlineData("es", "veintitrés", 23)]
    [InlineData("es", "veinticuatro", 24)]
    [InlineData("es", "veinticinco", 25)]
    [InlineData("es", "veintiséis", 26)]
    [InlineData("es", "veintisiete", 27)]
    [InlineData("es", "veintiocho", 28)]
    [InlineData("es", "veintinueve", 29)]
    [InlineData("es", "treinta", 30)]
    [InlineData("es", "cuarenta", 40)]
    [InlineData("es", "cincuenta", 50)]
    [InlineData("es", "sesenta", 60)]
    [InlineData("es", "setenta", 70)]
    [InlineData("es", "ochenta", 80)]
    [InlineData("es", "noventa", 90)]
    [InlineData("es", "ciento", 100)]
    [InlineData("es", "cien", 100)]
    [InlineData("es", "doscientos", 200)]
    [InlineData("es", "doscientas", 200)]
    [InlineData("es", "trescientos", 300)]
    [InlineData("es", "trescientas", 300)]
    [InlineData("es", "cuatrocientos", 400)]
    [InlineData("es", "cuatrocientas", 400)]
    [InlineData("es", "quinientos", 500)]
    [InlineData("es", "quinientas", 500)]
    [InlineData("es", "seiscientos", 600)]
    [InlineData("es", "seiscientas", 600)]
    [InlineData("es", "setecientos", 700)]
    [InlineData("es", "setecientas", 700)]
    [InlineData("es", "ochocientos", 800)]
    [InlineData("es", "ochocientas", 800)]
    [InlineData("es", "novecientos", 900)]
    [InlineData("es", "novecientas", 900)]
    [InlineData("es", "mil", 1000)]
    [InlineData("en", "zero", 0)]
    [InlineData("en", "one", 1)]
    [InlineData("en", "two", 2)]
    [InlineData("en", "three", 3)]
    [InlineData("en", "four", 4)]
    [InlineData("en", "five", 5)]
    [InlineData("en", "six", 6)]
    [InlineData("en", "seven", 7)]
    [InlineData("en", "eight", 8)]
    [InlineData("en", "nine", 9)]
    [InlineData("en", "ten", 10)]
    [InlineData("en", "eleven", 11)]
    [InlineData("en", "twelve", 12)]
    [InlineData("en", "thirteen", 13)]
    [InlineData("en", "fourteen", 14)]
    [InlineData("en", "fifteen", 15)]
    [InlineData("en", "sixteen", 16)]
    [InlineData("en", "seventeen", 17)]
    [InlineData("en", "eighteen", 18)]
    [InlineData("en", "nineteen", 19)]
    [InlineData("en", "twenty", 20)]
    [InlineData("en", "thirty", 30)]
    [InlineData("en", "forty", 40)]
    [InlineData("en", "fifty", 50)]
    [InlineData("en", "sixty", 60)]
    [InlineData("en", "seventy", 70)]
    [InlineData("en", "eighty", 80)]
    [InlineData("en", "ninety", 90)]
    [InlineData("en", "hundred", 100)]
    [InlineData("en", "thousand", 1000)]
    public void Every_number_word_is_its_figure(string culture, string word, int value) =>
        Assert.Equal($"son {value} euros", Transcripts.Normalise($"Son {word} euros", culture));

    /// <summary>The edges of a run of number words: where it ends, what joins it, and what only looks like one.</summary>
    [Theory]
    [InlineData("Una mochila de veintidós", "es", "una mochila de 22")]
    [InlineData("Dos mil quinientos euros", "es", "2500 euros")]
    [InlineData("Two thousand five hundred", "en", "2500")]
    [InlineData("Unos treinta y", "es", "unos 30 y")]
    [InlineData("Treinta y gatos", "es", "30 y gatos")]
    [InlineData("Y cinco más", "es", "y 5 mas")]
    [InlineData("Cable usb3 de 2 metros", "es", "cable usb3 de 2 metros")]
    [InlineData("Women’s running shoes", "en", "womens running shoes")]
    public void A_run_of_number_words_ends_where_the_numbers_do(string text, string culture, string normalised) =>
        Assert.Equal(normalised, Transcripts.Normalise(text, culture));

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
