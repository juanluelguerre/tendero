using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElGuerre.Tendero.AI;

/// <summary>
/// Drains the log into the table, a batch at a time, off the request. A batch that
/// cannot be written is logged and dropped — the audit's trade, for a log nobody
/// reads in real time — and the worker goes on with the next one.
/// </summary>
public sealed partial class AIInvocationsWorker(AIInvocationLog log, IServiceScopeFactory scopes, ILogger<AIInvocationsWorker> logger)
    : BackgroundService
{
    private const int BatchSize = 100;

    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AIInvocation>(BatchSize);
        try
        {
            while (await log.Reader.WaitToReadAsync(stoppingToken))
            {
                while (batch.Count < BatchSize && log.Reader.TryRead(out var row))
                    batch.Add(row);

                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IAIInvocationWriter>().WriteAsync(batch, stoppingToken);
                }
                // Anything but the host stopping is one lost batch — a driver's
                // timeout can surface as an OperationCanceledException too.
                catch (Exception failure) when (!stoppingToken.IsCancellationRequested)
                {
                    LogLost(logger, failure, batch.Count);
                }

                batch.Clear();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        // The host is stopping: what is still in the log is written rather than
        // lost — the cost line is the sum of the rows — within a short grace.
        batch.Clear();
        while (log.Reader.TryRead(out var row))
            batch.Add(row);
        if (batch.Count == 0)
            return;

        try
        {
            using var grace = new CancellationTokenSource(DrainGrace);
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IAIInvocationWriter>().WriteAsync(batch, grace.Token);
        }
        catch (Exception failure)
        {
            LogLost(logger, failure, batch.Count);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lost {Count} model-call rows")]
    private static partial void LogLost(ILogger logger, Exception failure, int count);
}
