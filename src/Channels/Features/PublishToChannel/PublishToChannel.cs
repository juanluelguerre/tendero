using System.Diagnostics;
using ElGuerre.Tendero.Channels.Domain;
using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Channels.Features.PublishToChannel;

/// <summary>
/// Tell one channel about one product (phase 16, S3; ADR 0070). Sent by the worker
/// when Catalog says a product changed or was archived, with the product as values;
/// idempotent, so the outbox's retry and the reconciliation may send it again.
///
/// Not audited: it is the system keeping a copy in step, many times a day, and the
/// listing it updates is its record — what was told, when, and what came back.
/// </summary>
public sealed record PublishToChannelCommand(string Channel, ChannelProduct Product)
    : ICommand<ChannelListingOutcome>, IUnauditedCommand;

public sealed class PublishToChannelHandler(
    ISalesChannels channels, IChannelListings listings, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<PublishToChannelCommand, ChannelListingOutcome>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Channels);

    public async Task<ChannelListingOutcome> HandleAsync(PublishToChannelCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("channels.publish_product");
        activity?.SetTag("channels.channel", command.Channel);
        activity?.SetTag("channels.product", command.Product.Code);
        activity?.SetTag("channels.status", command.Product.Status.ToString());

        var listing = await listings.FindAsync(command.Channel, command.Product.Code, cancellationToken);
        if (listing is null)
        {
            // Archived before it was ever told: the channel holds nothing to archive.
            if (command.Product.Status == ChannelProductStatus.Archived)
                return ChannelListingOutcome.Unpublished;

            listing = ChannelListing.For(command.Channel, command.Product.Code);
            listings.Add(listing);
        }

        try
        {
            // What Tendero last published it as, so the channel updates that product
            // rather than look for it again.
            var told = command.Product with { ListedAs = listing.ExternalProductId };
            var ids = await channels.Get(command.Channel).PublishAsync(told, cancellationToken);
            listing.Published(ids.ProductId, ids.VariantIdsBySku, command.Product.Status, clock.GetUtcNow());
        }
        catch (ChannelRefusedException refused)
        {
            // Retrying the same thing would be refused the same way: recorded, and left
            // for a person to read on the listing, not the outbox's five attempts.
            listing.Refused(refused.Message, clock.GetUtcNow());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("channels.outcome", listing.Outcome.ToString());
        ChannelMetrics.Publication(command.Channel, ChannelMetrics.ProductKind, listing.Outcome);
        return listing.Outcome;
    }
}
