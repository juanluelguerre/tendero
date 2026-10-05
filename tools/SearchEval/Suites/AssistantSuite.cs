using System.Diagnostics;
using System.Text.Json;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Assist.Tino;
using ElGuerre.Tendero.Assist.Turns;
using ElGuerre.Tendero.SearchEval.Assistant;
using ElGuerre.Tendero.SearchEval.Copilot;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.SearchEval.Suites;

/// <summary>
/// Tino against its golden set (phase 12b, T-5): each case a fresh conversation
/// through the turn the API runs — the shopper's reads, the answer held to its
/// schema, grounding — asked as nobody or as one of the laboratory's shoppers, on
/// the page a case says. Each verdict is compared, none judged by a model: a kind,
/// the reads, products by code, the facts cited, an order by its id.
///
/// Local and at phase close, like Steward's: a CI runner holds neither the model
/// nor the scorer. Measured, not gated — the numbers fix the caps and the
/// threshold (<c>docs/analysis/tino-models.md</c>).
/// </summary>
internal sealed class AssistantSuite : IEvaluationSuite
{
    private static readonly JsonSerializerOptions Indented = new(CopilotScoring.Json) { WriteIndented = true };

    public string Name => "assistant";

    public TimeSpan Budget => TimeSpan.FromHours(1);

    public string Description => "Tino against Assistant/cases.json: kind, reads, products, cited facts, orders by who asks; time, tokens, sentences dropped";

    public async Task<int> RunAsync(EvaluationOptions options, CancellationToken cancellationToken)
    {
        if (options.Database is not { Length: > 0 } database)
            throw new ArgumentException("--suite assistant needs --database: a disposable Postgres the suite migrates and fills.");
        if (options.Model is not { Length: > 0 } || options.Chat is not { Length: > 0 })
            throw new ArgumentException("--suite assistant needs --chat and --model: the endpoint Tino's model answers on, and what it serves.");

        var golden = JsonSerializer.Deserialize<AssistantCases>(
            await File.ReadAllTextAsync(Path.Combine(options.AssistantDirectory!, "cases.json"), cancellationToken), CopilotScoring.Json)!;
        var (profile, label) = CopilotSuite.Profile(options);

        var root = Directory.GetCurrentDirectory();
        await using var provider = AssistantLaboratory.Compose(database, options.Elasticsearch, options.Tei!, profile, root);
        var people = await AssistantLaboratory.PrepareAsync(provider, root, cancellationToken);
        var codes = await CopilotLaboratory.ProductsAsync(provider, cancellationToken);

        string[] tools;
        using (var scope = provider.CreateScope())
            tools = [.. scope.ServiceProvider.GetServices<IReadTool>().Where(tool => tool.Audience == ToolAudience.Shopper).Select(tool => tool.Name)];

        // Before any model is asked: a case that names what the laboratory does not
        // have is the suite's mistake, and blaming a model for it would be a lie.
        if (AssistantScoring.Unknown(golden, codes, people, tools) is { Count: > 0 } unknown)
        {
            Console.Error.WriteLine("The cases name what the laboratory does not have:");
            foreach (var line in unknown)
                Console.Error.WriteLine("  " + line);
            return 2;
        }

        var only = options.Only is { Length: > 0 } filter ? filter.Split(',') : null;
        var verdicts = new List<AssistantVerdict>();
        foreach (var item in golden.Cases.Where(item => only is null || only.Any(part => item.Id.Contains(part, StringComparison.OrdinalIgnoreCase))))
        {
            var verdict = await AskAsync(provider, item, codes, people, cancellationToken);
            verdicts.Add(verdict);
            Console.Error.WriteLine($"  {item.Id,-28} {(verdict.Passed ? "pass" : "FAIL")} {verdict.Measure.Seconds,5:F1}s {verdict.Kind ?? verdict.Cause ?? "-",-9} " +
                                    $"{verdict.Shown} shown {verdict.Dropped} dropped  {string.Join("; ", verdict.Problems)}");
        }

        var score = AssistantScoring.Summarise(verdicts);
        Console.WriteLine(AssistantScoring.Line(label, score));
        if (options.ReportPath is { Length: > 0 } report)
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { profile = label, prompt = TinoPrompt.Version, score, cases = verdicts }, Indented), cancellationToken);

        return score.Answered == 0 ? 2 : 0;
    }

    private static async Task<AssistantVerdict> AskAsync(
        ServiceProvider provider, AssistantCase item, IReadOnlyDictionary<string, string> codes, LaboratoryPeople people,
        CancellationToken cancellationToken)
    {
        var calls = provider.GetRequiredService<ToolCallLog>();
        var logs = provider.GetRequiredService<CapturedLogs>();
        var log = provider.GetRequiredService<AIInvocationLog>();
        logs.Clear();
        calls.Clear();
        while (log.Reader.TryRead(out _)) { }

        var context = item.Context switch
        {
            { Product: { } product } => new TinoContext(TinoContextKind.Product, codes[product]),
            { Order: { } order } => new TinoContext(TinoContextKind.Order, people.Orders[order]),
            _ => null,
        };

        provider.GetRequiredService<Asker>().Current = people.Askers[item.As];
        var clock = Stopwatch.StartNew();
        TinoAnswer? answer = null;
        string? error = null;
        try
        {
            await using var scope = provider.CreateAsyncScope();
            answer = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(
                new TinoTurnCommand([new TurnMessage("user", item.Question)], context, $"assistant-{item.Id}-{Guid.CreateVersion7()}", item.Culture),
                cancellationToken);
            if (answer.Status != TinoStatus.Answered)
                error = string.Join(" | ", logs.Lines.Prepend(answer.Cause?.ToString() ?? "Unavailable"));
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
        {
            error = failure.Message;
        }
        finally
        {
            provider.GetRequiredService<Asker>().Current = Asker.Shopkeeper;
        }

        var rows = new List<AIInvocation>();
        while (log.Reader.TryRead(out var row))
            rows.Add(row);

        var measure = new TurnMeasure(
            answer?.Status.ToString() ?? "Failed", rows.Count, rows.Sum(row => row.InputTokens), rows.Sum(row => row.OutputTokens),
            rows.Sum(row => row.CostMicroEuros), clock.Elapsed.TotalSeconds,
            rows.Count == 0 ? 0 : rows.Max(row => row.InputTokens + row.OutputTokens), error);
        return AssistantScoring.Judge(item, answer, calls.Calls, codes, people.Orders, measure);
    }
}
