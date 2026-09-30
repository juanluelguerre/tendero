using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Elasticsearch;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ElGuerre.Tendero.Search.Health;

/// <summary>
/// One health check per stage of search, each through the port that stage already
/// has (R-9, D-7; no new dependency), and a publisher that keeps each stage's last
/// change for the shopkeeper's status. The checks carry <see cref="HealthTags.Private"/>:
/// the open <c>/health</c> leaves them out, because a stage switched off is a
/// search that degrades to words (invariant 8), not an API that is unhealthy.
/// </summary>
public static class SearchHealth
{
    /// <summary>The key of a check's data that carries its <see cref="StageState"/>.</summary>
    public const string StateKey = "state";

    internal static readonly IReadOnlyDictionary<string, SearchStage> Checks = new Dictionary<string, SearchStage>
    {
        ["search-words"] = SearchStage.Words,
        ["search-meaning"] = SearchStage.Meaning,
        ["search-reranking"] = SearchStage.Reranking,
    };

    public static IServiceCollection AddSearchStatus(this IServiceCollection services)
    {
        services.AddSingleton<SearchStatusMemory>();
        services.AddSingleton<IHealthCheckPublisher, SearchStatusPublisher>();
        services.Configure<HealthCheckPublisherOptions>(options =>
        {
            options.Delay = TimeSpan.FromSeconds(5);
            options.Period = TimeSpan.FromSeconds(30);
        });

        string[] tags = [HealthTags.Private, HealthTags.Search];
        services.AddHealthChecks()
            .AddCheck<ElasticsearchWordsHealthCheck>("search-words", tags: tags)
            .AddCheck<MeaningHealthCheck>("search-meaning", tags: tags)
            .AddCheck<RerankingHealthCheck>("search-reranking", tags: tags);

        return services;
    }

    internal static HealthCheckResult Result(StageState state, string? reason = null)
    {
        var data = new Dictionary<string, object> { [StateKey] = state.ToString() };
        return state switch
        {
            StageState.Up => HealthCheckResult.Healthy(reason, data),
            StageState.Off => HealthCheckResult.Degraded(reason, data: data),
            _ => HealthCheckResult.Unhealthy(reason, data: data),
        };
    }
}

/// <summary>Meaning: the vector layer's own check — the model server and the vector store together.</summary>
internal sealed class MeaningHealthCheck(IVectorLayer layer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = await layer.CheckAsync(cancellationToken);
        return !status.IsConfigured ? SearchHealth.Result(StageState.Off, status.Reason)
            : status.IsAvailable ? SearchHealth.Result(StageState.Up)
            : SearchHealth.Result(StageState.Down, status.Reason);
    }
}

/// <summary>Reranking: one pair scored, the smallest question the reranker can answer.</summary>
internal sealed class RerankingHealthCheck(IReranker reranker) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!reranker.IsConfigured)
            return SearchHealth.Result(StageState.Off, "this process was started without a reranker");

        try
        {
            await reranker.ScoreAsync("health", ["health"], cancellationToken);
            return SearchHealth.Result(StageState.Up);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return SearchHealth.Result(StageState.Down, "the reranker does not answer");
        }
    }
}

/// <summary>Keeps each stage's state from every round of checks; only a change moves its time.</summary>
internal sealed class SearchStatusPublisher(SearchStatusMemory memory, TimeProvider clock) : IHealthCheckPublisher
{
    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var (name, entry) in report.Entries)
        {
            if (!SearchHealth.Checks.TryGetValue(name, out var stage)) continue;
            var state = entry.Data.TryGetValue(SearchHealth.StateKey, out var value) && Enum.TryParse<StageState>(value as string, out var parsed)
                ? parsed
                : StageState.Down;
            memory.Record(stage, state, entry.Description, now);
        }

        return Task.CompletedTask;
    }
}
