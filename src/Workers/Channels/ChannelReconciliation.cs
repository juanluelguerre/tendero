using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Channels;
using ElGuerre.Tendero.Channels.Domain;
using ElGuerre.Tendero.Channels.Features.PublishToChannel;
using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Ordering.Features.ReceiveChannelOrders;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Workers.Channels;

/// <summary>
/// What repairs a channel when a message was missed (phase 16, S3; ADR 0070). A channel
/// guarantees neither its webhooks' delivery nor their order, a price list read from files
/// raises no event, and a bridge that met a channel down counted it and moved on — so the
/// orders it took are read again from where the last pass got to, and the catalogue and
/// the stock are told again whole. Each step is idempotent, which is what lets it run on
/// a timer. On a laptop Shopify cannot reach, this is the path orders arrive by.
/// </summary>
public sealed partial class ChannelReconciliation(
    ISalesChannels channels,
    IChannelCursors cursors,
    IUnitOfWork unitOfWork,
    ICommandDispatcher commands,
    ICatalogueExport catalogue,
    IStockRepository stock,
    StockToChannels stockToChannels,
    IOptions<ChannelOptions> options,
    TimeProvider clock,
    ILogger<ChannelReconciliation> logger)
{
    /// <summary>
    /// Every channel's orders changed since its mark, received, and the mark moved on. A
    /// channel seen for the first time starts now: what it sold before it was connected is
    /// the import's history, not orders to ship.
    /// </summary>
    public async Task ReceiveOrdersAsync(CancellationToken ct)
    {
        foreach (var channel in channels.Channels)
        {
            try
            {
                var cursor = await cursors.FindAsync(channel, ct);
                if (cursor is null)
                {
                    cursor = ChannelCursor.For(channel, clock.GetUtcNow());
                    cursors.Add(cursor);
                }

                var received = await commands.SendAsync(new ReceiveChannelOrdersCommand(channel, cursor.OrdersReadUntil), ct);
                cursor.ReadUntil(received.ReadUntil);
                await unitOfWork.SaveChangesAsync(ct);

                foreach (var note in received.Notes)
                    LogSkipped(logger, channel, note);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogOrdersFailed(logger, exception, channel);
            }
        }
    }

    /// <summary>Every live product and every SKU's stock, told again to every channel; what did not change is not sent twice.</summary>
    public async Task RepublishAsync(CancellationToken ct)
    {
        if (channels.Channels.Count == 0)
            return;

        foreach (var product in await catalogue.ProductsAsync(options.Value.Culture, ct))
        {
            var told = ChannelProducts.From(product);
            foreach (var channel in channels.Channels)
            {
                try
                {
                    await commands.SendAsync(new PublishToChannelCommand(channel, told), ct);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    ChannelMetrics.Publication(channel, ChannelMetrics.ProductKind, "failed");
                    LogProductFailed(logger, exception, product.Code, channel);
                }
            }
        }

        await stockToChannels.TellAsync([.. (await stock.AllAsync(ct)).Select(row => row.Sku).Distinct(StringComparer.Ordinal)], ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "An order {Channel} took was not received: {Note}")]
    private static partial void LogSkipped(ILogger logger, string channel, string note);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the orders {Channel} took; the next pass will")]
    private static partial void LogOrdersFailed(ILogger logger, Exception exception, string channel);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not publish {Code} to {Channel} on reconciling; the next pass will")]
    private static partial void LogProductFailed(ILogger logger, Exception exception, string code, string channel);
}

/// <summary>
/// The reconciliation on its timer: the catalogue and stock once at start and every few
/// hours, the orders every few minutes. Nothing runs while no channel is registered.
/// </summary>
public sealed partial class ChannelReconciliationWorker(
    IServiceScopeFactory scopes,
    IOptions<ChannelOptions> options,
    TimeProvider clock,
    ILogger<ChannelReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var republishAt = clock.GetUtcNow();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var reconciliation = scope.ServiceProvider.GetRequiredService<ChannelReconciliation>();

                if (clock.GetUtcNow() >= republishAt)
                {
                    await reconciliation.RepublishAsync(stoppingToken);
                    republishAt = clock.GetUtcNow() + options.Value.RepublishEvery;
                }

                await reconciliation.ReceiveOrdersAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }

            await Task.Delay(options.Value.ReceiveOrdersEvery, clock, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The channels' reconciliation failed; it runs again on its next turn")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
