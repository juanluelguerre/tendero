using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Tendero.Persistence;
using Tendero.Persistence.Outbox;
using Tendero.ServiceDefaults;
using Tendero.SharedKernel;

namespace Tendero.Workers;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int BatchSize { get; set; } = 50;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Tras este número de intentos el mensaje deja de reintentarse y
    /// se queda visible con su error: una cola envenenada no debe girar sola.</summary>
    public int MaxAttempts { get; set; } = 5;
}

/// <summary>
/// Bucle de la bandeja de salida. Deliberadamente simple: un SELECT ordenado por
/// fecha, despacho al handler y marca. Lo que falta (FOR UPDATE SKIP LOCKED para
/// varias réplicas, backoff exponencial) llega cuando haya una segunda réplica,
/// no antes.
/// </summary>
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Outbox);
    private static readonly Meter Metrics = new(TelemetrySources.Outbox);

    /// <summary>El retraso de la outbox es la métrica que dice si la consistencia
    /// eventual sigue siendo "eventual" o ya es "nunca".</summary>
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
                // Un fallo de infraestructura no puede matar el bucle: se registra
                // y se reintenta en el siguiente tick.
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
                message.MarkFailed(exception.Message);
                logger.LogWarning(exception,
                    "Outbox message {MessageId} of type {MessageType} failed (attempt {Attempts})",
                    message.Id, message.Type, message.Attempts);
            }
        }

        // SaveChanges del propio DbContext: las marcas de procesado son el único
        // cambio, y no generan eventos nuevos.
        await context.SaveChangesAsync(cancellationToken);
    }
}
