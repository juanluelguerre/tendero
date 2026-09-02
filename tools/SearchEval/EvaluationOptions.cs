namespace ElGuerre.Tendero.SearchEval;

/// <summary>
/// Parsed by hand: there are four options and they do not justify a package
/// (CLAUDE.md's dependency policy).
/// </summary>
public sealed record EvaluationOptions(
    string Suite,
    string Elasticsearch,
    string GoldenDirectory,
    string ThresholdsPath,
    string SeedPath,
    string? ReportPath,
    bool FailUnderThresholds)
{
    public static string Usage(IReadOnlyList<Suites.IEvaluationSuite> suites) =>
        $"""
        Usage: dotnet run --project tools/SearchEval -- [options]

          --suite <name>          Which gate to run (default search)
        {string.Join(Environment.NewLine, suites.Select(s => $"                            {s.Name}: {s.Description}"))}
          --elasticsearch <url>   Elasticsearch endpoint (default http://localhost:9200)
          --golden <dir>          Golden set directory  (default tools/SearchEval/golden)
          --thresholds <file>     Committed thresholds  (default tools/SearchEval/eval.thresholds.json)
          --seed <file>           Seed catalogue        (default seed/products.sample.json)
          --report <file>         Write the Markdown report to a file as well
          --ci                    Exit non-zero when a culture falls under its thresholds
        """;

    public static EvaluationOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ci = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            if (argument == "--ci")
            {
                ci = true;
                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{argument}'.");

            if (index + 1 >= args.Length)
                throw new ArgumentException($"Option '{argument}' needs a value.");

            values[argument[2..]] = args[++index];
        }

        return new EvaluationOptions(
            values.GetValueOrDefault("suite", "search"),
            values.GetValueOrDefault("elasticsearch", "http://localhost:9200"),
            values.GetValueOrDefault("golden", Path.Combine("tools", "SearchEval", "golden")),
            values.GetValueOrDefault("thresholds", Path.Combine("tools", "SearchEval", "eval.thresholds.json")),
            values.GetValueOrDefault("seed", Path.Combine("seed", "products.sample.json")),
            values.GetValueOrDefault("report"),
            ci);
    }
}
