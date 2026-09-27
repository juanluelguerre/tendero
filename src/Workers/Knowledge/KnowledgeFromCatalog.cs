using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.Knowledge.Features.RecordDescription;
using ElGuerre.Tendero.Knowledge.Ports;
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
/// Catalog's attribute definitions as the specs a claim may be typed with.
/// The variant axes and the size range come from the import and the variants,
/// not from a description, and are left out — as they were in P10-0's golden set.
/// </summary>
public sealed class CatalogAttributeVocabulary(IAttributeDefinitionReader definitions) : IAttributeVocabulary
{
    private static readonly HashSet<string> NotFromText = new(StringComparer.OrdinalIgnoreCase) { "SIZE_RANGE" };

    public async Task<IReadOnlyList<AttributeSpec>> CurrentAsync(CancellationToken cancellationToken = default)
    {
        var all = await definitions.AllAsync(cancellationToken);
        return
        [
            .. all.All
                .Where(definition => !definition.IsVariantAxis && !NotFromText.Contains(definition.Code))
                .Select(definition => new AttributeSpec(
                    definition.Code,
                    definition.Kind.ToString(),
                    definition.Unit,
                    definition.Label.In("en"),
                    [.. definition.Options.Select(option => option.Code)],
                    definition.Label.In("es"))),
        ];
    }
}
