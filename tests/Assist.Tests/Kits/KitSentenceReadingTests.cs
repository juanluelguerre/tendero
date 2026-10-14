using ElGuerre.Tendero.Assist.Kits;
using Xunit;

namespace ElGuerre.Tendero.Assist.Tests.Kits;

/// <summary>
/// What code reads in a kit's sentence without a model (phase 13c, K-6): the
/// budget, written as a price, and the requirements, written as their labels.
/// A number that is not said as money is not a budget: "para 4 personas" is
/// four people.
/// </summary>
public sealed class KitSentenceReadingTests
{
    [Theory]
    [InlineData("Quiero un kit de cocina por menos de 200 euros", "200")]
    [InlineData("A starter kitchen for induction under €59.90", "59.90")]
    [InlineData("algo para hacer café, unos 150 €", "150")]
    [InlineData("cafetera y molinillo, 59,90€ como mucho", "59.90")]
    [InlineData("un kit para 2 personas, hasta 120", "120")]
    [InlineData("a desk under 300 EUR", "300")]
    [InlineData("presupuesto de 80", "80")]
    [InlineData("budget of 1,250", "1250")]
    public void A_budget_is_a_number_said_as_money(string sentence, string amount) =>
        Assert.Equal(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), KitSentenceReading.BudgetIn(sentence));

    [Theory]
    [InlineData("un kit de cocina para 4 personas")]
    [InlineData("algo para hacer café")]
    [InlineData("menos de cero euros")]
    [InlineData("por 0 €")]
    public void A_number_not_said_as_money_or_nothing_at_all_is_no_budget(string sentence) =>
        Assert.Null(KitSentenceReading.BudgetIn(sentence));

    [Theory]
    [InlineData("apta para inducción", "Inducción", true)]
    [InlineData("apta para induccion", "Inducción", true)] // written without its accent
    [InlineData("FOR INDUCTION HOBS", "Induction", true)]
    [InlineData("sin inducción no", "Inducción", true)] // a negation is still read as asked: the shopper sees the switch and corrects it
    [InlineData("una cocina de gas", "Inducción", false)]
    [InlineData("reinduccionado", "Inducción", false)] // a word, not a piece of one
    public void A_requirement_is_asked_when_its_label_is_in_the_sentence(string sentence, string label, bool asked) =>
        Assert.Equal(asked, KitSentenceReading.Mentions(sentence, label));

    // ---------- phase 13c's close: what Stryker found nothing held ----------

    /// <summary>Every price cue, in both languages, makes the bare number after it a budget.</summary>
    [Theory]
    [InlineData("menos de"), InlineData("hasta"), InlineData("maximo"), InlineData("como maximo"), InlineData("presupuesto")]
    [InlineData("presupuesto de"), InlineData("por debajo de"), InlineData("under"), InlineData("below"), InlineData("up to")]
    [InlineData("less than"), InlineData("max"), InlineData("maximum"), InlineData("budget"), InlineData("budget of")]
    public void Each_price_cue_makes_the_number_after_it_a_budget(string cue) =>
        Assert.Equal(90m, KitSentenceReading.BudgetIn($"un kit, {cue} 90"));

    /// <summary>
    /// A number with nothing before it is no price: "2 personas" is two people. An
    /// empty cue would match any number at a sentence's start, so this is what holds
    /// each cue to being a word.
    /// </summary>
    [Fact]
    public void A_number_opening_the_sentence_with_no_cue_is_no_budget() =>
        Assert.Null(KitSentenceReading.BudgetIn("2 personas en casa"));

    [Theory]
    [InlineData("algo de €45", "45")] // the euro before it, and no cue
    [InlineData("1.250 € como mucho", "1250")] // thousands with a point
    [InlineData("hasta 1.299,50", "1299.50")] // thousands and decimals
    public void A_currency_before_the_number_or_thousands_with_a_point_read_as_money(string sentence, string amount) =>
        Assert.Equal(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), KitSentenceReading.BudgetIn(sentence));

    /// <summary>Every accented vowel reads as its plain one, whichever way it is typed.</summary>
    [Theory]
    [InlineData('á', 'a'), InlineData('à', 'a'), InlineData('â', 'a'), InlineData('ä', 'a')]
    [InlineData('é', 'e'), InlineData('è', 'e'), InlineData('ê', 'e'), InlineData('ë', 'e')]
    [InlineData('í', 'i'), InlineData('ì', 'i'), InlineData('î', 'i'), InlineData('ï', 'i')]
    [InlineData('ó', 'o'), InlineData('ò', 'o'), InlineData('ô', 'o'), InlineData('ö', 'o')]
    [InlineData('ú', 'u'), InlineData('ù', 'u'), InlineData('û', 'u'), InlineData('ü', 'u'), InlineData('ç', 'c')]
    public void An_accented_letter_is_the_plain_one(char accented, char plain) =>
        Assert.True(KitSentenceReading.Mentions($"x{plain}y", $"x{accented}y"));

    [Fact]
    public void A_blank_label_is_never_mentioned_and_nothing_is_read_from_null()
    {
        Assert.False(KitSentenceReading.Mentions("algo, otra", "  "));
        Assert.Throws<ArgumentNullException>(() => KitSentenceReading.BudgetIn(null!));
        Assert.Throws<ArgumentNullException>(() => KitSentenceReading.Mentions(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => KitSentenceReading.Mentions("a", null!));
    }
}
