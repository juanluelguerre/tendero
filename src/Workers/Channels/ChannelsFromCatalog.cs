using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Channels;
using ElGuerre.Tendero.Channels.Domain;
using ElGuerre.Tendero.Channels.Features.PublishToChannel;
using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Workers.Channels;

// Where Catalog and Channels meet, and why it is here (ADR 0070). They share no
// entities: Channels never reads a Product. The worker composes both, so it is the
// one place that turns Catalog's change into the values a channel is told — the
// product as the exports say it — and nothing but values crosses.

/// <summary>
/// A product was saved or archived: every channel is told, through Channels' own
/// command. Like the other projections it never fails the message — the outbox would
/// replay the lexical index and the claims with it — so a channel that is down is
/// counted and logged, and the reconciliation repairs what it missed.
/// </summary>
public sealed partial class PublishToChannelsOnProductChange(
    IProductReader products,
    ICatalogueExport catalogue,
    ISalesChannels channels,
    ICommandDispatcher commands,
    StockToChannels stock,
    IOptions<ChannelOptions> options,
    ILogger<PublishToChannelsOnProductChange> logger) : IDomainEventHandler<ProductUpserted>, IDomainEventHandler<ProductArchived>
{
    public Task HandleAsync(ProductUpserted domainEvent, CancellationToken ct) => PublishAsync(domainEvent.ProductId, ct);

    public Task HandleAsync(ProductArchived domainEvent, CancellationToken ct) => PublishAsync(domainEvent.ProductId, ct);

    private async Task PublishAsync(ProductId id, CancellationToken ct)
    {
        if (channels.Channels.Count == 0 || await products.GetByIdAsync(id, ct) is not { } product)
            return;

        // The export says a live product; an archived one it does not say at all.
        var told = await catalogue.ProductAsync(product.Code, options.Value.Culture, ct) is { } exported
            ? ChannelProducts.From(exported)
            : ChannelProduct.Archived(product.Code);

        foreach (var channel in channels.Channels)
        {
            try
            {
                var outcome = await commands.SendAsync(new PublishToChannelCommand(channel, told), ct);

                // On the shelf, with what it may sell: not empty until the stock next moves.
                if (outcome == ChannelListingOutcome.Published && told.Status == ChannelProductStatus.OnSale)
                    await stock.TellAsync([.. told.Variants.Select(variant => variant.Sku)], [channel], ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                ChannelMetrics.Publication(channel, ChannelMetrics.ProductKind, "failed");
                LogFailed(logger, exception, product.Code, channel);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not publish {Code} to the channel {Channel}; the reconciliation will")]
    private static partial void LogFailed(ILogger logger, Exception exception, string code, string channel);
}

/// <summary>A product as the exports say it, as a channel is told it.</summary>
public static class ChannelProducts
{
    public static ChannelProduct From(ExportedProduct product) => new(
        product.Code,
        product.OnSale ? ChannelProductStatus.OnSale : ChannelProductStatus.NotOnSale,
        product.Name,
        product.Description,
        product.Brand,
        [.. product.Pictures.Select(picture => new ChannelPicture(picture.ImageId, picture.Alt))],
        [
            .. product.Variants.Select(variant => new ChannelVariant(
                variant.Sku,
                variant.Barcode,
                variant.Price,
                // The axis as a shopper reads it: the channel shows the option's name.
                [.. variant.Options.Select(option => new ChannelOption(option.AxisLabel, option.Label))])),
        ])
    {
        // One id per source: a product is imported from a source once.
        References = product.References
            .GroupBy(reference => reference.Source, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().ExternalId, StringComparer.Ordinal),
    };
}
