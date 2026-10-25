using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Shopify;

/// <summary>
/// The gift cards port's Shopify adapter (phase 16, S2b, P16-20; decision 6). It
/// reads the cards the store still owes, a page at a time, each by the end of its
/// code with its balance, its expiry and the customer it was sold or issued to.
/// The store's search already leaves the spent ones out; a card that comes anyway
/// disabled or empty is not handed over, since there is nothing to reissue. The
/// expired ones are asked for first and left out, because the store's search
/// cannot exclude them without losing the cards that never expire (P16-16).
///
/// Registered with the other Shopify adapters when the store's credentials are
/// configured.
/// </summary>
internal sealed class ShopifyGiftCardConnector(ShopifyAdmin admin) : IGiftCardSourceConnector
{
    public const string Key = "shopify";

    public string Source => Key;

    public async IAsyncEnumerable<SourceGiftCard> StreamGiftCardsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var expired = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var node in PagesAsync(ShopifyQueries.ExpiredGiftCards, cancellationToken))
            expired.Add(ShopifyBulkProducts.IdOf(node.GetProperty("id").GetString()));

        await foreach (var node in PagesAsync(ShopifyQueries.GiftCards, cancellationToken))
        {
            if (Card(node) is { } card && !expired.Contains(card.ExternalId))
                yield return card;
        }
    }

    /// <summary>Every card a query answers, a page at a time, the next asked after the cursor.</summary>
    private async IAsyncEnumerable<JsonElement> PagesAsync(Func<string?, string> query, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? after = null;
        do
        {
            var page = (await admin.QueryAsync(query(after), cancellationToken)).GetProperty("giftCards");

            foreach (var node in page.GetProperty("nodes").EnumerateArray())
                yield return node;

            var info = page.GetProperty("pageInfo");
            after = info.GetProperty("hasNextPage").GetBoolean() ? info.GetProperty("endCursor").GetString() : null;
        }
        while (after is not null);
    }

    private static SourceGiftCard? Card(JsonElement node)
    {
        var balance = node.GetProperty("balance");
        var amount = decimal.Parse(balance.GetProperty("amount").GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture);
        if (!node.GetProperty("enabled").GetBoolean() || amount <= 0)
            return null;

        return new SourceGiftCard(
            ShopifyBulkProducts.IdOf(node.GetProperty("id").GetString()),
            node.GetProperty("lastCharacters").GetString()!,
            new Money(amount, balance.GetProperty("currencyCode").GetString()!),
            node.TryGetProperty("expiresOn", out var expires) && expires.ValueKind == JsonValueKind.String
                ? DateOnly.Parse(expires.GetString()!, CultureInfo.InvariantCulture)
                : null,
            node.TryGetProperty("customer", out var customer) && customer.ValueKind == JsonValueKind.Object
                ? ShopifyBulkProducts.IdOf(customer.GetProperty("id").GetString())
                : null);
    }
}
