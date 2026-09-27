using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Knowledge.Features.RecordDescription;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Workers.Knowledge;

// Where Catalog and Knowledge meet, and why it is here. They are contexts that
// share no entities (ADR 0014): Knowledge never reads a Product or an
// AttributeDefinition. The worker composes both, so it is the one place that
// turns Catalog's facts into the values Knowledge takes — a description as text,
// an attribute as a spec — and nothing but values crosses.

/// <summary>
/// A product was saved: its description, per culture, is recorded as Knowledge's
/// source document. Milliseconds, on the outbox; the model runs later, off it.
///
/// Like the vector projection, it never fails the message: the outbox retries a
/// whole message when one handler throws, and the lexical index must not be
/// replayed five times because the claims layer stumbled. A failure is logged
/// and counted, and costs a description recorded late, at the product's next save.
/// </summary>
public sealed partial class RecordDescriptionOnProductUpserted(
    IProductReader products,
    ICommandDispatcher commands,
    ILogger<RecordDescriptionOnProductUpserted> logger) : IDomainEventHandler<ProductUpserted>
{
    private static readonly string[] Cultures = ["es", "en"];

    private static readonly Meter Meter = new(TelemetrySources.Knowledge);

    private static readonly Counter<long> Failed = Meter.CreateCounter<long>(
        "tendero.knowledge.descriptions.failed", unit: "{change}", description: "Product changes whose description Knowledge could not record; the next save records it.");

    public async Task HandleAsync(ProductUpserted domainEvent, CancellationToken ct)
    {
        // A product with no description has nothing to claim.
        var product = await products.GetByIdAsync(domainEvent.ProductId, ct);
        if (product?.Description is not { } description)
            return;

        try
        {
            foreach (var culture in Cultures)
            {
                if (description.Values.TryGetValue(culture, out var text) && !string.IsNullOrWhiteSpace(text))
                    await commands.SendAsync(new RecordDescriptionCommand(product.Id, culture, text), ct);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Failed.Add(1);
            LogFailed(logger, exception, product.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not record the description of product {ProductId} for claims; its next save will")]
    private static partial void LogFailed(ILogger logger, Exception exception, ProductId productId);
}

/// <summary>
/// The catalogue that was there before the claims layer. The worker records
/// descriptions when a product changes; a catalogue imported before phase 10 —
/// or before this worker last ran — would never be read. So at start every
/// product's description is recorded once: a hundred commands of milliseconds,
/// and idempotent, because an unchanged text is nothing (its hash). Search does
/// the same for its index at start.
/// </summary>
public sealed partial class RecordEveryDescriptionAtStart(
    IServiceScopeFactory scopes,
    ILogger<RecordEveryDescriptionAtStart> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var products = scope.ServiceProvider.GetRequiredService<IProductReader>();
            var commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            var recorded = 0;

            await foreach (var product in products.StreamAllAsync(stoppingToken))
            {
                if (product.Description is not { } description)
                    continue;

                foreach (var (culture, text) in description.Values)
                {
                    if (string.IsNullOrWhiteSpace(text))
                        continue;
                    var result = await commands.SendAsync(new RecordDescriptionCommand(product.Id, culture, text), stoppingToken);
                    if (result.Outcome == RecordDescriptionOutcome.Recorded)
                        recorded++;
                }
            }

            LogRecorded(logger, recorded);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // Not fatal: a product's next save records its description anyway.
            LogFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recorded {Recorded} descriptions for claims at start; the rest were already recorded")]
    private static partial void LogRecorded(ILogger logger, int recorded);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not record the catalogue's descriptions at start; each product's next save will")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

