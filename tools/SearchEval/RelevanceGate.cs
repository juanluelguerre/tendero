using ElGuerre.Tendero.Search.Evaluation;

namespace ElGuerre.Tendero.SearchEval;

/// <summary>
/// What fails the relevance gate, decided apart from the run that measures it so
/// it can be held without an Elasticsearch: a gated strategy under its thresholds,
/// and scores measured against other judgments than the thresholds were set
/// against (2026-10-24). If a pull request changes the code and the golden set
/// together, a moved score cannot say which moved it; the gate refuses to compare
/// rather than guess. A diagnostic strategy has no thresholds and never fails it.
/// </summary>
public static class RelevanceGate
{
    public static IReadOnlyList<string> Failures(
        IReadOnlyList<StrategyScores> results, EvaluationThresholds thresholds, string goldenSet)
    {
        var failures = new List<string>();

        if (thresholds.GoldenSet != goldenSet)
        {
            failures.Add(
                $"golden set {goldenSet}, while the thresholds were set against {thresholds.GoldenSet ?? "an unnamed one"}");
        }

        failures.AddRange(results
            .Where(result => result.Gated)
            .SelectMany(result => result.Cultures
                .Where(score => !thresholds.For(result.Strategy, score.Culture).AreMetBy(score))
                .Select(score => $"{result.Strategy} {score.Culture}")));

        return failures;
    }
}
