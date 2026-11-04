using System.Diagnostics;
using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Features.ReceiveChannelOrders;

/// <summary>
/// The orders a sales channel took since a mark (phase 16, S3; ADR 0070): the
/// reconciliation's pass, because the channel guarantees neither delivery nor order of
/// its webhooks. Its answer says how far it read, for the next pass to go on from.
/// </summary>
public sealed record ReceiveChannelOrdersCommand(string Channel, DateTimeOffset Since)
    : ICommand<ChannelOrdersReceived>, IUnauditedCommand;

/// <summary>One order a sales channel took, by its id: the one a webhook names, or one held and read again (P16-29).</summary>
public sealed record ReceiveChannelOrderCommand(string Channel, string ExternalId, ChannelOrderPath Path = ChannelOrderPath.Webhook)
    : ICommand<ChannelOrdersReceived>, IUnauditedCommand;

/// <summary>How an order came to be read, as <c>tendero.channel.orders.received</c> counts it.</summary>
public enum ChannelOrderPath
{
    Webhook,
    Reconciliation,

    /// <summary>Skipped before for a reason that can be mended, and read again by its id.</summary>
    Held,
}

/// <summary>Why an order a channel took did not become one of Tendero's.</summary>
public enum ChannelOrderSkip
{
    /// <summary>Cancelled or fulfilled on the channel before Tendero saw it: nothing to ship.</summary>
    NotOpen,

    /// <summary>A line sells something the catalogue does not.</summary>
    NotInCatalogue,

    /// <summary>Neither a shipping nor a billing address.</summary>
    NoAddress,

    /// <summary>The channel is not one an order can come from.</summary>
    UnknownChannel,

    /// <summary>The channel's figures do not make an order by Tendero's arithmetic.</summary>
    TotalsDoNotAddUp,
}

/// <param name="About">What it concerns, when one thing does: the SKU or product the catalogue does not sell.</param>
/// <param name="Note">The sentence, in English, for the log and the trace.</param>
public sealed record SkippedChannelOrder(string ExternalId, string Number, ChannelOrderSkip Why, string? About, string Note);

/// <param name="ReadUntil">The latest change read, or the mark asked for when nothing was.</param>
/// <param name="Notes">Why an order was skipped, one sentence each, for the log and the trace.</param>
public sealed record ChannelOrdersReceived(int Received, int AlreadyHere, int Skipped, DateTimeOffset ReadUntil, IReadOnlyList<string> Notes)
{
    /// <summary>The same skips as values, in the order read: what the reconciliation holds and retries.</summary>
    public IReadOnlyList<SkippedChannelOrder> SkippedOrders { get; init; } = [];
}

