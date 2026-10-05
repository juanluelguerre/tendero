using ElGuerre.Tendero.Assist.Tino;
using ElGuerre.Tendero.SearchEval.Copilot;

namespace ElGuerre.Tendero.SearchEval.Assistant;

public sealed record AssistantCases(IReadOnlyList<AssistantCase> Cases);

/// <param name="As">Who asks: <c>anonymous</c>, or one of the laboratory's account holders.</param>
public sealed record AssistantCase(
    string Id, string Culture, string Question, Expectation Expect, string As = "anonymous", CaseContext? Context = null);

/// <summary>What the shopper is looking at: a product by its English name, or one of the laboratory's orders by its name.</summary>
public sealed record CaseContext(string? Product = null, string? Order = null);

/// <summary>
/// What a right answer has, each part compared and none judged by a model: a kind,
/// the reads made, products by their code, the **facts** cited — the shop's text,
/// not the model's words — an order by its id, and words no sentence may carry.
/// </summary>
/// <param name="Kinds">The answer's kind is one of these.</param>
/// <param name="Tools">Reads the turn must have made.</param>
/// <param name="NoTools">Reads it must not have made.</param>
/// <param name="AnyProduct">The answer names at least one of these products (English names).</param>
/// <param name="NoProduct">And none of these.</param>
/// <param name="Cites">Each is a fact some shown sentence rests on.</param>
/// <param name="NotSays">No sentence the model wrote and the shopper is shown contains any of these.</param>
/// <param name="Order">The answer is about this order of the laboratory's.</param>
/// <param name="NotOrder">And is not about this one, nor are its reads' answers shown.</param>
/// <param name="Language">The language the model's sentences are in.</param>
public sealed record Expectation(
    IReadOnlyList<string>? Kinds = null, IReadOnlyList<string>? Tools = null, IReadOnlyList<string>? NoTools = null,
    IReadOnlyList<string>? AnyProduct = null, IReadOnlyList<string>? NoProduct = null, IReadOnlyList<Citation>? Cites = null,
    IReadOnlyList<string>? NotSays = null, string? Order = null, string? NotOrder = null, string? Language = null);

/// <param name="Product">Whose fact, by English name; any product's, or the shop's or an order's, when omitted.</param>
/// <param name="Contains">Words of the fact as the shop holds it.</param>
public sealed record Citation(string Contains, string? Product = null);

/// <param name="Shown">Sentences of the model's the shopper is shown.</param>
/// <param name="Dropped">Sentences it wrote that did not hold.</param>
public sealed record AssistantVerdict(
    string Id, bool Passed, IReadOnlyList<string> Problems, string Status, string? Cause, string? Kind, int Shown, int Dropped,
    TurnMeasure Measure, IReadOnlyList<string> Sentences);

public sealed record AssistantScore(
    int Cases, int Passed, int Answered, IReadOnlyDictionary<string, int> Causes, int Shown, int Dropped,
    double SecondsP50, double SecondsP95, double SecondsMax, long LargestCall, long InputTokens, long OutputTokens);

public static class AssistantScoring
{
    public static AssistantVerdict Judge(
        AssistantCase item, TinoAnswer? answer, IReadOnlyList<ToolCall> calls, IReadOnlyDictionary<string, string> codes,
        IReadOnlyDictionary<string, string> orders, TurnMeasure measure)
    {
        var expect = item.Expect;
        var problems = new List<string>();
        var called = calls.Select(call => call.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var tool in expect.Tools ?? [])
        {
            if (!called.Contains(tool))
                problems.Add($"did not read {tool}");
        }

        foreach (var tool in expect.NoTools ?? [])
        {
            if (called.Contains(tool))
                problems.Add($"read {tool}");
        }

        // What the shopper is shown of the model's: each sentence with the fact under it.
        var reasons = answer is null
            ? []
            : answer.Products.SelectMany(product => product.Reasons.Select(reason => (product.Code, Reason: reason)))
                .Concat(answer.Says.Select(reason => ((string?)null, reason)).Select(pair => (Code: pair.Item1!, Reason: pair.reason)))
                .Concat((answer.NoData?.Nearest ?? []).Select(reason => (Code: answer.NoData!.Code!, Reason: reason)))
                .ToList();
        var sentences = reasons.Select(pair => pair.Reason.Text).ToList();

        if (answer is not { Status: TinoStatus.Answered })
        {
            problems.Add($"did not answer: {answer?.Cause?.ToString() ?? measure.Error ?? "no answer"}");
        }
        else
        {
            if (expect.Kinds is { Count: > 0 } kinds && !kinds.Contains(answer.Kind.ToString()!, StringComparer.OrdinalIgnoreCase))
                problems.Add($"answered {answer.Kind}, not {string.Join(" or ", kinds)}");

            var named = answer.Products.Select(product => product.Code).Append(answer.NoData?.Code).OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (expect.AnyProduct is { Count: > 0 } any && !any.Any(name => named.Contains(codes[name])))
                problems.Add($"named none of: {string.Join(", ", any)}");
            foreach (var name in (expect.NoProduct ?? []).Where(name => named.Contains(codes[name])))
                problems.Add($"named {name}");

            foreach (var citation in expect.Cites ?? [])
            {
                var cited = reasons.Any(pair =>
                    pair.Reason.Fact.Contains(citation.Contains, StringComparison.OrdinalIgnoreCase)
                    && (citation.Product is null || string.Equals(pair.Code, codes[citation.Product], StringComparison.OrdinalIgnoreCase)));
                if (!cited)
                    problems.Add($"cited no fact with \"{citation.Contains}\"" + (citation.Product is { } of ? $" of {of}" : ""));
            }

            foreach (var words in expect.NotSays ?? [])
            {
                if (sentences.Any(sentence => sentence.Contains(words, StringComparison.OrdinalIgnoreCase)))
                    problems.Add($"said \"{words}\"");
            }

            if (expect.Order is { } order && answer.Order?.Id != orders[order])
                problems.Add($"not about the order {order}");

            // The language the model writes in; a kind with none of its words has none to judge.
            var written = string.Join(' ', sentences.Append(answer.Ask).OfType<string>());
            if (expect.Language is { } language && written.Length > 0 && CopilotScoring.LanguageOf(written) is { } spoken && spoken != language)
                problems.Add($"written in {spoken}, not {language}");
        }

        // Whatever the answer is, it is never about an order the asker may not read.
        if (expect.NotOrder is { } forbidden && answer?.Order?.Id == orders[forbidden])
            problems.Add($"about the order {forbidden}");

        return new AssistantVerdict(item.Id, problems.Count == 0, problems, answer?.Status.ToString() ?? "Failed", answer?.Cause?.ToString(),
            answer?.Kind?.ToString(), sentences.Count, answer?.ReasonsDropped ?? 0, measure, sentences);
    }

