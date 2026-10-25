using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.Ordering.Tests.Sources;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Shopify.Tests;

/// <summary>The Shopify adapter of the gift cards port passes the contract every gift card source does.</summary>
public sealed class ShopifyGiftCardConnectorContractTests : GiftCardSourceConnectorContractTests
{
    protected override IGiftCardSourceConnector CreateConnector() => ShopifyGiftCardConnectorTests.AConnector(new FakeShopify());
}

/// <summary>
/// A store's gift cards, to be listed (phase 16, S2b, P16-20; decision 6): the
/// cards still owed, each by the end of its code — all the store tells after the
/// day it made it — with its balance, its expiry and whom it was issued to.
/// </summary>
public sealed class ShopifyGiftCardConnectorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static ShopifyGiftCardConnector AConnector(FakeShopify store)
    {
        var clock = new TestClock();
        var options = Options.Create(new ShopifyOptions
        {
            Store = FakeShopify.Store,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            BulkPollInterval = TimeSpan.Zero,
            ThrottleBackoff = TimeSpan.Zero,
        });
        var http = new HttpClient(store);
        return new ShopifyGiftCardConnector(new ShopifyAdmin(http, new ShopifyAccessTokens(http, options, clock), options, clock));
    }

    private static async Task<List<SourceGiftCard>> Cards(FakeShopify store)
    {
        var cards = new List<SourceGiftCard>();
        await foreach (var card in AConnector(store).StreamGiftCardsAsync(Ct))
            cards.Add(card);
        return cards;
    }

    [Fact]
    public async Task Each_card_still_owed_comes_with_the_end_of_its_code_its_balance_its_expiry_and_its_holder()
    {
        var cards = await Cards(new FakeShopify());

        Assert.Equal(
            [
                new SourceGiftCard("8001", "ab12", new Money(25m, "EUR"), new DateOnly(2027, 12, 31), "6001"),
                new SourceGiftCard("8002", "cd34", new Money(50m, "EUR"), null, null),
                new SourceGiftCard("8005", "ij90", new Money(15m, "EUR"), null, "6003"),
            ],
            cards);
    }

    /// <summary>
    /// Every page is read, the second after the first's cursor; the first is asked
    /// with none. Found by phase 16 S2b's Stryker close, when the fixture was one page.
    /// </summary>
    [Fact]
    public async Task Every_page_is_read_the_next_after_the_cursor()
    {
        var store = new FakeShopify();

        await Cards(store);

        var pages = store.Queries.Where(query => query.Contains("{ giftCards(", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, pages.Count);
        Assert.Contains("balance_status:full_or_partial\") {", pages[0], StringComparison.Ordinal);
        Assert.Contains("after: \"g3\")", pages[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// The store is asked for the cards still worth something, and a spent or
    /// disabled one that comes anyway is not handed over: there is nothing to reissue.
    /// </summary>
    [Fact]
    public async Task Only_the_cards_still_owed_are_asked_for_and_handed_over()
    {
        var store = new FakeShopify();

        var cards = await Cards(store);

        Assert.DoesNotContain(cards, card => card.ExternalId is "8003" or "8004");
        Assert.Contains(store.Queries, query => query.Contains("{ giftCards(", StringComparison.Ordinal)
            && query.Contains("status:enabled", StringComparison.Ordinal)
            && query.Contains("balance_status:full_or_partial", StringComparison.Ordinal));
    }
}
