using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElGuerre.Tendero.SearchEval.Copilot;

/// <summary>
/// The copilot's golden set (phase 12, S-6; Steward spec, *Evaluation and tests*):
/// questions over a fixed laboratory, each with the reads it should make and the
/// proposals it should store. A proposal is a command, so it is compared exactly —
/// no judge model.
/// </summary>
public sealed record CopilotCases(int Version, IReadOnlyList<CopilotCase> Cases);

/// <param name="Culture">The language the question is asked in: es or en.</param>
/// <param name="Screen">The screen it is asked on; <see cref="CopilotScreen.SubjectProduct"/>
/// names a product whose code the laboratory fills in, minted at import as it is.</param>
/// <param name="Tools">The reads it should make, each with the arguments that matter.</param>
/// <param name="Proposals">What it should store; empty means nothing — a question that
/// only asks must not propose.</param>
public sealed record CopilotCase(
    string Id,
    string Question,
    string Culture,
    CopilotScreen? Screen,
    IReadOnlyList<ExpectedTool> Tools,
    IReadOnlyList<ExpectedProposal> Proposals);

public sealed record CopilotScreen(string Page, string? Subject, string? SubjectProduct);

/// <param name="Arguments">Each must hold on one call of the tool: exact, ignoring case,
/// or — a value starting with <c>~</c> — contained, for the words a model chooses.</param>
public sealed record ExpectedTool(string Name, IReadOnlyDictionary<string, string>? Arguments);

/// <param name="Product">The product, by its English name in the seed.</param>
/// <param name="Fields">The payload's fields that must be what the case says, ignoring case.</param>
public sealed record ExpectedProposal(string Command, string Product, IReadOnlyDictionary<string, string>? Fields);

/// <summary>A read the turn made, as the model asked for it.</summary>
public sealed record ToolCall(string Name, JsonElement Arguments);

/// <summary>A proposal the turn stored: its command, its subject's code, its payload.</summary>
public sealed record StoredProposal(string Command, string Subject, JsonElement Payload);

/// <summary>What one turn did and cost, as the suite measured it.</summary>
/// <param name="LargestCall">The most tokens one model call took, input and output:
/// what the context window must hold.</param>
public sealed record TurnMeasure(
    string Status, int Calls, long InputTokens, long OutputTokens, long MicroEuros, double Seconds, long LargestCall, string? Error);

/// <param name="RightTools">Every expected read was made.</param>
/// <param name="ValidArguments">Every expected read was made with the arguments that matter.</param>
/// <param name="ExactProposals">Exactly the expected proposals were stored, no more.</param>
public sealed record CaseVerdict(
    string Id, bool RightTools, bool ValidArguments, bool ExactProposals,
    IReadOnlyList<string> Called, IReadOnlyList<string> Problems, TurnMeasure Measure)
{
    public bool Passed => RightTools && ValidArguments && ExactProposals && Measure.Error is null;
}

public sealed record CopilotScore(
    int Cases, double RightTools, double ValidArguments, double ExactProposals, double Passed,
    double LatencyP50Seconds, double LatencyP95Seconds, double TokensPerTurn, double EurosPerTurn,
    long LargestCall, int Errors);

