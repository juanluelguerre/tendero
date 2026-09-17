using System.Diagnostics;
using System.Text.Json;
using ElGuerre.Tendero.Search.Contracts;

namespace ElGuerre.Tendero.Search.Evaluation;

/// <summary>
/// The committed thresholds, per strategy and per culture. Lowering them requires
/// a justification in the PR body (docs/search-evaluation.md): it is the only
/// defence against "move the bar until it passes".
///
/// **Per strategy**, since hybrid search exists (P8-10): hybrid is held to its own
/// bar, and lexical still has to clear its own — a hybrid that won by making the
/// fallback worse would have made the mode every AI failure lands in worse.
/// </summary>
public sealed record EvaluationThresholds(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, CultureThresholds>> ByStrategy)
{
    public static async Task<EvaluationThresholds> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await LoadAsync(stream, path, cancellationToken);
    }

    public static async Task<EvaluationThresholds> LoadAsync(Stream stream, string origin, CancellationToken cancellationToken)
    {
        var strategies = await JsonSerializer.DeserializeAsync<Dictionary<string, Dictionary<string, CultureThresholds>>>(
                             stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken)
                         ?? throw new InvalidDataException($"'{origin}' is empty or not a thresholds file.");

        return new EvaluationThresholds(strategies.ToDictionary(
            strategy => strategy.Key,
            strategy => (IReadOnlyDictionary<string, CultureThresholds>)strategy.Value,
            StringComparer.OrdinalIgnoreCase));
    }

    public CultureThresholds For(string strategy, string culture) =>
        ByStrategy.TryGetValue(strategy, out var cultures) && cultures.TryGetValue(culture, out var thresholds)
            ? thresholds
            : throw new InvalidDataException($"No committed thresholds for the {strategy} strategy in culture '{culture}'.");
}

public sealed record CultureThresholds(double NdcgAt10, double RecallAt50)
{
    public bool AreMetBy(CultureScore score) =>
        score.MeanNdcgAt10 >= NdcgAt10 && score.MeanRecallAt50 >= RecallAt50;
}

/// <summary>
/// A result in the first ten that the golden set has no judgment for. It counts as
/// irrelevant in the score, and it is a candidate for the golden set rather than a
/// miss — which of the two it really is, only a person reading it can say.
/// </summary>
public sealed record UnjudgedHit(int Position, string ExternalId, string Name);

public sealed record QueryScore(
    string Query,
    double? NdcgAt10,
    double? RecallAt50,
    int Returned,
    double Milliseconds,
    double? JudgedAt10,
    IReadOnlyList<UnjudgedHit> Unjudged,
    string? Kind = null,
    string? Split = null,
    string? FirstResult = null,
    SearchMatch? Match = null,
    string? HelpTopic = null);

public sealed record CultureScore(string Culture, IReadOnlyList<QueryScore> Queries)
{
    // The mean over the scoreable queries. A query with no relevant judgments
    // does not score: counting it as 0 would punish the engine for a half-written
    // annotation.
    public double MeanNdcgAt10 => Mean(Queries.Select(query => query.NdcgAt10));
    public double MeanRecallAt50 => Mean(Queries.Select(query => query.RecallAt50));

    /// <summary>How much of what this strategy put in its first ten the ruler can see at all.</summary>
    public double MeanJudgedAt10 => Mean(Queries.Select(query => query.JudgedAt10));
    public int ScoredQueries => Queries.Count(query => query.NdcgAt10 is not null);
    public double MeanMilliseconds => Queries.Count == 0 ? 0 : Queries.Average(query => query.Milliseconds);

    /// <summary>
    /// For queries with nothing to find: the share whose page admits it — the closest
    /// things, not the shop's, or help for a question about the shop. Null when no
    /// answerer was asked, so a run without one claims nothing.
    /// </summary>
    public double? Admitted
    {
        get
        {
            var noAnswer = Queries.Where(query => QueryKinds.IsNoAnswer(query.Kind)).ToList();
            if (noAnswer.Count == 0 || noAnswer.All(query => query.Match is null))
                return null;

            return noAnswer.Count(query =>
                    query.Match is SearchMatch.Related
                    || (query.Kind == QueryKinds.NonProduct && query.HelpTopic is not null))
                / (double)noAnswer.Count;
        }
    }

