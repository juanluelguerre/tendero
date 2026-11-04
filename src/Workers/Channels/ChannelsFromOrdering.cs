using ElGuerre.Tendero.Channels;
using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Workers.Channels;

/// <summary>
/// Tendero shipped an order a sales channel took (phase 16, S3; ADR 0070): the channel is
/// told, so its customer hears it from the channel — without it the channel's order would
/// stay unfulfilled forever. Like the other bridges it never fails the message: a channel
/// that is down is counted and logged.
/// </summary>
public sealed partial class TellTheChannelOnOrderShipped(
    IOrderRepository orders,
    ISalesChannels channels,
    ILogger<TellTheChannelOnOrderShipped> logger) : IDomainEventHandler<OrderShipped>
{
    public async Task HandleAsync(OrderShipped domainEvent, CancellationToken ct)
    {
        if (await orders.FindByIdAsync(domainEvent.OrderId, ct) is not { ReceivedFrom: { } origin }
            || !channels.Channels.Contains(origin.Source))
            return;

        try
        {
            await channels.Get(origin.Source).MarkShippedAsync(new ChannelShipment(origin.ExternalId, null, null), ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ChannelMetrics.Publication(origin.Source, ChannelMetrics.ShipmentKind, "failed");
            LogFailed(logger, exception, origin.Number, origin.Source);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not tell {Channel} that its order {Number} was shipped")]
    private static partial void LogFailed(ILogger logger, Exception exception, string number, string channel);
}
