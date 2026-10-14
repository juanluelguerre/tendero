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
}
