using System.Globalization;
using System.Text;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Evaluation;

namespace ElGuerre.Tendero.SearchEval;

/// <summary>One strategy's scores, one per culture.</summary>
/// <param name="Gated">
/// False for a diagnostic strategy: it is reported, it has no thresholds, and it
/// can never fail a run.
/// </param>
public sealed record StrategyScores(string Strategy, IReadOnlyList<CultureScore> Cultures, bool Gated = true);

/// <summary>
/// The report is read in a console and published as a CI artefact, so it comes
/// out as Markdown: pasting it into a PR has to be readable without tools.
/// </summary>
public static class EvaluationReport
{
    private const string Lexical = "lexical";
    private const string Hybrid = "hybrid";

    public static string Render(IReadOnlyList<StrategyScores> results, EvaluationThresholds thresholds, bool rerank = false)
    {
        var report = new StringBuilder();
        report.AppendLine("# Search relevance report").AppendLine();

        if (results.Any(result => result.Strategy == Hybrid))
            report.AppendLine(CultureInfo.InvariantCulture, $"Hybrid is fused by rank{(rerank ? " and reranked" : ", not reranked")}.").AppendLine();

        RenderSummary(report, results, thresholds);
        RenderBreakdown(report, results, "kind", query => query.Kind);
        RenderBreakdown(report, results, "split", query => query.Split);
        RenderNoticeByKind(report, results);

        var lexical = results.FirstOrDefault(result => result.Strategy == Lexical);
        var hybrid = results.FirstOrDefault(result => result.Strategy == Hybrid);

        if (lexical is not null && hybrid is not null)
            RenderComparison(report, lexical, hybrid);
        else
            foreach (var result in results.Where(result => result.Gated))
                RenderQueries(report, result);

        if (results.Count > 1)
            RenderCoverage(report, results);

        RenderNoAnswer(report, results);

        return report.ToString();
    }

    public static bool Passes(CultureScore score, CultureThresholds thresholds) => thresholds.AreMetBy(score);

