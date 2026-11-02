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

/// <summary>One order a sales channel took, by the id its webhook names.</summary>
public sealed record ReceiveChannelOrderCommand(string Channel, string ExternalId)
    : ICommand<ChannelOrdersReceived>, IUnauditedCommand;

/// <param name="ReadUntil">The latest change read, or the mark asked for when nothing was.</param>
/// <param name="Notes">Why an order was skipped, one sentence each, for the log and the trace.</param>
public sealed record ChannelOrdersReceived(int Received, int AlreadyHere, int Skipped, DateTimeOffset ReadUntil, IReadOnlyList<string> Notes);

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

        return await ReceiveAsync(command.Channel, "reconciliation", taken, command.Since, activity, cancellationToken);
    }

    public async Task<ChannelOrdersReceived> HandleAsync(ReceiveChannelOrderCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.receive_channel_order");
        activity?.SetTag("ordering.channel", command.Channel);
        activity?.SetTag("ordering.channel_order", command.ExternalId);

        var order = await channels.Get(command.Channel).FindAsync(command.ExternalId, cancellationToken);
        return await ReceiveAsync(command.Channel, "webhook", order is null ? [] : [order], clock.GetUtcNow(), activity, cancellationToken);
    }

    private async Task<ChannelOrdersReceived> ReceiveAsync(
        string channel, string path, IReadOnlyList<SourceOrder> taken, DateTimeOffset since, Activity? activity, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        int received = 0, alreadyHere = 0, skipped = 0;

        foreach (var placed in taken)
        {
            var outcome = await ReceiveOneAsync(channel, placed, notes, cancellationToken);
            switch (outcome)
            {
                case "received": received++; break;
                case "already_here": alreadyHere++; break;
                default: skipped++; break;
            }

            Received.Add(1, new("channel", channel), new("path", path), new("outcome", outcome));
        }

        if (received > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("ordering.received", received);
        activity?.SetTag("ordering.skipped", skipped);

        var readUntil = taken.Select(order => order.UpdatedAt).OfType<DateTimeOffset>().DefaultIfEmpty(since).Max();
        return new ChannelOrdersReceived(received, alreadyHere, skipped, readUntil > since ? readUntil : since, notes);
    }

    private async Task<string> ReceiveOneAsync(string channel, SourceOrder placed, List<string> notes, CancellationToken cancellationToken)
    {
        if (await orders.FindByIdempotencyKeyAsync(Order.ReceivedKey(channel, placed.ExternalId), cancellationToken) is not null)
            return "already_here";

        if (placed.State != SourceOrderState.Open)
        {
            notes.Add($"{placed.Number} was {placed.State.ToString().ToLowerInvariant()} on {channel} before Tendero saw it: nothing to ship.");
            return "not_open";
        }

        var (lines, missing) = await SourceOrders.LinesAsync(placed, catalogue, cancellationToken);
        if (lines is null)
        {
            notes.Add($"{placed.Number} sells {missing!.Sku ?? missing.ProductName}, which the catalogue does not.");
            return "not_in_catalogue";
        }

        var shipping = placed.ShippingAddress ?? placed.BillingAddress;
        var billing = placed.BillingAddress ?? placed.ShippingAddress;
        if (shipping is null || billing is null)
        {
            notes.Add($"{placed.Number} has nowhere to be sent.");
            return "no_address";
        }

        if (OrderChannelOf(channel) is not { } orderChannel)
        {
            notes.Add($"{channel} is not a channel an order can come from.");
            return "unknown_channel";
        }

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
            return "received";
        }
        catch (InvalidOperationException refused)
        {
            notes.Add($"{placed.Number}'s figures do not make an order: {refused.Message}");
            return "totals_do_not_add_up";
        }
    }

    private static OrderChannel? OrderChannelOf(string channel) => channel switch
    {
        "shopify" => OrderChannel.Shopify,
        _ => null,
    };
}