/// <summary>The suite's arithmetic, kept pure so it is tested without a model or a database.</summary>
public static class CopilotScoring
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// One case's verdict. <paramref name="codes"/> maps a seed product's English name
    /// to the code the laboratory gave it at import.
    /// </summary>
    public static CaseVerdict Judge(
        CopilotCase expected, IReadOnlyList<ToolCall> calls, IReadOnlyList<StoredProposal> stored,
        IReadOnlyDictionary<string, string> codes, TurnMeasure measure)
    {
        var problems = new List<string>();

        var rightTools = true;
        var validArguments = true;
        foreach (var tool in expected.Tools)
        {
            var made = calls.Where(call => call.Name == tool.Name).ToList();
            if (made.Count == 0)
            {
                rightTools = validArguments = false;
                problems.Add($"never called {tool.Name}");
                continue;
            }

            if (tool.Arguments is { Count: > 0 } wanted && !made.Any(call => Holds(call.Arguments, wanted)))
            {
                validArguments = false;
                problems.Add($"{tool.Name} without {string.Join(", ", wanted.Select(pair => $"{pair.Key}={pair.Value}"))}: " +
                             string.Join(" | ", made.Select(call => call.Arguments.GetRawText())));
            }
        }

        var unmatched = stored.ToList();
        var exact = true;
        foreach (var proposal in expected.Proposals)
        {
            if (!codes.TryGetValue(proposal.Product, out var code))
                throw new InvalidOperationException($"Case {expected.Id}: no product named '{proposal.Product}' in the laboratory.");

            var match = unmatched.FirstOrDefault(candidate =>
                candidate.Command == proposal.Command && candidate.Subject == code &&
                (proposal.Fields is not { Count: > 0 } fields || Holds(candidate.Payload, fields)));
            if (match is null)
            {
                exact = false;
                problems.Add($"no {proposal.Command} for {proposal.Product}");
            }
            else
            {
                unmatched.Remove(match);
            }
        }

        if (unmatched.Count > 0)
        {
            exact = false;
            problems.Add("proposed besides: " + string.Join(", ", unmatched.Select(extra => $"{extra.Command} {extra.Subject}")));
        }

        return new CaseVerdict(expected.Id, rightTools, validArguments, exact,
            [.. calls.Select(call => call.Name)], problems, measure);
    }

    /// <summary>Every wanted field holds: exact ignoring case and spacing, or contained when it starts with ~.</summary>
    public static bool Holds(JsonElement arguments, IReadOnlyDictionary<string, string> wanted) =>
        arguments.ValueKind == JsonValueKind.Object && wanted.All(pair =>
        {
            var property = arguments.EnumerateObject()
                .FirstOrDefault(candidate => string.Equals(candidate.Name, pair.Key, StringComparison.OrdinalIgnoreCase));
            if (property.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                return false;

            var actual = (property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText()).Trim();
            return pair.Value.StartsWith('~')
                ? actual.Contains(pair.Value[1..], StringComparison.OrdinalIgnoreCase)
                : string.Equals(actual, pair.Value.Trim(), StringComparison.OrdinalIgnoreCase);
        });

    public static CopilotScore Summarise(IReadOnlyList<CaseVerdict> verdicts)
    {
        if (verdicts.Count == 0)
            return new CopilotScore(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        double Share(Func<CaseVerdict, bool> holds) => (double)verdicts.Count(holds) / verdicts.Count;
        var seconds = verdicts.Select(verdict => verdict.Measure.Seconds).Order().ToList();
        return new CopilotScore(
            verdicts.Count,
            Share(verdict => verdict.RightTools),
            Share(verdict => verdict.ValidArguments),
            Share(verdict => verdict.ExactProposals),
            Share(verdict => verdict.Passed),
            Percentile(seconds, 0.50),
            Percentile(seconds, 0.95),
            verdicts.Average(verdict => verdict.Measure.InputTokens + verdict.Measure.OutputTokens),
            verdicts.Average(verdict => verdict.Measure.MicroEuros) / 1_000_000d,
            verdicts.Max(verdict => verdict.Measure.LargestCall),
            verdicts.Count(verdict => verdict.Measure.Error is not null));
    }

    /// <summary>Nearest rank: the value at or above the share asked for, never between two.</summary>
    public static double Percentile(IReadOnlyList<double> sorted, double share) =>
        sorted.Count == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(share * sorted.Count) - 1, 0, sorted.Count - 1)];

    public static string Line(string profile, CopilotScore s) => string.Create(CultureInfo.InvariantCulture,
        $"{profile,-22} tools {s.RightTools:F2}  args {s.ValidArguments:F2}  proposals {s.ExactProposals:F2}  passed {s.Passed:F2}  " +
        $"p50 {s.LatencyP50Seconds:F1}s  p95 {s.LatencyP95Seconds:F1}s  tokens/turn {s.TokensPerTurn:F0}  €/turn {s.EurosPerTurn:F5}  " +
        $"largest call {s.LargestCall}  errors {s.Errors}");
}
