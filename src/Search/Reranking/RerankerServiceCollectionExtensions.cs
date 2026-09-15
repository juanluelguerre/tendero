using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Embeddings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Search.Reranking;

public static class RerankerServiceCollectionExtensions
{
    /// <summary>Where the AppHost hands over the reranker's address (<c>WithReference</c> on its endpoint).</summary>
    public const string EndpointKey = "services:reranker:http:0";

    /// <summary>The reranker, only when the AppHost started one.</summary>
    public static IServiceCollection AddRerankerIfConfigured(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration[EndpointKey] is { Length: > 0 } endpoint
            ? services.AddReranker(new Uri(endpoint))
            : services;
    }

    public static IServiceCollection AddReranker(this IServiceCollection services, Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        // Its own client, like the model server's probe: the factory's resilience
        // handler retries for thirty seconds, and the budget the strategy gives a
        // rerank is under one.
        services.AddSingleton<IReranker>(new TextEmbeddingsInferenceReranker(
            new HttpClient { BaseAddress = new Uri(endpoint, "/"), Timeout = TimeSpan.FromSeconds(10) },
            RerankerModel.BgeRerankerV2M3));

        return services;
    }
}

/// <summary>
/// The reranking model, pinned to the revision whose licence was checked (ADR 0031):
/// <c>bge-reranker-v2-m3</c>, Apache-2.0. It has no ONNX export at that revision, so
/// TEI serves it with its candle backend — 10 to 12 seconds for twenty pairs on a CPU,
/// which is why it only starts where there is a GPU.
/// </summary>
internal sealed record RerankerModel(string ModelId, string Revision)
{
    public static readonly RerankerModel BgeRerankerV2M3 = new("BAAI/bge-reranker-v2-m3", "953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e");
}

/// <summary>
/// Text Embeddings Inference's <c>/rerank</c>: a query and texts in, one score per
/// text out, in no particular order.
///
/// The revision is checked once, against <c>/info</c>, before the first score is
/// trusted. A different reranker would not corrupt anything the way a different
/// embedding model mixes a vector space — it would only order differently — but
/// the gate's numbers were measured with this one, and a reranker nobody measured
/// is a relevance change nobody reviewed.
/// </summary>
internal sealed class TextEmbeddingsInferenceReranker(HttpClient http, RerankerModel model) : IReranker
{
    private readonly ModelServerInfo info = new(http);
    private volatile bool verified;

    public async Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0)
            return [];

        try
        {
            if (!this.verified)
                await VerifyAsync(ct);

            using var response = await http.PostAsJsonAsync(
                new Uri("rerank", UriKind.Relative), new RerankRequest(query, texts), ct);
            response.EnsureSuccessStatusCode();

            var ranks = await response.Content.ReadFromJsonAsync<List<Rank>>(ct) ?? [];
            var scores = new double?[texts.Count];

            foreach (var rank in ranks.Where(rank => rank.Index >= 0 && rank.Index < texts.Count))
                scores[rank.Index] = rank.Score;

            return scores.All(score => score.HasValue)
                ? [.. scores.Select(score => score!.Value)]
                : throw new SearchUnavailableException("reranking", $"{ranks.Count} scores came back for {texts.Count} texts");
        }
        catch (Exception exception) when (exception is not (SearchUnavailableException or OperationCanceledException))
        {
            throw new SearchUnavailableException("reranking", $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private async Task VerifyAsync(CancellationToken ct)
    {
        var served = await this.info.ReadAsync(ct);

        if (!string.Equals(served.ModelId, model.ModelId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(served.Revision, model.Revision, StringComparison.OrdinalIgnoreCase))
        {
            throw new SearchUnavailableException(
                "reranking",
                $"the reranker serves {served.ModelId}@{served.Revision}, and this build was measured with {model.ModelId}@{model.Revision}");
        }

        this.verified = true;
    }

    /// <summary>Truncation on, because a long description is cut rather than refused; scores normalised to 0–1.</summary>
    private sealed record RerankRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("texts")] IReadOnlyList<string> Texts)
    {
        [JsonPropertyName("truncate")] public bool Truncate => true;
        [JsonPropertyName("raw_scores")] public bool RawScores => false;
        [JsonPropertyName("return_text")] public bool ReturnText => false;
    }

    private sealed record Rank(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("score")] double Score);
}
