using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Catalog.Features.SetProductAttribute;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Workers.Knowledge;

/// <summary>
/// A person approved a typed claim: Catalog is asked to hold its value (P10-6).
/// Knowledge never writes to Catalog (ADR 0018) — it raises the approval, this
/// handler turns the event's values into Catalog's own command, and Catalog
/// decides whether the value is one its definition accepts. From there the
/// ordinary path does the rest: the product's <c>ProductUpserted</c> reindexes it,
/// and the value reaches the searchable text and the facets like any imported one.
///
/// An untyped claim — a statement with no attribute — changes nothing here: it
/// stays in Knowledge, for the comparison and the product page to read.
///
/// Unlike the description recorder it lets a failure through: the outbox then
/// retries the message, which is what a person's decision deserves. A refusal is
/// not a failure — it is Catalog saying no — and is counted, not retried.
/// </summary>
public sealed partial class ApplyApprovedClaimToCatalog(
    ICommandDispatcher commands,
    ILogger<ApplyApprovedClaimToCatalog> logger) : IDomainEventHandler<ClaimApproved>
{
    private static readonly Meter Meter = new(TelemetrySources.Knowledge);

    private static readonly Counter<long> Applied = Meter.CreateCounter<long>(
        "tendero.knowledge.claims.applied", unit: "{claim}", description: "Approved typed claims Catalog was asked to hold, by what it answered.");

    public async Task HandleAsync(ClaimApproved domainEvent, CancellationToken ct)
    {
        if (domainEvent is not { AttributeCode: { } code, Value: { } value })
            return;

        var result = await commands.SendAsync(new SetProductAttributeCommand(
            domainEvent.ProductId, code, value, $"claim {domainEvent.ClaimId} approved by {domainEvent.ApprovedBy}"), ct);

        Applied.Add(1, new KeyValuePair<string, object?>("outcome", MetricLabels.Of(result.Outcome)));
        if (result.Outcome is not (SetAttributeOutcome.Set or SetAttributeOutcome.Unchanged))
            LogRefused(logger, domainEvent.ClaimId.Value, code, value, result.Outcome);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catalog refused approved claim {ClaimId}: {AttributeCode} = {Value} ({Outcome})")]
    private static partial void LogRefused(ILogger logger, Guid claimId, string attributeCode, string value, SetAttributeOutcome outcome);
}
