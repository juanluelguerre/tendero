using ElGuerre.Tendero.Channels;
using ElGuerre.Tendero.Channels.Features.PublishStockToChannel;
using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Workers.Channels;

// Where Inventory and Channels meet (ADR 0070). Inventory raises the change of one
// warehouse's row; the channel is told every warehouse's available for the SKU, by
// warehouse code, and the channel's adapter maps a warehouse to its location.

/// <summary>
/// What every channel may sell of a set of SKUs, told warehouse by warehouse. Used when
/// stock moves and when a product is first published, so a product never reaches a
/// channel with no stock until its next movement. Never fails its caller: a channel
/// that is down is counted and logged, and the reconciliation repairs it.
/// </summary>
public sealed partial class StockToChannels(
    IStockRepository stock,
    ISalesChannels channels,
    ICommandDispatcher commands,
    ILogger<StockToChannels> logger)
{
    /// <summary>Every registered channel.</summary>
    public Task TellAsync(IReadOnlyCollection<string> skus, CancellationToken ct) => TellAsync(skus, channels.Channels, ct);

    /// <summary>Only the channels named: the one a product was just published to.</summary>
    public async Task TellAsync(IReadOnlyCollection<string> skus, IReadOnlyCollection<string> told, CancellationToken ct)
    {
        if (told.Count == 0 || skus.Count == 0)
            return;

        var rows = await stock.ForSkusAsync(skus, ct);

        foreach (var channel in told)
        {
            foreach (var row in rows)
            {
                try
                {
                    await commands.SendAsync(new PublishStockToChannelCommand(channel, row.Sku, row.WarehouseCode, row.Available), ct);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    ChannelMetrics.Publication(channel, ChannelMetrics.StockKind, "failed");
                    LogFailed(logger, exception, row.Sku, row.WarehouseCode, channel);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not tell the channel {Channel} the stock of {Sku} at {Warehouse}; the reconciliation will")]
    private static partial void LogFailed(ILogger logger, Exception exception, string sku, string warehouse, string channel);
}

/// <summary>The worker's bridges to the sales channels, beyond the handlers its scan registers.</summary>
public static class ChannelBridges
{
    public static IServiceCollection AddChannelBridges(this IServiceCollection services)
    {
        services.AddScoped<StockToChannels>();
        services.AddScoped<ChannelReconciliation>();
        return services;
    }
}

/// <summary>A SKU's stock moved at a warehouse: every channel is told what it may sell now.</summary>
public sealed class PublishStockToChannelsOnStockLevelChanged(StockToChannels stock) : IDomainEventHandler<StockLevelChanged>
{
    public Task HandleAsync(StockLevelChanged domainEvent, CancellationToken ct) => stock.TellAsync([domainEvent.Sku], ct);
}
