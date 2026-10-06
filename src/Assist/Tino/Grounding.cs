using System.Globalization;
using System.Text.RegularExpressions;
using ElGuerre.Tendero.AI.Support;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Assist.Tino;

/// <summary>
/// The figures of a sentence against the text it rests on (the Tino spec,
/// *Grounding*, the second check). A model that scores meaning is weak with
/// numbers, so a number is not scored: it is compared. Every figure a reason
/// states must be one the fact states.
/// </summary>
public static partial class Figures
{
    /// <summary>Whether every number in <paramref name="sentence"/> is a number of <paramref name="premise"/>.</summary>
    public static bool AllIn(string sentence, string premise)
    {
        var known = Of(premise).ToHashSet();
        return Of(sentence).All(known.Contains);
    }

    /// <summary>The numbers a text states, as values: <c>0,8</c> and <c>0.80</c> are the same figure, in either language.</summary>
    public static IEnumerable<decimal> Of(string text) =>
        Number().Matches(text).Select(match => decimal.Parse(match.Value.Replace(',', '.'), CultureInfo.InvariantCulture));

    [GeneratedRegex(@"\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex Number();
}

/// <summary>The support scorer did not answer: nothing can be shown, because nothing was checked.</summary>
public sealed class SupportUnavailableException(Exception inner) : Exception("The support scorer did not answer.", inner);

/// <summary>
/// Whether what the model wrote is said by the fact it cites (the Tino spec,
/// *Grounding*). <see cref="TinoReply"/> already knows the citation exists and is
/// the right product's; that leaves a real fact under a sentence it does not say —
/// <em>"it is stainless steel"</em> cited to <em>pieces: 3</em>. Here each sentence
/// is held to its fact: its figures compared, its meaning scored by the support
/// port (ADR 0018's, the one that scores a claim against its quote). What does not
/// hold is dropped and counted, never shown and never repaired.
///
/// The premise is the product's name and the fact. A reason says <em>"a probe
/// thermometer made of stainless steel"</em> over <em>material: stainless steel</em>:
/// the fact alone does not entail it, the name and the fact do — measured
/// (<c>docs/analysis/tino-models.md</c>). The name is the shop's, read this turn.
/// </summary>
public sealed class Grounding(ISupportScorer scorer, double threshold)
{
    public async Task<TinoBody> HoldAsync(TinoBody body, FactSheet sheet, CancellationToken cancellationToken)
    {
        var dropped = body.Dropped;
        switch (body.Kind)
        {
            case TinoKind.Products:
                var products = new List<AnsweredProduct>();
                foreach (var product in body.Products)
                {
                    var held = await HeldAsync(product.Reasons, product.Name, cancellationToken);
                    dropped += product.Reasons.Count - held.Count;
                    products.Add(product with { Reasons = held });
                }

                // A product whose reasons all fell is the turn's to leave out: here it is only counted.
                return body with { Products = products, Dropped = dropped };

            // An order's facts are whole sentences and so is the policy: each is held to
            // the fact alone.
            case TinoKind.Shop or TinoKind.Order:
                var says = await HeldAsync(body.Says, null, cancellationToken);
                dropped += body.Says.Count - says.Count;
                return body with { Says = says, Dropped = dropped };

            case TinoKind.NoData when body.NoData is { Code: { } code } missing:
                // "The details do not say X" stands only if none of them says X. One
                // that does is the answer: that product, X as its reason, that fact cited.
                if (body.Claim is { Length: > 0 } claim)
                {
                    foreach (var fact in sheet.Facts.Where(fact => string.Equals(fact.Fact.Subject, code, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (await HoldsAsync(claim, Premise(missing.Name, fact.Fact.Text), cancellationToken))
                        {
                            return new TinoBody(TinoKind.Products,
                                [new AnsweredProduct(code, missing.Name ?? code,
                                    [new Reason(claim, fact.Fact.Source, fact.Fact.Text, fact.Fact.Quotes ?? [], fact.Fact.Support)])],
                                [], null, null, dropped);
                        }
                    }
                }

                var nearest = await HeldAsync(missing.Nearest, missing.Name, cancellationToken);
                dropped += missing.Nearest.Count - nearest.Count;
                return body with { NoData = missing with { Nearest = nearest }, Dropped = dropped };

            default:
                return body;
        }
    }

    private async Task<List<Reason>> HeldAsync(IReadOnlyList<Reason> reasons, string? name, CancellationToken cancellationToken)
    {
        var held = new List<Reason>();
        foreach (var reason in reasons)
        {
            if (await HoldsAsync(reason.Text, Premise(name, reason.Fact), cancellationToken))
                held.Add(reason);
        }

        return held;
    }

    /// <summary>The figures first: they cost nothing, and a sentence with a number of its own is not asked about.</summary>
    private async Task<bool> HoldsAsync(string sentence, string premise, CancellationToken cancellationToken)
    {
        if (!Figures.AllIn(sentence, premise))
            return false;

        try
        {
            return await scorer.ScoreAsync(premise, sentence, cancellationToken) >= threshold;
        }
        catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new SupportUnavailableException(failure);
        }
    }

    private static string Premise(string? name, string fact) => name is { Length: > 0 } ? $"{name}. {fact}" : fact;
}
