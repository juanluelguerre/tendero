using System.Text.Json;
using ElGuerre.Tendero.Assist.Turns;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Assist.Tino;

public enum TinoStatus
{
    Answered,

    /// <summary>Tino did not answer: the storefront shows the search results for <see cref="TinoAnswer.SearchText"/>.</summary>
    Unavailable,
}

/// <summary>Why Tino did not answer — what the fallback's event says, and what a test names.</summary>
public enum TinoCause
{
    /// <summary>No profile is configured, or Tino is switched off.</summary>
    NoProfile,

    /// <summary>The model failed, or what it wrote was not an answer.</summary>
    Model,

    /// <summary>The turn outlived its seconds.</summary>
    Time,

    /// <summary>The shop's day of tokens, or the conversation's, is spent.</summary>
    Budget,

    /// <summary>The shop's own code failed.</summary>
    Shop,

    /// <summary>The conversation no longer fits any profile's window: a new one starts again.</summary>
    TooLong,

    /// <summary>The support scorer is not there, or did not answer: nothing was checked, so nothing is shown.</summary>
    Scorer,
}

/// <summary>The shapes an answer has (the Tino spec, *An answer*; <c>Order</c> arrives in T-4).</summary>
public enum TinoKind
{
    Products,
    Shop,
    Ask,
    NoData,

    /// <summary>About an order of the asking shopper's.</summary>
    Order,
}

/// <summary>
/// One sentence of an answer and the fact it rests on. The words are the model's;
/// the fact, its source and its evidence are what a read returned, so the chip the
/// shopper sees beside the sentence is the shop's and not the model's say-so.
/// </summary>
/// <param name="Text">The sentence, as the model phrased it.</param>
/// <param name="Source">Where the fact comes from: the chip.</param>
/// <param name="Fact">The fact as the read returned it.</param>
/// <param name="Quotes">For an approved claim, the words it was approved on.</param>
public sealed record Reason(string Text, FactSource Source, string Fact, IReadOnlyList<string> Quotes, double? Support);

/// <summary>A product the answer names. Its card is the storefront's, drawn from the
/// shop's own read of that code; the name is here for a reader that draws none.</summary>
public sealed record AnsweredProduct(string Code, string Name, IReadOnlyList<Reason> Reasons);

/// <param name="Code">The product whose details do not say, or none when it is the shop that has no such information.</param>
/// <param name="Sentence">Written by code, in es or en: the model supplies only what was asked about.</param>
/// <param name="Nearest">What the product's details do say that is nearest, cited.</param>
public sealed record NoData(string? Code, string? Name, string Sentence, IReadOnlyList<Reason> Nearest);

/// <summary>The order an answer is about. Its id is for the storefront's link to the
/// order's page — where a return is opened — and went nowhere near a model.</summary>
public sealed record AnsweredOrder(string Id, string Title);

/// <summary>A read the turn made, with what it was asked — a search's is where "see all" leads.</summary>
public sealed record TinoRead(string Tool, JsonElement Arguments, int Rows, int? Total);

/// <summary>
/// Tino's answer to one turn (phase 12b). Never prose of the model's own: products
/// with reasons that each cite a fact, sentences about the shop that each cite one,
/// a question back, or what the shop does not hold.
/// </summary>
/// <param name="SearchText">The shopper's last words: what the storefront searches for when Tino did not answer.</param>
/// <param name="ReasonsDropped">Sentences the model wrote that did not hold and were not shown.</param>
public sealed record TinoAnswer(
    TinoStatus Status, TinoCause? Cause, TinoKind? Kind,
    IReadOnlyList<AnsweredProduct> Products, IReadOnlyList<Reason> Says, string? Ask, NoData? NoData,
    IReadOnlyList<TinoRead> Reads, string SearchText, int ReasonsDropped,
    TurnCost? Cost, string? Profile, bool FellBack, string ConversationId, AnsweredOrder? Order = null);

/// <summary>What the model's reply holds once code has read it: one of the kinds, and how much of it was dropped.</summary>
/// <param name="Claim">For a <see cref="TinoKind.NoData"/> about a product, the statement
/// the model says its details do not make — what grounding checks that none of them does.</param>
public sealed record TinoBody(
    TinoKind Kind, IReadOnlyList<AnsweredProduct> Products, IReadOnlyList<Reason> Says, string? Ask, NoData? NoData, int Dropped,
    string? Claim = null, AnsweredOrder? Order = null);
