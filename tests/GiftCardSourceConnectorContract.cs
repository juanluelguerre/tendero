using ElGuerre.Tendero.Ordering.Ports;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests.Sources;

/// <summary>
/// The gift cards port's executable contract (phase 16, S2b, P16-20): every gift
/// card source inherits this class and passes the same suite. Linked as a file
/// into each test project that has one (Ordering.Tests for its fake, Shopify.Tests
/// for Shopify), like the orders' suite.
/// </summary>
public abstract class GiftCardSourceConnectorContractTests
{
    protected abstract IGiftCardSourceConnector CreateConnector();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<List<SourceGiftCard>> All()
    {
        var cards = new List<SourceGiftCard>();
        await foreach (var card in CreateConnector().StreamGiftCardsAsync(Ct))
            cards.Add(card);
        return cards;
    }

    [Fact]
    public void Source_is_a_stable_lowercase_key()
    {
        var connector = CreateConnector();

        Assert.False(string.IsNullOrWhiteSpace(connector.Source));
        Assert.Equal(connector.Source, connector.Source.ToLowerInvariant());
        Assert.DoesNotContain(' ', connector.Source);
    }

    [Fact]
    public async Task Cards_have_an_id_and_no_id_twice()
    {
        var cards = await All();

        Assert.NotEmpty(cards);
        Assert.All(cards, card => Assert.False(string.IsNullOrWhiteSpace(card.ExternalId)));
        Assert.Equal(cards.Count, cards.Select(card => card.ExternalId).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A card handed over is one still owed: there is nothing to reissue for a spent one.</summary>
    [Fact]
    public async Task Every_card_is_still_worth_something() =>
        Assert.All(await All(), card => Assert.True(card.Balance.Amount > 0, card.ExternalId));

    /// <summary>
    /// The end of a code and never the code: a listing a merchant reads, and an
    /// agent may, must not be something to pay with.
    /// </summary>
    [Fact]
    public async Task A_card_is_named_by_the_end_of_its_code_and_never_by_its_code() =>
        Assert.All(await All(), card => Assert.InRange(card.LastCharacters.Trim().Length, 1, 4));
}