    /// <summary>What the cases name that the laboratory does not have: the suite's mistake, found before any model is asked.</summary>
    public static IReadOnlyList<string> Unknown(
        AssistantCases cases, IReadOnlyDictionary<string, string> codes, LaboratoryPeople people, IReadOnlyCollection<string> tools)
    {
        var unknown = new List<string>();
        foreach (var item in cases.Cases)
        {
            var products = (item.Expect.AnyProduct ?? []).Concat(item.Expect.NoProduct ?? [])
                .Concat((item.Expect.Cites ?? []).Select(citation => citation.Product).OfType<string>())
                .Append(item.Context?.Product).OfType<string>();
            unknown.AddRange(products.Where(name => !codes.ContainsKey(name)).Select(name => $"{item.Id}: product \"{name}\""));

            string?[] named = [item.Context?.Order, item.Expect.Order, item.Expect.NotOrder];
            unknown.AddRange(named.OfType<string>().Where(order => !people.Orders.ContainsKey(order)).Select(order => $"{item.Id}: order \"{order}\""));
            unknown.AddRange((item.Expect.Tools ?? []).Concat(item.Expect.NoTools ?? []).Where(tool => !tools.Contains(tool))
                .Select(tool => $"{item.Id}: read \"{tool}\""));
            if (!people.Askers.ContainsKey(item.As))
                unknown.Add($"{item.Id}: asker \"{item.As}\"");
            if (item.Expect.Kinds?.FirstOrDefault(kind => !Enum.TryParse<TinoKind>(kind, ignoreCase: true, out _)) is { } kind)
                unknown.Add($"{item.Id}: kind \"{kind}\"");
        }

        return [.. unknown.Distinct()];
    }

    public static AssistantScore Summarise(IReadOnlyList<AssistantVerdict> verdicts)
    {
        var seconds = verdicts.Select(verdict => verdict.Measure.Seconds).Order().ToList();
        return new AssistantScore(
            verdicts.Count,
            verdicts.Count(verdict => verdict.Passed),
            verdicts.Count(verdict => verdict.Status == nameof(TinoStatus.Answered)),
            verdicts.Where(verdict => verdict.Cause is not null).GroupBy(verdict => verdict.Cause!).ToDictionary(group => group.Key, group => group.Count()),
            verdicts.Sum(verdict => verdict.Shown),
            verdicts.Sum(verdict => verdict.Dropped),
            Percentile(seconds, 0.50), Percentile(seconds, 0.95), seconds.Count == 0 ? 0 : seconds[^1],
            verdicts.Count == 0 ? 0 : verdicts.Max(verdict => verdict.Measure.LargestCall),
            verdicts.Sum(verdict => verdict.Measure.InputTokens),
            verdicts.Sum(verdict => verdict.Measure.OutputTokens));
    }

    /// <summary>The nearest-rank percentile: the value a share of the turns were at or under.</summary>
    public static double Percentile(IReadOnlyList<double> ordered, double share) =>
        ordered.Count == 0 ? 0 : ordered[Math.Clamp((int)Math.Ceiling(share * ordered.Count) - 1, 0, ordered.Count - 1)];

    public static string Line(string label, AssistantScore score) => FormattableString.Invariant(
        $"{label}: {score.Passed}/{score.Cases} passed · {score.Answered} answered{Causes(score)} · {score.Shown} sentences shown, {score.Dropped} dropped · p50 {score.SecondsP50:F1}s p95 {score.SecondsP95:F1}s max {score.SecondsMax:F1}s · largest call {score.LargestCall} tok · {score.InputTokens} in / {score.OutputTokens} out");

    private static string Causes(AssistantScore score) =>
        score.Causes.Count == 0 ? "" : $" ({string.Join(", ", score.Causes.OrderBy(pair => pair.Key).Select(pair => $"{pair.Value} {pair.Key}"))})";
}
