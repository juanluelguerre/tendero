using System.Globalization;
using System.Text;

namespace ElGuerre.Tendero.SearchEval;

/// <summary>
/// El informe se lee en consola y se publica como artefacto de CI, así que sale
/// en Markdown: pegarlo en un PR tiene que ser legible sin herramientas.
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

            report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {score.Culture} | {score.ScoredQueries} | {score.MeanNdcgAt10:0.000} | {limit.NdcgAt10:0.000} " +
                $"| {score.MeanRecallAt50:0.000} | {limit.RecallAt50:0.000} | {(passed ? "pass" : "FAIL")} |"));
        }

        foreach (var score in scores)
        {
            report.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## {score.Culture}").AppendLine();
            report.AppendLine("| query | NDCG@10 | recall@50 | hits |");
            report.AppendLine("|---|---:|---:|---:|");

            // Peores primero: el informe debe empezar por lo que hay que arreglar.
            foreach (var query in score.Queries.OrderBy(query => query.NdcgAt10 ?? double.MaxValue))
            {
                report.AppendLine(string.Create(CultureInfo.InvariantCulture,
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
