using ElGuerre.Tendero.Search.Answers;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// Non-product queries are data, not a score: a closed list, matched as whole
/// words, with accents folded. A topic exists only where its destination does.
/// </summary>
public sealed class HelpTopicsTests
{
    [Theory]
    [InlineData("devoluciones", "es")]
    [InlineData("Devolución", "es")]
    [InlineData("como devolver un pedido", "es")]
    [InlineData("politica de devolucion", "es")]
    [InlineData("returns", "en")]
    [InlineData("Return policy", "en")]
    [InlineData("how do I send it back", "en")]
    public void A_question_about_returns_is_the_returns_topic(string text, string culture) =>
        Assert.Equal(HelpTopics.Returns, HelpTopics.Match(text, culture));

    [Theory]
    [InlineData("zapatillas running", "es")]
    [InlineData("devolvedor", "es")]       // a whole word, not a prefix
    [InlineData("returnsx", "en")]
    [InlineData("devoluciones", "en")]     // phrases belong to their culture
    public void Anything_else_is_no_topic(string text, string culture) =>
        Assert.Null(HelpTopics.Match(text, culture));

    [Fact]
    public void Every_topic_speaks_both_languages_and_leads_somewhere()
    {
        Assert.NotEmpty(HelpTopics.All);

        Assert.All(HelpTopics.All, topic =>
        {
            Assert.All(new[] { "es", "en" }, culture =>
                Assert.NotEmpty(topic.PhrasesByCulture[culture]));
            Assert.StartsWith("/api/", topic.Destination);
        });
    }
}
