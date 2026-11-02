using System.Diagnostics;
using ElGuerre.Tendero.Channels.Domain;
using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Channels.Features.PublishStockToChannel;

/// <summary>
/// Tell one channel what it may sell of one SKU at one warehouse (phase 16, S3; ADR
/// 0070): Tendero's available there less the margin, never below zero. Sent by the
/// worker whenever the stock moves; idempotent, because what was last told is the
/// expected value of the next compare-and-swap and the same publication twice carries
/// the same key.
/// </summary>
public sealed record PublishStockToChannelCommand(string Channel, string Sku, string Warehouse, int Available)
    : ICommand<StockOutcome>, IUnauditedCommand;

public enum StockOutcome
{
    /// <summary>The channel holds the new quantity.</summary>
    Set,

    /// <summary>The channel already holds it: nothing was sent.</summary>
    Unchanged,

    /// <summary>The channel sold since; it kept its number, and the sale's order will move Tendero's.</summary>
    Stale,

    /// <summary>The channel refused: an unmapped warehouse, a variant it does not have.</summary>
    Refused,

    /// <summary>No product on the channel holds the SKU: there is nothing there to stock.</summary>
    NotListed,
}

public sealed class PublishStockToChannelHandler(
    ISalesChannels channels,
    IChannelListings listings,
    IChannelStockLevels levels,
    IUnitOfWork unitOfWork,
    StockMargin margin,
    TimeProvider clock) : ICommandHandler<PublishStockToChannelCommand, StockOutcome>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Channels);

    public async Task<StockOutcome> HandleAsync(PublishStockToChannelCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("channels.publish_stock");
        activity?.SetTag("channels.channel", command.Channel);
        activity?.SetTag("channels.sku", command.Sku);
        activity?.SetTag("channels.warehouse", command.Warehouse);

        if (await listings.HoldingAsync(command.Channel, command.Sku, cancellationToken) is null)
            return Done(activity, command.Channel, StockOutcome.NotListed);

        var level = await levels.FindAsync(command.Channel, command.Sku, command.Warehouse, cancellationToken);
        if (level is null)
        {
            level = ChannelStockLevel.For(command.Channel, command.Sku, command.Warehouse);
            levels.Add(level);
        }

        var quantity = margin.ToPublish(command.Available);
        activity?.SetTag("channels.quantity", quantity);

        var outcome = StockOutcome.Unchanged;
        if (quantity == level.Expected)
        {
            // The channel already says so; a stale mark is answered by agreeing with it.
            if (level.StaleSince is not null)
                level.Set(quantity, clock.GetUtcNow());
        }
        else
        {
            // The key names this exact change, so a retry of it changes nothing twice.
            var key = $"{command.Channel}:{command.Sku}:{command.Warehouse}:{level.Expected?.ToString() ?? "none"}>{quantity}";
            var answer = await channels.Get(command.Channel)
                .SetAvailableAsync(new ChannelStock(command.Sku, command.Warehouse, quantity, level.Expected, key), cancellationToken);

            outcome = answer switch
            {
                StockPublication.Set => Then(StockOutcome.Set, () => level.Set(quantity, clock.GetUtcNow())),
                StockPublication.Stale stale => Then(StockOutcome.Stale, () => level.WentStale(stale.Actual, clock.GetUtcNow())),
                StockPublication.Refused => StockOutcome.Refused,
            };
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Done(activity, command.Channel, outcome);
    }

    private static StockOutcome Then(StockOutcome outcome, Action change)
    {
        change();
        return outcome;
    }

    private static StockOutcome Done(Activity? activity, string channel, StockOutcome outcome)
    {
        activity?.SetTag("channels.outcome", outcome.ToString());
        if (outcome is not StockOutcome.NotListed and not StockOutcome.Unchanged)
            ChannelMetrics.Publication(channel, ChannelMetrics.StockKind, outcome switch
            {
                StockOutcome.Set => "published",
                StockOutcome.Stale => "stale",
                _ => "refused",
            });

        return outcome;
    }
}
