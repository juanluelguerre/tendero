using System.Diagnostics;
using System.Text.Json;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Assist.Ports;
using ElGuerre.Tendero.Assist.Steward;
using ElGuerre.Tendero.SearchEval.Copilot;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.SearchEval.Suites;

/// <summary>
/// Steward against its golden set (phase 12, S-6): each case a fresh conversation
/// through the same turn the API runs — the agent, the contexts' reads, the
/// proposals stored — over a laboratory shop, and each verdict exact. The run that
/// asks a real model is local and at phase close (a CI runner cannot hold a 4B
/// model); with <c>--script</c> it is a dry run of the suite itself.
/// </summary>
internal sealed class CopilotSuite : IEvaluationSuite
{
    private static readonly JsonSerializerOptions Indented = new(CopilotScoring.Json) { WriteIndented = true };

    public string Name => "copilot";

    /// <summary>Two dozen turns at up to six model calls each, minutes apiece on a CPU (S-2 measured up to ~458 s for one).</summary>
    public TimeSpan Budget => TimeSpan.FromHours(4);

    public string Description => "Steward against Copilot/cases.json: right tools, valid arguments, exact proposals, latency, tokens, cost";

    public async Task<int> RunAsync(EvaluationOptions options, CancellationToken cancellationToken)
    {
        if (options.Database is not { Length: > 0 } database)
            throw new ArgumentException("--suite copilot needs --database: a disposable Postgres the suite migrates and seeds.");

        var golden = JsonSerializer.Deserialize<CopilotCases>(
            await File.ReadAllTextAsync(Path.Combine(options.CopilotDirectory!, "cases.json"), cancellationToken), CopilotScoring.Json)!;
        var (profile, label) = Profile(options);

        await using var provider = CopilotLaboratory.Compose(database, profile, Directory.GetCurrentDirectory());
        await CopilotLaboratory.PrepareAsync(provider, Directory.GetCurrentDirectory(), cancellationToken);
        var codes = await CopilotLaboratory.ProductsAsync(provider, cancellationToken);

        // Before any model is asked: a case that names a product the shop does not
        // have is the suite's mistake, and blaming a model for it would be a lie.
        var missing = golden.Cases
            .SelectMany(item => item.Proposals.Select(proposal => proposal.Product).Append(item.Screen?.SubjectProduct))
            .OfType<string>().Where(name => !codes.ContainsKey(name)).Distinct().ToList();
        if (missing.Count > 0)
        {
            Console.Error.WriteLine($"The laboratory has no product named: {string.Join(", ", missing)}");
            return 2;
        }

        var verdicts = new List<CaseVerdict>();
        foreach (var item in golden.Cases)
        {
            var verdict = await AskAsync(provider, item, codes, cancellationToken);
            verdicts.Add(verdict);
            Console.Error.WriteLine($"  {item.Id,-24} {(verdict.Passed ? "pass" : "FAIL")} {verdict.Measure.Seconds,6:F1}s " +
                                    $"{verdict.Measure.LargestCall,5} tok  {string.Join("; ", verdict.Problems)}{verdict.Measure.Error}");
        }

        var score = CopilotScoring.Summarise(verdicts);
        Console.WriteLine(CopilotScoring.Line(label, score));
        if (options.ReportPath is { Length: > 0 } report)
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { profile = label, score, cases = verdicts }, Indented), cancellationToken);

        // Measured, not gated: S-7 compares the profiles, and the author picks the default with them.
        return score.Errors == score.Cases ? 2 : 0;
    }

    private static async Task<CaseVerdict> AskAsync(
        ServiceProvider provider, CopilotCase item, IReadOnlyDictionary<string, string> codes, CancellationToken cancellationToken)
    {
        var calls = provider.GetRequiredService<ToolCallLog>();
        var logs = provider.GetRequiredService<CapturedLogs>();
        logs.Clear();
        var log = provider.GetRequiredService<AIInvocationLog>();
        calls.Clear();
        while (log.Reader.TryRead(out _)) { }

        var screen = item.Screen is { } shown
            ? new ScreenContext(shown.Page, shown.SubjectProduct is { } name ? codes[name] : shown.Subject)
            : null;
        var clock = Stopwatch.StartNew();
        StewardAnswer? answer = null;
        string? error = null;
        try
        {
            await using var scope = provider.CreateAsyncScope();
            answer = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(
                new StewardTurnCommand([new TurnMessage("user", item.Question)], screen, $"copilot-{item.Id}-{Guid.CreateVersion7()}", item.Culture),
                cancellationToken);
            // An unavailable turn logged why; that is the case's error, not just its status.
            if (answer.Status != StewardStatus.Answered)
                error = string.Join(" | ", logs.Lines.Prepend(answer.Status.ToString()));
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
        {
            error = failure.Message;
        }

        var rows = new List<AIInvocation>();
        while (log.Reader.TryRead(out var row))
            rows.Add(row);

        var stored = new List<StoredProposal>();
        if (answer is { ProposalIds.Count: > 0 })
        {
            await using var scope = provider.CreateAsyncScope();
            var proposals = await scope.ServiceProvider.GetRequiredService<IProposalRepository>()
                .FindManyAsync([.. answer.ProposalIds.Select(id => new ProposalId(Guid.Parse(id)))], cancellationToken);
            stored.AddRange(proposals.Select(proposal =>
                new StoredProposal(proposal.Command, proposal.Subject, JsonDocument.Parse(proposal.Payload).RootElement.Clone())));
        }

        var measure = new TurnMeasure(
            answer?.Status.ToString() ?? "Failed",
            rows.Count,
            rows.Sum(row => row.InputTokens),
            rows.Sum(row => row.OutputTokens),
            rows.Sum(row => row.CostMicroEuros),
            clock.Elapsed.TotalSeconds,
            rows.Count == 0 ? 0 : rows.Max(row => row.InputTokens + row.OutputTokens),
            error);
        return CopilotScoring.Judge(item, calls.Calls, stored, codes, measure);
    }

    /// <summary>The profile the turn asks, in configuration's words: a script, or an endpoint and its model.</summary>
    private static (IReadOnlyDictionary<string, string?> Settings, string Label) Profile(EvaluationOptions options)
    {
        const string prefix = "Steward:Models:" + CopilotLaboratory.Profile + ":";
        if (options.Script is { Length: > 0 } script)
        {
            return (new Dictionary<string, string?>
            {
                [prefix + "Kind"] = "Scripted",
                [prefix + "Script"] = Path.GetFullPath(script),
                [prefix + "Model"] = "scripted",
                [prefix + "ContextTokens"] = options.ContextTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }, "scripted");
        }

        if (options.Model is not { Length: > 0 } model)
            throw new ArgumentException("--suite copilot needs --model with --chat, or --script for a dry run.");

        var endpoint = options.Chat!.TrimEnd('/');
        return (new Dictionary<string, string?>
        {
            [prefix + "Kind"] = "OpenAICompatible",
            [prefix + "Endpoint"] = endpoint.EndsWith("/v1", StringComparison.Ordinal) ? endpoint + "/" : endpoint + "/v1/",
            [prefix + "Model"] = model,
            [prefix + "Auth"] = "None",
            [prefix + "ContextTokens"] = options.ContextTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [prefix + "TimeoutSeconds"] = "480",
        }, model);
    }
}