    private static void RenderSummary(StringBuilder report, IReadOnlyList<StrategyScores> results, EvaluationThresholds thresholds)
    {
        report.AppendLine("| strategy | culture | queries | NDCG@10 | threshold | recall@50 | threshold | judged@10 | notice rate | false out-of-scope | mean ms | |");
        report.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");

        foreach (var result in results)
        {
            foreach (var score in result.Cultures)
            {
                var (ndcgLimit, recallLimit, verdict) = result.Gated
                    ? Gate(thresholds.For(result.Strategy, score.Culture), score)
                    : ("—", "—", "diagnostic");

                report.AppendLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"| {result.Strategy} | {score.Culture} | {score.ScoredQueries} | {score.MeanNdcgAt10:0.000} | {ndcgLimit} " +
                        $"| {score.MeanRecallAt50:0.000} | {recallLimit} | {score.MeanJudgedAt10:0.000} | {score.NoticeRate:0.000} " +
                        $"| {score.FalseOutOfScope} | {score.MeanMilliseconds:0} | {verdict} |"));
            }
        }

        report.AppendLine().AppendLine(
            "`judged@10` is how much of a strategy's first ten the golden set has any judgment for. " +
            "An unjudged result scores as irrelevant, so below 1.000 the NDCG beside it is a lower bound. " +
            "`notice rate` is how often a scored query — one with a real answer — was told nothing matches exactly; " +
            "`false out-of-scope` is how many were told the shop does not sell them at all. Both are reported, not gated.");
    }

    /// <summary>
    /// NDCG@10 per strategy, grouped by one property of the query — its kind or its
    /// split. An overall mean hides the one thing that decides whether hybrid search
    /// earns its place: it can lose a little on the queries lexical already answers
    /// and win a lot on the ones nobody names a product in.
    /// </summary>
    private static void RenderBreakdown(
        StringBuilder report, IReadOnlyList<StrategyScores> results, string title, Func<QueryScore, string?> key)
    {
        if (!results.SelectMany(result => result.Cultures).SelectMany(culture => culture.Queries).Any(query => key(query) is not null))
            return;

        foreach (var culture in results[0].Cultures.Select(score => score.Culture))
        {
            report.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## {culture}: NDCG@10 by {title}").AppendLine();
            report.AppendLine($"| {title} | queries | " + string.Join(" | ", results.Select(result => result.Strategy)) + " |");
            report.AppendLine("|---|---:|" + string.Concat(Enumerable.Repeat("---:|", results.Count)));

            var groups = results[0].Cultures.Single(score => score.Culture == culture).Queries
                .GroupBy(query => key(query) ?? "unclassified", StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal);

            foreach (var group in groups)
            {
                // A kind nothing can answer has no NDCG; it has its own section.
                if (group.All(query => query.NdcgAt10 is null))
                    continue;

                var members = group.Select(query => query.Query).ToHashSet(StringComparer.Ordinal);

                var cells = results.Select(result => Format(Mean(
                    result.Cultures.Single(score => score.Culture == culture).Queries
                        .Where(query => members.Contains(query.Query))
                        .Select(query => query.NdcgAt10))));

                report.AppendLine(CultureInfo.InvariantCulture, $"| {group.Key} | {group.Count()} | {string.Join(" | ", cells)} |");
            }
        }
    }

    /// <summary>
    /// How often the "nothing matches exactly" notice lands on a query that HAS a
    /// real answer, grouped by what kind of query it is. The overall rate in the
    /// summary cannot tell the two cases apart: on a vague query nobody writes the
    /// words of, the notice is honest; on a product type the shop answers well it
    /// is pedantic. Only the breakdown says which of the two the shop is doing —
    /// which is why the queries themselves are printed beside the rate.
    ///
    /// Report only. Nothing here gates a run.
    /// </summary>
    private static void RenderNoticeByKind(StringBuilder report, IReadOnlyList<StrategyScores> results)
    {
        const int Sample = 5;

        // No answerer was asked, so the run claims nothing about the notice.
        if (!results.SelectMany(result => result.Cultures).SelectMany(culture => culture.Queries).Any(query => query.Match is not null))
            return;

        foreach (var culture in results[0].Cultures.Select(score => score.Culture))
        {
            var byStrategy = results.ToDictionary(
                result => result.Strategy,
                result => result.Cultures.Single(score => score.Culture == culture).Queries
                    .Where(query => query.NdcgAt10 is not null && !QueryKinds.IsNoAnswer(query.Kind))
                    .ToDictionary(query => query.Query, StringComparer.Ordinal),
                StringComparer.Ordinal);

            var groups = byStrategy[results[0].Strategy].Values
                .GroupBy(query => query.Kind ?? "unclassified", StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList();

            if (groups.Count == 0)
                continue;

            report.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## {culture}: notice rate by kind").AppendLine();
            report.AppendLine(
                "The share of SCORED queries — the ones with a real answer — whose page was told nothing matches " +
                $"exactly. Reported, not gated. The last column names the queries that got it (at most {Sample} per kind).")
                .AppendLine();

            report.AppendLine(
                "| kind | queries | " + string.Join(" | ", results.Select(result => $"{result.Strategy} notice rate")) +
                " | queries with the notice |");
            report.AppendLine("|---|---:|" + string.Concat(Enumerable.Repeat("---:|", results.Count)) + "---|");

            foreach (var group in groups)
            {
                var members = group.Select(query => query.Query).ToHashSet(StringComparer.Ordinal);

                var rates = results.Select(result =>
                {
                    var queries = byStrategy[result.Strategy].Values.Where(query => members.Contains(query.Query)).ToList();

                    return queries.Count == 0
                        ? "n/a"
                        : (queries.Count(query => query.Match == SearchMatch.Related) / (double)queries.Count)
                            .ToString("0.000", CultureInfo.InvariantCulture);
                });

                // The notice is decided after the strategy answered and does not
                // depend on which one did, so the queries are listed once, from the
                // first — a second identical column would be noise.
                var noticed = group.Where(query => query.Match == SearchMatch.Related).Select(query => query.Query).ToList();

                var sample = noticed.Count == 0
                    ? "—"
                    : string.Join("; ", noticed.Take(Sample)) +
                      (noticed.Count > Sample ? $" (+{noticed.Count - Sample} more)" : "");

                report.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"| {group.Key} | {group.Count()} | {string.Join(" | ", rates)} | {sample} |");
            }
        }
    }

    private static double? Mean(IEnumerable<double?> values)
    {
        var scored = values.OfType<double>().ToList();
        return scored.Count == 0 ? null : scored.Average();
    }

    private static (string Ndcg, string Recall, string Verdict) Gate(CultureThresholds limit, CultureScore score) =>
    (
        limit.NdcgAt10.ToString("0.000", CultureInfo.InvariantCulture),
        limit.RecallAt50.ToString("0.000", CultureInfo.InvariantCulture),
        limit.AreMetBy(score) ? "pass" : "FAIL"
    );

    /// <summary>Worst first: the report has to start with what needs fixing.</summary>
    private static void RenderQueries(StringBuilder report, StrategyScores result)
    {
        foreach (var score in result.Cultures)
        {
            report.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## {score.Culture}").AppendLine();
            report.AppendLine("| query | NDCG@10 | recall@50 | judged@10 | hits |");
            report.AppendLine("|---|---:|---:|---:|---:|");

            foreach (var query in score.Queries.OrderBy(query => query.NdcgAt10 ?? double.MaxValue))
            {
                report.AppendLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"| {query.Query} | {Format(query.NdcgAt10)} | {Format(query.RecallAt50)} | {Format(query.JudgedAt10)} | {query.Returned} |"));
            }
        }
    }

    /// <summary>
    /// The two columns and the difference, ordered by what hybrid LOST first: a
    /// query that got worse is the one a reviewer has to read, and an average that
    /// went up can hide it.
    /// </summary>
    private static void RenderComparison(StringBuilder report, StrategyScores before, StrategyScores after)
    {
        foreach (var (words, meaning) in before.Cultures.Zip(after.Cultures))
        {
            report.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## {words.Culture}: {before.Strategy} → {after.Strategy}").AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"| query | {before.Strategy} NDCG@10 | {after.Strategy} NDCG@10 | Δ | {before.Strategy} recall@50 | {after.Strategy} recall@50 |");
            report.AppendLine("|---|---:|---:|---:|---:|---:|");

            var rows = words.Queries.Zip(meaning.Queries)
                .Where(pair => pair.First.NdcgAt10 is not null || pair.Second.NdcgAt10 is not null)
                .OrderBy(pair => (pair.Second.NdcgAt10 ?? 0) - (pair.First.NdcgAt10 ?? 0))
                .ThenBy(pair => pair.First.Query, StringComparer.Ordinal);

            foreach (var (lexical, hybrid) in rows)
            {
                var delta = (hybrid.NdcgAt10 ?? 0) - (lexical.NdcgAt10 ?? 0);

                report.AppendLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"| {lexical.Query} | {Format(lexical.NdcgAt10)} | {Format(hybrid.NdcgAt10)} | {delta:+0.000;-0.000;0.000} " +
                        $"| {Format(lexical.RecallAt50)} | {Format(hybrid.RecallAt50)} |"));
            }
        }
    }

    /// <summary>
    /// How much of each strategy's first ten the ruler can see, per query, least
    /// covered first — with the diagnostic strategies' NDCG beside it, because a
    /// vector side that scores low where its results are mostly unjudged has not
    /// been measured yet.
    /// </summary>
    private static void RenderCoverage(StringBuilder report, IReadOnlyList<StrategyScores> results)
    {
        var diagnostics = results.Where(result => !result.Gated).ToList();
        var cultures = results[0].Cultures.Select(culture => culture.Culture);

        foreach (var culture in cultures)
        {
            var byStrategy = results.ToDictionary(
                result => result.Strategy,
                result => result.Cultures.Single(score => score.Culture == culture).Queries
                    .ToDictionary(query => query.Query, StringComparer.Ordinal),
                StringComparer.Ordinal);

            report.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## {culture}: judgment coverage").AppendLine();
            report.AppendLine(
                "| query | " + string.Join(" | ", results.Select(result => $"{result.Strategy} judged@10")) +
                string.Concat(diagnostics.Select(result => $" | {result.Strategy} NDCG@10")) + " | unjudged in any top 10 |");
            report.AppendLine("|---|" + string.Concat(Enumerable.Repeat("---:|", results.Count + diagnostics.Count)) + "---:|");

            var queries = results[0].Cultures.Single(score => score.Culture == culture).Queries.Select(query => query.Query);

            var rows = queries
                .Where(query => !QueryKinds.IsNoAnswer(byStrategy[results[0].Strategy][query].Kind))
                .Select(query => (
                    Query: query,
                    Scores: results.Select(result => byStrategy[result.Strategy][query]).ToList(),
                    Unjudged: results
                        .SelectMany(result => byStrategy[result.Strategy][query].Unjudged)
                        .Select(hit => hit.ExternalId)
                        .Distinct(StringComparer.Ordinal)
                        .Count()))
                .OrderBy(row => row.Scores.Min(score => score.JudgedAt10 ?? 1))
                .ThenBy(row => row.Query, StringComparer.Ordinal);

            foreach (var row in rows)
            {
                var judged = string.Join(" | ", row.Scores.Select(score => Format(score.JudgedAt10)));
                var diagnosticNdcg = string.Concat(diagnostics.Select(result => $" | {Format(byStrategy[result.Strategy][row.Query].NdcgAt10)}"));

                report.AppendLine(CultureInfo.InvariantCulture, $"| {row.Query} | {judged}{diagnosticNdcg} | {row.Unjudged} |");
            }
        }
    }

    /// <summary>
    /// The queries nothing in the catalogue answers — a product the shop does not
    /// sell, or a question that is not about a product. There is no NDCG to give
    /// them. `admitted` — whether the page says so — is the headline now (ADR
    /// 0032); `shown@10` is a diagnostic kept beside it: how many results a
    /// strategy put on the first page anyway, and what the first one was. Lexical
    /// search with an OR fallback can match "casa" in house slippers; a vector
    /// side always returns its nearest neighbours, however far away they are.
    /// </summary>
    private static void RenderNoAnswer(StringBuilder report, IReadOnlyList<StrategyScores> results)
    {
        foreach (var culture in results[0].Cultures.Select(score => score.Culture))
        {
            var byStrategy = results.ToDictionary(
                result => result.Strategy,
                result => result.Cultures.Single(score => score.Culture == culture).Queries
                    .ToDictionary(query => query.Query, StringComparer.Ordinal),
                StringComparer.Ordinal);

            var queries = results[0].Cultures.Single(score => score.Culture == culture).Queries
                .Where(query => QueryKinds.IsNoAnswer(query.Kind))
                .ToList();

            if (queries.Count == 0)
                continue;

            report.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## {culture}: queries with nothing to find").AppendLine();
            report.AppendLine(
                "Nothing in the catalogue answers these. `admitted` is whether the page says so — the closest things, " +
                "not the shop's, or help for a question about the shop — and it is the headline now. `shown@10` is a " +
                "diagnostic: how many results a strategy put on the first page anyway (lower is better), and the first " +
                "result is what a shopper would read first.").AppendLine();

            report.AppendLine(
                "| query | kind | " + string.Join(" | ", results.Select(result => $"{result.Strategy} shown@10")) + " | " +
                string.Join(" | ", results.Select(result => $"{result.Strategy} admitted")) + " | " +
                string.Join(" | ", results.Select(result => $"{result.Strategy} first result")) + " |");
            report.AppendLine(
                "|---|---|" + string.Concat(Enumerable.Repeat("---:|", results.Count)) +
                string.Concat(Enumerable.Repeat("---|", results.Count)) +
                string.Concat(Enumerable.Repeat("---|", results.Count)));

            foreach (var query in queries)
            {
                var shown = string.Join(" | ", results.Select(result => ShownAt10(byStrategy[result.Strategy][query.Query]).ToString(CultureInfo.InvariantCulture)));
                var admitted = string.Join(" | ", results.Select(result => AdmittedAnswer(byStrategy[result.Strategy][query.Query])));
                var first = string.Join(" | ", results.Select(result => byStrategy[result.Strategy][query.Query].FirstResult ?? "—"));

                report.AppendLine(CultureInfo.InvariantCulture, $"| {query.Query} | {query.Kind} | {shown} | {admitted} | {first} |");
            }

            var means = string.Join(" | ", results.Select(result =>
                queries.Average(query => ShownAt10(byStrategy[result.Strategy][query.Query])).ToString("0.0", CultureInfo.InvariantCulture)));

            report.AppendLine(
                CultureInfo.InvariantCulture,
                $"| **mean** | | {means} |{string.Concat(Enumerable.Repeat(" |", results.Count))}{string.Concat(Enumerable.Repeat(" |", results.Count))}");

            var admittedRatios = string.Join(" · ", results.Select(result =>
            {
                var score = result.Cultures.Single(cultureScore => cultureScore.Culture == culture).Admitted;
                return $"{result.Strategy} {(score is { } value ? value.ToString("0.00", CultureInfo.InvariantCulture) : "n/a")}";
            }));

            report.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"**admitted**: {admittedRatios}");
        }
    }

    /// <summary>
    /// What a query with nothing to find was told, in the report's own words —
    /// <c>Related</c> or <c>OutOfScope</c> for the closest-things answer, a help
    /// topic for a non-product question, and an em dash when nothing admitted it.
    /// </summary>
    private static string AdmittedAnswer(QueryScore query) => query switch
    {
        { Match: SearchMatch.Related } => "Related",
        { Match: SearchMatch.OutOfScope } => "OutOfScope",
        { Kind: QueryKinds.NonProduct, HelpTopic: not null } => $"help: {query.HelpTopic}",
        _ => "—"
    };

    private static int ShownAt10(QueryScore query) => Math.Min(RelevanceEvaluator.NdcgCutoff, query.Returned);

    private static string Format(double? value) =>
        value is null ? "n/a" : value.Value.ToString("0.000", CultureInfo.InvariantCulture);
}
