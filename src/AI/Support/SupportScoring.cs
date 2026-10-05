using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElGuerre.Tendero.AI.Support;

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

/// <summary>
/// A scorer that answers the same for every pair — for tests only, as the Scripted
/// model is (phase 12b): the browser spec runs Tino in CI, where no model fits, and
/// what it proves is the wiring. What a real score does to an answer is measured
/// (<c>docs/analysis/tino-models.md</c>) and unit-tested with scores of its own.
/// </summary>
public sealed class FixedSupportScorer(double support) : ISupportScorer
{
    public Task<double> ScoreAsync(string text, string claim, CancellationToken cancellationToken) => Task.FromResult(support);
}

public static class SupportServiceCollectionExtensions
{
    /// <summary>Where the AppHost says the scorer's container answers, when it started it.</summary>
    public const string EndpointKey = "services:support:http:0";

    /// <summary>
    /// The support scorer, for whoever in this host needs it — the claims pipeline
    /// (Knowledge) and the shopper's assistant (Assist) — registered once and only
    /// when the AppHost handed its address over: an address nothing answers would
    /// fail every call, no address at all is no scorer, which each caller has an
    /// answer for.
    /// </summary>
    /// <summary>A fixed score instead of a scorer: set only by a host that runs a scripted model.</summary>
    public const string ScriptedKey = "Support:Scripted";

    public static IServiceCollection AddSupportScorer(this IServiceCollection services, IConfiguration configuration)
    {
        // Only where there is no scorer to ask: an address always wins.
        if (configuration[EndpointKey] is not { Length: > 0 }
            && double.TryParse(configuration[ScriptedKey], System.Globalization.CultureInfo.InvariantCulture, out var scripted))
            services.TryAddSingleton<ISupportScorer>(new FixedSupportScorer(scripted));

        if (configuration[EndpointKey] is { Length: > 0 } support)
        {
            // Its own HttpClient, not the factory's: the default resilience handler
            // retries for thirty seconds, and whoever waits on a scorer that is down
            // should hear so, not wait.
            services.TryAddSingleton<ISupportScorer>(new TeiNliSupportScorer(new HttpClient
            {
                BaseAddress = new Uri(new Uri(support), "/"),
                Timeout = TimeSpan.FromSeconds(30),
            }));
        }

        return services;
    }
}
