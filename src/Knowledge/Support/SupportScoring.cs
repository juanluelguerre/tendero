using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ElGuerre.Tendero.Knowledge.Support;

/// <summary>
/// Does this text say this claim? A number from 0 to 1 for <c>Evidence.Support</c>
/// (roadmap B4), the one figure the review screen ranks by.
///
/// Code proves a quote <em>exists</em>; it cannot prove the quote <em>says</em> the
/// claim — <em>"no apta para inducción"</em> exists and contradicts <em>"apta para
/// inducción"</em>. P10-0 measured candidates for this port side by side.
/// </summary>
public interface ISupportScorer
{
    Task<double> ScoreAsync(string text, string claim, CancellationToken cancellationToken);
}

/// <summary>The keys the scorers are registered under.</summary>
public static class SupportScorers
{
    /// <summary>A textual-entailment model: P(the text entails the claim).</summary>
    public const string Nli = "nli";

    /// <summary>The search reranker: how related, which is not whether it is said. Kept as the counter-example.</summary>
    public const string Reranker = "reranker";

    /// <summary>The generative model asked yes or no, read by the probability of its answer.</summary>
    public const string YesNo = "yesno";
}

/// <summary>
/// An NLI classifier served by Text Embeddings Inference (<c>/predict</c>), the
/// model server the stack already runs — so the candidate needs no new runtime.
/// </summary>
public sealed class TeiNliSupportScorer(HttpClient http) : ISupportScorer
{
    public async Task<double> ScoreAsync(string text, string claim, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("predict", new { inputs = new[] { new[] { text, claim } } }, cancellationToken);
        response.EnsureSuccessStatusCode();

        // One pair answers with one list of labels; TEI wraps it in another for a batch.
        var labels = await response.Content.ReadFromJsonAsync<Prediction[][]>(cancellationToken) ?? [];
        return labels.SelectMany(pair => pair).First(label => label.Label == "entailment").Score;
    }

    private sealed record Prediction([property: JsonPropertyName("label")] string Label, [property: JsonPropertyName("score")] double Score);
}

/// <summary>
/// The search reranker, served by Text Embeddings Inference (<c>/rerank</c>). It
/// answers <em>how related</em>, and a contradiction is very related: measured as
/// the counter-example, never meant to be adopted.
/// </summary>
public sealed class TeiRerankerSupportScorer(HttpClient http) : ISupportScorer
{
    public async Task<double> ScoreAsync(string text, string claim, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("rerank", new { query = claim, texts = new[] { text } }, cancellationToken);
        response.EnsureSuccessStatusCode();

        var ranks = await response.Content.ReadFromJsonAsync<Rank[]>(cancellationToken) ?? [];
        return ranks.Single().Score;
    }

    private sealed record Rank([property: JsonPropertyName("index")] int Index, [property: JsonPropertyName("score")] double Score);
}
