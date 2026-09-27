using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.Knowledge.Features.ProposeClaims;

/// <summary>
/// Works through the descriptions waiting for claims, one product at a time, in
/// the worker and off the outbox: at ~30 s a product on a CPU (P10-0), a model
/// inside the outbox would hold every projection and the stock saga behind it.
///
/// A product that fails is retried on the next pass, up to
/// <see cref="Domain.SourceDocument.MaxAttempts"/>; then it stops, counted, rather
/// than spend the model on a text it cannot read.
/// </summary>
public sealed partial class ClaimProposer(
    IServiceScopeFactory scopes,
    ILogger<ClaimProposer> logger,
    TimeProvider clock) : BackgroundService
{
    /// <summary>How long an empty queue waits before it looks again.</summary>
    public static readonly TimeSpan Idle = TimeSpan.FromSeconds(15);

    private static readonly Meter Meter = new(TelemetrySources.Knowledge);

    private static readonly Counter<long> Failed = Meter.CreateCounter<long>(
        "tendero.knowledge.proposals.failed", unit: "{attempt}", description: "Proposal runs that failed on a product; retried until its limit.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                worked = await ProposeNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The queue itself not answering — Postgres down — is logged and
                // waited out; the worker keeps running its other jobs.
                LogQueueUnavailable(logger, exception);
            }

            if (!worked)
                await Task.Delay(Idle, clock, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    /// <summary>Proposes claims for the oldest waiting product; false when none waits.</summary>
    public async Task<bool> ProposeNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var knowledge = scope.ServiceProvider.GetRequiredService<IKnowledgeRepository>();

        var waiting = await knowledge.WaitingAsync(1, cancellationToken);
        if (waiting.Count == 0)
            return false;

        var product = waiting[0];
        try
        {
            var result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(new ProposeClaimsCommand(product), cancellationToken);
            LogProposed(logger, result.Proposed, result.Dropped.Count, product, result.Extractor);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Failed.Add(1);
            LogFailed(logger, exception, product);

            // A fresh scope: the failed one may hold half-tracked claims.
            await using var retry = scopes.CreateAsyncScope();
            var fresh = retry.ServiceProvider.GetRequiredService<IKnowledgeRepository>();
            foreach (var document in await fresh.CurrentDocumentsAsync(product, cancellationToken))
                if (document.IsWaiting)
                    document.RecordFailedAttempt();
            await retry.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Proposed {Proposed} claims ({Dropped} refused) for product {ProductId} with {Extractor}")]
    private static partial void LogProposed(ILogger logger, int proposed, int dropped, ProductId productId, string extractor);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Proposing claims for product {ProductId} failed; it is retried until its limit")]
    private static partial void LogFailed(ILogger logger, Exception exception, ProductId productId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The claims queue could not be read; trying again shortly")]
    private static partial void LogQueueUnavailable(ILogger logger, Exception exception);
}
