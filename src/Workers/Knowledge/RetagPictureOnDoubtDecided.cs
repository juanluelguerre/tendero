using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.SetImageShows;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Workers.Knowledge;

/// <summary>
/// A person decided a doubted picture *belongs to another variant*: Catalog is
/// asked to have the picture say the colour that was seen (ADR 0057; the
/// author's ruling, 2026-10-08). Knowledge never writes to Catalog (ADR 0018) —
/// it raises the decision with its values, this handler turns them into
/// Catalog's own command, and Catalog decides whether a variant still sold holds
/// that colour. What the picture shows on its other axes stays: the doubt was
/// about one of them.
///
/// The other two decisions ask nothing of Catalog. *The picture is fine* is a
/// person saying the two colours are one to the shop; *replace it* is work for
/// whoever draws the next one.
///
/// As with an approved claim, a failure is let through for the outbox to retry —
/// a person's decision deserves that — and a refusal is not a failure: it is
/// Catalog saying no variant is that colour, counted and logged, not retried.
/// </summary>
public sealed partial class RetagPictureOnDoubtDecided(
    ICommandDispatcher commands,
    ILogger<RetagPictureOnDoubtDecided> logger) : IDomainEventHandler<PictureDoubtDecided>
{
    private static readonly Meter Meter = new(TelemetrySources.Knowledge);

    private static readonly Counter<long> Retagged = Meter.CreateCounter<long>(
        "tendero.knowledge.picture_doubts.retagged", unit: "{picture}",
        description: "Pictures Catalog was asked to file under another variant after a doubt was decided, by what it answered.");

    public async Task HandleAsync(PictureDoubtDecided domainEvent, CancellationToken ct)
    {
        // An event stored before it carried the picture has nothing to act on.
        if (domainEvent is not { Decision: PictureDoubtDecision.BelongsToAnotherVariant, PictureId: { } picture, AttributeCode: { } axis, Seen: { } seen })
            return;

        var result = await commands.SendAsync(new SetImageShowsCommand(
            domainEvent.ProductId, new ImageId(picture), new Dictionary<string, string> { [axis] = seen }, KeepOtherAxes: true), ct);

        Retagged.Add(1, new KeyValuePair<string, object?>("outcome", MetricLabels.Of(result.Outcome)));
        if (result.Outcome is not (ImageShowsOutcome.Shown or ImageShowsOutcome.Unchanged))
            LogRefused(logger, domainEvent.DoubtId.Value, picture, axis, seen, result.Outcome);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catalog did not file picture {PictureId} of doubt {DoubtId} under {Axis} = {Seen} ({Outcome})")]
    private static partial void LogRefused(ILogger logger, Guid doubtId, string pictureId, string axis, string seen, ImageShowsOutcome outcome);
}
