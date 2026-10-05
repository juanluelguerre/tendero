using System.Text.Json;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Assist.Tino;

/// <summary>
/// The model's reply, read by code (the Tino spec, *Grounding*, the first check):
/// what names a product the turn did not read, cites a fact it did not issue, or
/// cites another product's fact is dropped and counted — never shown, never
/// repaired. A local model is held to the schema by its grammar; a hosted or a
/// scripted one is not, and comparisons slip past even a grammar, because the id
/// exists. So everything the schema says is checked again here.
/// </summary>
public static class TinoReply
{
    /// <summary>A sentence of an answer at most: a reason is short, and nothing here is an essay.</summary>
    public const int MaxSentence = 300;

    /// <summary>What was asked about, at most: it completes one sentence.</summary>
    public const int MaxAbout = 120;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Written(
        string? Kind, IReadOnlyList<WrittenProduct>? Products, IReadOnlyList<Cited>? Says, string? Ask,
        string? Code, string? About, string? Language, IReadOnlyList<Cited>? Nearest);

    private sealed record WrittenProduct(string? Code, IReadOnlyList<Cited>? Reasons);

    private sealed record Cited(string? Says, string? Cites);

    /// <returns>The answer the reply holds, or null when it holds none: not JSON, no
    /// kind, or nothing left once what does not hold was dropped.</returns>
    public static TinoBody? Read(string? reply, FactSheet sheet)
    {
        Written? written;
        try
        {
            written = string.IsNullOrWhiteSpace(reply) ? null : JsonSerializer.Deserialize<Written>(reply, Json);
        }
        catch (JsonException)
        {
            return null;
        }

        var dropped = 0;
        switch (written?.Kind)
        {
            case "products":
                var products = new List<AnsweredProduct>();
                foreach (var product in written.Products ?? [])
                {
                    // A product no read returned is not named, and what was said of it goes with it.
                    if (sheet.Product(product.Code) is not { } known)
                    {
                        dropped += product.Reasons?.Count ?? 0;
                        continue;
                    }

                    if (products.Count == AnswerSchema.MaxProducts || products.Any(named => named.Code == known.Key))
                        continue;
                    products.Add(new AnsweredProduct(known.Key, known.Name, Reasons(product.Reasons, known.Key, sheet, ref dropped)));
                }

                return products.Count > 0 ? new(TinoKind.Products, products, [], null, null, dropped) : null;

            case "shop":
                var says = Reasons(written.Says, Fact.TheShop, sheet, ref dropped);
                return says.Count > 0 ? new(TinoKind.Shop, [], says, null, null, dropped) : null;

            case "ask":
                return IsAQuestion(written.Ask) ? new(TinoKind.Ask, [], [], written.Ask!.Trim(), null, 0) : null;

            case "noData" when sheet.Product(written.Code) is { } about && Completes(written.About):
                return new(TinoKind.NoData, [], [], null,
                    new NoData(about.Key, about.Name, TinoPrompt.NoData(Language(written.Language), true, Trimmed(written.About!)),
                        Reasons(written.Nearest, about.Key, sheet, ref dropped)),
                    dropped);

            // The shop has no such information — and so does a noData about a product
            // the turn never read: there is nothing of it to say anything about.
            case "unknown" or "noData":
                return new(TinoKind.NoData, [], [], null,
                    new NoData(null, null, TinoPrompt.NoData(Language(written.Language), false, ""), []), dropped);

            default:
                return null;
        }
    }

    /// <summary>The reasons that hold: each cites a fact this turn issued, about this subject.</summary>
    private static List<Reason> Reasons(IReadOnlyList<Cited>? written, string subject, FactSheet sheet, ref int dropped)
    {
        var reasons = new List<Reason>();
        foreach (var reason in written ?? [])
        {
            var fact = sheet.Find(reason.Cites)?.Fact;
            var holds = fact is not null
                && string.Equals(fact.Subject, subject, StringComparison.OrdinalIgnoreCase)
                && reason.Says is { Length: > 0 and <= MaxSentence } says && !string.IsNullOrWhiteSpace(says);
            if (!holds)
            {
                dropped++;
                continue;
            }

            if (reasons.Count < AnswerSchema.MaxReasons)
                reasons.Add(new Reason(reason.Says!.Trim(), fact!.Source, fact.Text, fact.Quotes ?? [], fact.Support));
        }

        return reasons;
    }

    /// <summary>One short question and nothing that states a fact: no figure in it.</summary>
    private static bool IsAQuestion(string? ask) =>
        ask?.Trim() is { Length: > 0 and <= MaxSentence } question && question.EndsWith('?') && !question.Any(char.IsDigit);

    /// <summary>
    /// What was asked about completes one sentence written by code, so it must be a
    /// phrase and not a sentence of its own: a stop inside it would let the model end
    /// ours and start one nothing checks.
    /// </summary>
    private static bool Completes(string? about) =>
        Trimmed(about ?? "") is { Length: > 0 and <= MaxAbout } phrase && phrase.IndexOfAny(['.', ';', ':', '!', '\n']) < 0;

    private static string Trimmed(string about) => about.Trim().TrimEnd('.', '?', '!', ' ');

    private static string Language(string? language) => CultureNegotiation.Supports(language) ? language! : CultureNegotiation.Default;
}
