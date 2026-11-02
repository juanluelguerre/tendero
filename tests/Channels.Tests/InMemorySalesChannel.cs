using ElGuerre.Tendero.Channels.Ports;

namespace ElGuerre.Tendero.Channels.Tests;

/// <summary>
/// A channel that holds what it is told in memory: the deterministic fake the slices'
/// tests run against, held to the same contract as Shopify's adapter. <see cref="Sell"/>
/// is a sale the channel made on its own, which is what turns the next publication stale.
/// </summary>
internal sealed class InMemorySalesChannel : ISalesChannel
{
    private readonly Dictionary<string, (ChannelProduct Product, ChannelListingIds Ids)> products = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Sku, string Location), int> stock = [];

    public string Channel => "memory";

    public List<ChannelShipment> Shipped { get; } = [];

    public ChannelProductStatus? StatusOf(string code) => products.TryGetValue(code, out var held) ? held.Product.Status : null;

    public int? AvailableOf(string sku, string location) => stock.TryGetValue((sku, location), out var held) ? held : null;

    public void Sell(string sku, string location, int quantity) => stock[(sku, location)] -= quantity;

    public Task<ChannelListingIds> PublishAsync(ChannelProduct product, CancellationToken cancellationToken = default)
    {
        if (product.Status == ChannelProductStatus.Archived && product.Variants.Count == 0)
        {
            if (!products.TryGetValue(product.Code, out var archived))
                throw new ChannelRefusedException($"This channel never held {product.Code}.");

            products[product.Code] = (archived.Product with { Status = ChannelProductStatus.Archived }, archived.Ids);
            return Task.FromResult(archived.Ids);
        }

        var ids = products.TryGetValue(product.Code, out var held)
            ? held.Ids with
            {
                VariantIdsBySku = product.Variants.ToDictionary(
                    variant => variant.Sku,
                    variant => held.Ids.VariantIdsBySku.GetValueOrDefault(variant.Sku) ?? $"variant/{variant.Sku}",
                    StringComparer.Ordinal),
            }
            : new ChannelListingIds(
                $"product/{product.Code}",
                product.Variants.ToDictionary(variant => variant.Sku, variant => $"variant/{variant.Sku}", StringComparer.Ordinal));

        products[product.Code] = (product, ids);
        return Task.FromResult(ids);
    }

    public Task<StockPublication> SetAvailableAsync(ChannelStock publication, CancellationToken cancellationToken = default)
    {
        if (!products.Values.Any(held => held.Ids.VariantIdsBySku.ContainsKey(publication.Sku)))
            return Task.FromResult<StockPublication>(new StockPublication.Refused($"No variant {publication.Sku} on this channel."));

        var key = (publication.Sku, publication.Location);
        int? held = stock.TryGetValue(key, out var quantity) ? quantity : null;
        if (held != publication.Expected)
            return Task.FromResult<StockPublication>(new StockPublication.Stale(held));

        stock[key] = publication.Available;
        return Task.FromResult<StockPublication>(new StockPublication.Set());
    }

    public Task MarkShippedAsync(ChannelShipment shipment, CancellationToken cancellationToken = default)
    {
        Shipped.Add(shipment);
        return Task.CompletedTask;
    }
}
