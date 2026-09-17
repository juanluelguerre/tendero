using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Ucp.Catalog;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

public sealed class UcpSearchNoticesTests
{
    [Fact]
    public void An_exact_answer_says_nothing() =>
        Assert.Empty(UcpSearchNotices.For(new SearchAnswer(SearchMatch.Exact), "cafetera", "es"));

    [Fact]
    public void A_related_answer_is_an_info_message_in_the_answered_language()
    {
        var message = Assert.Single(UcpSearchNotices.For(new SearchAnswer(SearchMatch.Related), "regalo abuela", "es"));

        Assert.Equal(("info", "no_exact_match", "$.query"), (message.Type, message.Code, message.Path));
        Assert.Contains("regalo abuela", message.Content);

        // Hardcoding the English text would still contain the query, so the
        // language itself has to be asserted — a Spanish word the English copy
        // does not carry.
        Assert.Contains("parecidos", message.Content);
    }

    /// <summary>
    /// The English half, asserted by an English word the Spanish copy does not
    /// carry: at the 2026-09-17 phase close every answer could have been written
    /// in Spanish with nothing red, because each test that asked in English
    /// checked only the words both copies share.
    /// </summary>
    [Fact]
    public void A_related_answer_asked_in_english_is_answered_in_english()
    {
        var message = Assert.Single(UcpSearchNotices.For(new SearchAnswer(SearchMatch.Related), "gift for grandma", "en"));

        Assert.Contains("closest", message.Content);
    }

    [Fact]
    public void A_help_topic_adds_its_own_message()
    {
        var messages = UcpSearchNotices.For(new SearchAnswer(SearchMatch.Related, HelpTopic: "returns"), "returns", "en");

        var help = Assert.Single(messages, message => message.Code == "help_available");
        Assert.Equal(("info", "$.query"), (help.Type, help.Path));
        Assert.Contains("/api/returns/policy", help.Content);
    }

    [Fact]
    public void No_answer_says_nothing() => Assert.Empty(UcpSearchNotices.For(null, "", "es"));
}
