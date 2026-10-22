using System.Diagnostics;
using ElGuerre.Tendero.Search.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Search;

/// <summary>
/// Asks the hybrid search once in each language when the API starts, so the
/// first shopper is not the one whose search finds the embeddings and the
/// reranker cold (phase 13c). A cold stage does not answer in time, the search
/// degrades to lexical as it should (invariant 8), and lexical ranks
/// differently: the first kit asked of a fresh stack chose the Aroma Compacta,
/// and every kit after it the 6-cup stovetop (2026-10-14).
///
/// It waits for the vector layer to answer, because the models load after the
/// API starts, and gives up after a while: warming is a courtesy, never a
/// condition. In the background, never in the start-up path. A search that
/// fails is logged and costs nothing but the warming. A shop without the vector
/// layer has nothing to warm and does nothing. Registered by the API alone,
/// beside <see cref="Images.WarmVisualSearchAtStartupWorker"/>.
/// </summary>
public sealed class WarmHybridSearchAtStartupWorker(
    IServiceScopeFactory scopes,
    ILogger<WarmHybridSearchAtStartupWorker> logger,
    TimeProvider clock,
    TimeSpan? every = null,
    int attempts = 24) : BackgroundService
{
    /// <summary>A word a shopper of each language types, so each language's analyser and model are the ones warmed.</summary>
    private static readonly (string Culture, string Word)[] Warmed = [("es", "cafetera"), ("en", "coffee maker")];

    private readonly TimeSpan wait = every ?? TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var status = await scope.ServiceProvider.GetRequiredService<IVectorLayer>().CheckAsync(stoppingToken);
            if (!status.IsConfigured)
                return;

            if (status.IsAvailable)
            {
                var search = scope.ServiceProvider.GetRequiredKeyedService<ISearchStrategy>(SearchStrategies.Hybrid);
                foreach (var (culture, word) in Warmed)
                    await this.WarmAsync(search, culture, word, stoppingToken);
                return;
            }

            if (attempt < attempts)
                await Task.Delay(this.wait, clock, stoppingToken);
        }

        logger.LogWarning("The vector layer did not answer at startup; the first searches may be answered lexically");
    }

    private async Task WarmAsync(ISearchStrategy search, string culture, string word, CancellationToken stoppingToken)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            await search.SearchAsync(new ProductSearchQuery(word, culture, Page: 1, PageSize: 1), stoppingToken);
            logger.LogInformation("Hybrid search warmed in {Culture}: {Elapsed:F1}s", culture, stopwatch.Elapsed.TotalSeconds);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Hybrid search could not be warmed in {Culture}", culture);
        }
    }
}
