using System.Globalization;
using System.Text;

namespace ElGuerre.Tendero.SearchEval;

/// <summary>
/// The report is read in a console and published as a CI artefact, so it comes
/// out as Markdown: pasting it into a PR has to be readable without tools.
/// </summary>
public static class EvaluationReport
{
    public static string Render(IReadOnlyList<CultureScore> scores, EvaluationThresholds thresholds)
    {
        var report = new StringBuilder();
        report.AppendLine("# Search relevance report").AppendLine();

        report.AppendLine("| culture | queries | NDCG@10 | threshold | recall@50 | threshold | |");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---|");

        foreach (var score in scores)
        {
            var limit = thresholds.For(score.Culture);
            var passed = Passes(score, limit);

            report.AppendLine(
                String.Create(
                    CultureInfo.InvariantCulture,
                    $"| {score.Culture} | {score.ScoredQueries} | {score.MeanNdcgAt10:0.000} | {limit.NdcgAt10:0.000} " +
                    $"| {score.MeanRecallAt50:0.000} | {limit.RecallAt50:0.000} | {(passed ? "pass" : "FAIL")} |"));
        }

        foreach (var score in scores)
        {
            report.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## {score.Culture}").AppendLine();
            report.AppendLine("| query | NDCG@10 | recall@50 | hits |");
            report.AppendLine("|---|---:|---:|---:|");

            // Worst first: the report has to start with what needs fixing.
            foreach (var query in score.Queries.OrderBy(query => query.NdcgAt10 ?? Double.MaxValue))
            {
                report.AppendLine(
                    String.Create(
                        CultureInfo.InvariantCulture,
                        $"| {query.Query} | {Format(query.NdcgAt10)} | {Format(query.RecallAt50)} | {query.Returned} |"));
            }
        }

        return report.ToString();
    }

    public static bool Passes(CultureScore score, CultureThresholds thresholds) =>
        score.MeanNdcgAt10 >= thresholds.NdcgAt10 && score.MeanRecallAt50 >= thresholds.RecallAt50;

    private static string Format(double? value) =>
        value is null ? "n/a" : value.Value.ToString("0.000", CultureInfo.InvariantCulture);
}
