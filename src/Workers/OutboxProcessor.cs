using System.Diagnostics;
using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.Persistence.Outbox;
using ElGuerre.Tendero.ServiceDefaults;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Workers;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int BatchSize { get; set; } = 50;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>After this many attempts the message stops being retried and
    /// stays visible with its error: a poisoned queue must not spin on its own.</summary>
    public int MaxAttempts { get; set; } = 5;
}

/// <summary>
/// The outbox loop. Deliberately simple: a SELECT ordered by date, dispatch to
/// the handler, mark. What is missing (FOR UPDATE SKIP LOCKED for several
/// replicas, exponential backoff) arrives when there is a second replica, not
/// before.
/// </summary>
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Outbox);
    private static readonly Meter Metrics = new(TelemetrySources.Outbox);

    /// <summary>Outbox lag is the metric that says whether eventual consistency
    /// is still "eventual" or has become "never".</summary>
    private static readonly Histogram<double> LagSeconds = Metrics.CreateHistogram<double>(
        "tendero.outbox.lag", unit: "s", description: "Seconds between an event being raised and processed.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // An infrastructure failure must not kill the loop: it is logged
                // and retried on the next tick.
                logger.LogError(exception, "Outbox batch failed; retrying on the next tick");
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TenderoDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        var pending = await context.OutboxMessages
            .Where(message => message.ProcessedAt == null && message.Attempts < options.Value.MaxAttempts)
            .OrderBy(message => message.OccurredAt)
            .Take(options.Value.BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return;

        using var activity = Telemetry.StartActivity("outbox.batch");
        activity?.SetTag("outbox.batch.size", pending.Count);

        foreach (var message in pending)
        {
            try
            {
                await dispatcher.PublishAsync(
                    DomainEventSerializer.Deserialize(message.Type, message.Payload), cancellationToken);

                message.MarkProcessed();
                LagSeconds.Record((DateTimeOffset.UtcNow - message.OccurredAt).TotalSeconds);
            }
            catch (Exception exception)
            {
                // exception.ToString(), not .Message: "Object reference not set
                // to an instance of an object" with no type and no stack trace
                // diagnoses nothing, and this column is all that is left of a
                // message that exhausted its retries.
                message.MarkFailed(exception.ToString());
                logger.LogWarning(exception,
                    "Outbox message {MessageId} of type {MessageType} failed (attempt {Attempts})",
                    message.Id, message.Type, message.Attempts);
            }
        }

        // The DbContext's own SaveChanges: the processed marks are the only
        // change, and they raise no new events.
        await context.SaveChangesAsync(cancellationToken);
    }
}
