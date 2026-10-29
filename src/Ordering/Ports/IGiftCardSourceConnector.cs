using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Ports;

/// <summary>
/// The gift cards' inbound port (phase 16, S2b, P16-20; spec decision 6). Tendero
/// has no gift cards, and a store hands over a card's full code only on the day it
/// creates it, so a card cannot come across: it is listed, with what the merchant
/// needs to reissue it. A source hands over the cards still worth something —
/// enabled, with a balance, not expired — and nothing else. Keyed by name, as the
/// orders' port is; a source with orders and no gift cards registers none.
/// </summary>
public interface IGiftCardSourceConnector
{
    /// <summary>A stable lowercase identifier: "shopify"…</summary>
    string Source { get; }

    IAsyncEnumerable<SourceGiftCard> StreamGiftCardsAsync(CancellationToken cancellationToken = default);
}

/// <param name="ExternalId">The source's id for the card.</param>
/// <param name="LastCharacters">The end of its code, all a store tells after the day it made it: what the holder recognises their card by.</param>
/// <param name="CustomerExternalId">The source's id for whoever it was sold or issued to, when it says.</param>
public sealed record SourceGiftCard(string ExternalId, string LastCharacters, Money Balance, DateOnly? ExpiresOn, string? CustomerExternalId);

/// <summary>The gift card sources registered, by name; the only place that talks to the container.</summary>
public interface IGiftCardSourceRegistry
{
    /// <summary>The source's gift cards, or null when it registers none.</summary>
    IGiftCardSourceConnector? Find(string source);
}