    /// <summary>For scored queries: how often one with real answers is told nothing matches exactly. Reported, not gated.</summary>
    public double NoticeRate =>
        ScoredQueries == 0 ? 0 : Queries.Count(query => query.NdcgAt10 is not null && query.Match == SearchMatch.Related) / (double)ScoredQueries;

    private static double Mean(IEnumerable<double?> values)
    {
        var scored = values.OfType<double>().ToList();
        return scored.Count == 0 ? 0d : scored.Average();
    }
}

/// <summary>
/// Runs a golden set through a strategy and scores what came back. The same code
/// for the CI gate and for the backoffice panel, which is the reason it is here.
///
/// <paramref name="answerer"/> is optional so a caller that never built layer A —
/// or a test that does not care about it — claims nothing: <see cref="CultureScore.Admitted"/>
/// stays null rather than reading as "the shop admits everything".
/// </summary>
public sealed class RelevanceEvaluator(ISearchStrategy strategy, ISearchAnswerer? answerer = null)
{
    public const int NdcgCutoff = 10;
    public const int RecallCutoff = 50;

    public async Task<CultureScore> RunAsync(
        GoldenSet golden,
        IReadOnlyDictionary<string, string> externalIdByProductId,
        CancellationToken cancellationToken)
    {
        var scores = new List<QueryScore>(golden.Queries.Count);

        foreach (var goldenQuery in golden.Queries)
        {
            var searchQuery = new ProductSearchQuery(goldenQuery.Query, golden.Culture, Page: 1, PageSize: RecallCutoff);

            var stopwatch = Stopwatch.StartNew();
            var page = await strategy.SearchAsync(searchQuery, cancellationToken);
            stopwatch.Stop();

            // The answer is not search latency, so it is asked after the stopwatch
            // stops: layer A's own cost must never be blamed on the strategy.
            var answer = answerer is null ? null : await answerer.AnswerAsync(searchQuery, cancellationToken);

            // The index returns internal ids; the golden set annotates source ids.
            // A hit from no known source cannot be judged either way and is left out.
            var sourced = page.Hits
                .Select(hit => (Hit: hit, ExternalId: externalIdByProductId.GetValueOrDefault(hit.ProductId)))
                .Where(item => item.ExternalId is not null)
                .Select(item => (item.Hit, ExternalId: item.ExternalId!))
                .ToList();

            var ranked = sourced.Select(item => item.ExternalId).ToList();
            var judgments = goldenQuery.ToRelevanceMap();

            // A query nothing can answer has nothing to judge: what it shows is not
            // a candidate for the golden set, it is the failure being measured.
            var noAnswer = QueryKinds.IsNoAnswer(goldenQuery.Kind);

            IReadOnlyList<UnjudgedHit> unjudged = noAnswer
                ? []
                :
                [
                    .. sourced
                        .Take(NdcgCutoff)
                        .Select((item, index) => new UnjudgedHit(index + 1, item.ExternalId, item.Hit.Name))
                        .Where(hit => !judgments.ContainsKey(hit.ExternalId))
                ];

            scores.Add(new QueryScore(
                goldenQuery.Query,
                RelevanceMetrics.NdcgAt(NdcgCutoff, ranked, judgments),
                RelevanceMetrics.RecallAt(RecallCutoff, ranked, judgments),
                ranked.Count,
                stopwatch.Elapsed.TotalMilliseconds,
                noAnswer ? null : RelevanceMetrics.JudgedAt(NdcgCutoff, ranked, judgments),
                unjudged,
                goldenQuery.Kind,
                goldenQuery.Split,
                sourced.FirstOrDefault().Hit?.Name,
                answer?.Match,
                answer?.HelpTopic));
        }

        return new CultureScore(golden.Culture, scores);
    }
}
