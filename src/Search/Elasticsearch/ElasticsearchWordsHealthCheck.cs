using Elastic.Clients.Elasticsearch;
using ElGuerre.Tendero.Search.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ElGuerre.Tendero.Search.Elasticsearch;

/// <summary>
/// Words, the first stage of search, as the shopkeeper's status reads it (R-9,
/// D-7): the index answers a ping or it does not. Inside the adapter, because the
/// Elasticsearch client never leaves this folder — the day the index is replaced,
/// this check goes with it.
/// </summary>
internal sealed class ElasticsearchWordsHealthCheck(ElasticsearchClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var ping = await client.PingAsync(cancellationToken);
            return ping.IsValidResponse
                ? SearchHealth.Result(StageState.Up)
                : SearchHealth.Result(StageState.Down, "the search index does not answer");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return SearchHealth.Result(StageState.Down, "the search index does not answer");
        }
    }
}