/// <summary>
/// Each open order becomes one Tendero ships (<see cref="Order.Received"/>), keyed by the
/// channel's id so the webhook and the reconciliation make one order. An order already
/// cancelled or fulfilled there, or for a product the catalogue does not sell, is skipped
/// and said. The buyer is snapshotted on the order and copied nowhere (decision 4).
///
/// Not audited: the system receiving what a channel sold, many times a day; the order it
/// creates is its record.
/// </summary>
public sealed class ReceiveChannelOrdersHandler(
    IChannelOrderSources channels,
    IOrderRepository orders,
    ICatalogueVariants catalogue,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<ReceiveChannelOrdersCommand, ChannelOrdersReceived>,
      ICommandHandler<ReceiveChannelOrderCommand, ChannelOrdersReceived>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);
    private static readonly Meter Meter = new(TelemetrySources.Ordering);

    private static readonly Counter<long> Received = Meter.CreateCounter<long>(
        "tendero.channel.orders.received", unit: "{order}",
        description: "Every order a sales channel took that Tendero read, by the path it came by and what became of it.");

    public async Task<ChannelOrdersReceived> HandleAsync(ReceiveChannelOrdersCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.receive_channel_orders");
        activity?.SetTag("ordering.channel", command.Channel);

        var taken = new List<SourceOrder>();
        await foreach (var order in channels.Get(command.Channel).UpdatedSinceAsync(command.Since, cancellationToken))
            taken.Add(order);

        return await ReceiveAsync(command.Channel, ChannelOrderPath.Reconciliation, taken, command.Since, activity, cancellationToken);
    }

    public async Task<ChannelOrdersReceived> HandleAsync(ReceiveChannelOrderCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.receive_channel_order");
        activity?.SetTag("ordering.channel", command.Channel);
        activity?.SetTag("ordering.channel_order", command.ExternalId);

        var order = await channels.Get(command.Channel).FindAsync(command.ExternalId, cancellationToken);
        return await ReceiveAsync(command.Channel, command.Path, order is null ? [] : [order], clock.GetUtcNow(), activity, cancellationToken);
    }

    private async Task<ChannelOrdersReceived> ReceiveAsync(
        string channel, ChannelOrderPath path, IReadOnlyList<SourceOrder> taken, DateTimeOffset since, Activity? activity, CancellationToken cancellationToken)
    {
        var skippedOrders = new List<SkippedChannelOrder>();
        int received = 0, alreadyHere = 0;

        foreach (var placed in taken)
        {
            var (outcome, skip) = await ReceiveOneAsync(channel, placed, cancellationToken);
            switch (outcome)
            {
                case "received": received++; break;
                case "already_here": alreadyHere++; break;
                default: skippedOrders.Add(skip!); break;
            }

            Received.Add(1, new("channel", channel), new("path", PathTag(path)), new("outcome", outcome));
        }

        var skipped = skippedOrders.Count;

        if (received > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("ordering.received", received);
        activity?.SetTag("ordering.skipped", skipped);

        var readUntil = taken.Select(order => order.UpdatedAt).OfType<DateTimeOffset>().DefaultIfEmpty(since).Max();
        return new ChannelOrdersReceived(received, alreadyHere, skipped, readUntil > since ? readUntil : since, [.. skippedOrders.Select(order => order.Note)])
        {
            SkippedOrders = skippedOrders,
        };
    }

    private async Task<(string Outcome, SkippedChannelOrder? Skipped)> ReceiveOneAsync(string channel, SourceOrder placed, CancellationToken cancellationToken)
    {
        (string, SkippedChannelOrder?) Skip(string outcome, ChannelOrderSkip why, string note, string? about = null) =>
            (outcome, new SkippedChannelOrder(placed.ExternalId, placed.Number, why, about, note));

        if (await orders.FindByIdempotencyKeyAsync(Order.ReceivedKey(channel, placed.ExternalId), cancellationToken) is not null)
            return ("already_here", null);

        if (placed.State != SourceOrderState.Open)
            return Skip("not_open", ChannelOrderSkip.NotOpen,
                $"{placed.Number} was {placed.State.ToString().ToLowerInvariant()} on {channel} before Tendero saw it: nothing to ship.");

        var (lines, missing) = await SourceOrders.LinesAsync(placed, catalogue, cancellationToken);
        if (lines is null)
        {
            var unsold = missing!.Sku ?? missing.ProductName;
            return Skip("not_in_catalogue", ChannelOrderSkip.NotInCatalogue, $"{placed.Number} sells {unsold}, which the catalogue does not.", unsold);
        }

        var shipping = placed.ShippingAddress ?? placed.BillingAddress;
        var billing = placed.BillingAddress ?? placed.ShippingAddress;
        if (shipping is null || billing is null)
            return Skip("no_address", ChannelOrderSkip.NoAddress, $"{placed.Number} has nowhere to be sent.");

        if (OrderChannelOf(channel) is not { } orderChannel)
            return Skip("unknown_channel", ChannelOrderSkip.UnknownChannel, $"{channel} is not a channel an order can come from.");

        try
        {
            orders.Add(Order.Received(
                clock,
                new OrderOrigin(channel, placed.ExternalId, placed.Number),
                orderChannel,
                CustomerId.New(),
                lines,
                shipping,
                billing,
                new OrderShipping($"{channel}:shipping", placed.Shipping.Label, placed.Shipping.Amount, null),
                new OrderTotals(placed.Totals.Subtotal, placed.Totals.Discount, placed.Totals.Shipping, placed.Totals.Tax, placed.Totals.Total),
                placed.PlacedAt,
                [.. placed.Discounts.Select(discount => new OrderDiscount(discount.Code, discount.Label, discount.Amount))],
                [.. placed.Taxes.Select(SourceOrders.Tax)],
                placed.Culture ?? "es",
                placed.ContactEmail));
            return ("received", null);
        }
        catch (InvalidOperationException refused)
        {
            return Skip("totals_do_not_add_up", ChannelOrderSkip.TotalsDoNotAddUp, $"{placed.Number}'s figures do not make an order: {refused.Message}");
        }
    }

    private static string PathTag(ChannelOrderPath path) => path switch
    {
        ChannelOrderPath.Webhook => "webhook",
        ChannelOrderPath.Reconciliation => "reconciliation",
        ChannelOrderPath.Held => "held",
        _ => throw new ArgumentOutOfRangeException(nameof(path), path, "Not a path an order comes by."),
    };

    private static OrderChannel? OrderChannelOf(string channel) => channel switch
    {
        "shopify" => OrderChannel.Shopify,
        _ => null,
    };
}
