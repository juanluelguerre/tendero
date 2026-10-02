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
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    LogLost(logger, failure, batch.Count);
                }

                batch.Clear();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lost {Count} model-call rows")]
    private static partial void LogLost(ILogger logger, Exception failure, int count);
}
