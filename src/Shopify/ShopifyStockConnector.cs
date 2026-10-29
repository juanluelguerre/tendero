using System.Runtime.CompilerServices;
using System.Text.Json;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Shopify;

/// <summary>
/// The stock port's Shopify adapter (phase 16, S2a, P16-10). It hands over a
/// store's stock in the shop's words, which is all the port accepts:
///
/// - a location is the warehouse a person mapped it to (<see cref="ShopifyOptions.Locations"/>),
///   as a category is; one nobody mapped is proposed under a code made from its
///   name, and its stock is not read until someone decides what it is here;
/// - a variant is the SKU the catalogue sells it under, asked of the catalogue
///   (<see cref="ISourceVariantSkus"/>) — the store's SKU is ours only when no
///   sibling shared it, so reading it from the store would count the wrong shelf;
/// - a level is <c>on_hand</c>, the units on the shelf, whatever is reserved.
///
/// What cannot be put in those words is a note, never a guess.
/// </summary>
internal sealed class ShopifyStockConnector(
    ShopifyAdmin admin, IOptions<ShopifyOptions> options, ISourceVariantSkus catalogue) : IStockSourceConnector
{
    public const string Key = "shopify";

    /// <summary>How many variants are asked of the catalogue at once.</summary>
    private const int Batch = 250;

    private readonly List<StockNote> sourceNotes = [];

    public string Source => Key;

    public IReadOnlyList<StockNote> SourceNotes => this.sourceNotes;

    public async IAsyncEnumerable<SourceWarehouse> StreamWarehousesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var location in await LocationsAsync(cancellationToken))
        {
            var code = CodeOf(location) ?? Slugs.Of(location.Name).ToUpperInvariant();
            // Two locations mapped to one warehouse are one warehouse.
            if (seen.Add(code))
                yield return new SourceWarehouse(code, location.Name, location.IsActive);
        }
    }

    public async IAsyncEnumerable<SourceStockLevel> StreamLevelsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        this.sourceNotes.Clear();

        var codes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var location in await LocationsAsync(cancellationToken))
        {
            if (CodeOf(location) is { } code)
                codes[location.Id] = code;
            else
                this.sourceNotes.Add(new StockNote(StockNoteCode.LocationNotMapped, null, location.Name));
        }

        var file = await admin.RunBulkAsync(ShopifyQueries.InventoryLevels, cancellationToken);
        if (file is null)
            yield break;

        var batch = new List<VariantLevels>(Batch);
        await foreach (var variant in GatherAsync(admin.ReadLinesAsync(file, cancellationToken), codes, cancellationToken))
        {
            batch.Add(variant);
            if (batch.Count < Batch)
                continue;

            foreach (var level in await InOurWordsAsync(batch, cancellationToken))
                yield return level;
            batch.Clear();
        }

        foreach (var level in await InOurWordsAsync(batch, cancellationToken))
            yield return level;
    }

    /// <summary>A batch of variants under the SKUs the catalogue sells them as; the ones it never imported, noted.</summary>
    private async Task<List<SourceStockLevel>> InOurWordsAsync(List<VariantLevels> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
            return [];

        var skus = await catalogue.SkusOfAsync(Key, [.. batch.Select(variant => variant.Id)], ct);
        var levels = new List<SourceStockLevel>();
        foreach (var variant in batch)
        {
            if (!skus.TryGetValue(variant.Id, out var sku))
            {
                this.sourceNotes.Add(new StockNote(StockNoteCode.VariantNotInCatalogue, null, variant.Id));
                continue;
            }

            levels.AddRange(variant.OnHand.Select(each => new SourceStockLevel(sku, each.Key, each.Value)));
        }

        return levels;
    }

    /// <summary>
    /// The bulk file read as variants, each with what it holds per warehouse. A
    /// level's parent is its variant, and grouped it comes right after it; levels
    /// in an unmapped location are left out, and two locations mapped to one
    /// warehouse add up — one count per SKU and warehouse, as the port promises.
    /// </summary>
    private static async IAsyncEnumerable<VariantLevels> GatherAsync(
        IAsyncEnumerable<JsonElement> lines, IReadOnlyDictionary<string, string> codes, [EnumeratorCancellation] CancellationToken ct)
    {
        VariantLevels? reading = null;

        await foreach (var line in lines.WithCancellation(ct))
        {
            if (!line.TryGetProperty("__parentId", out var parent))
            {
                if (reading is not null)
                    yield return reading;

                reading = new VariantLevels(ShopifyBulkProducts.IdOf(line.GetProperty("id").GetString()));
                continue;
            }

            if (reading is null || ShopifyBulkProducts.IdOf(parent.GetString()) != reading.Id)
            {
                throw new ShopifyException(
                    ShopifyFailure.UngroupedBulkFile,
                    $"A level of {parent.GetString()} came while reading {reading?.Id ?? "nothing"}: the bulk file is not grouped.");
            }

            var location = line.GetProperty("location").GetProperty("id").GetString() ?? "";
            if (!codes.TryGetValue(location, out var code))
                continue;

            var onHand = line.GetProperty("quantities").EnumerateArray()
                .Where(quantity => quantity.GetProperty("name").GetString() == "on_hand")
                .Sum(quantity => quantity.GetProperty("quantity").GetInt32());
            reading.OnHand[code] = reading.OnHand.GetValueOrDefault(code) + onHand;
        }

        if (reading is not null)
            yield return reading;
    }

    private string? CodeOf(ShopifyLocation location) =>
        options.Value.Locations.GetValueOrDefault(location.Id);

    private async Task<List<ShopifyLocation>> LocationsAsync(CancellationToken ct)
    {
        var data = await admin.QueryAsync(ShopifyQueries.Locations, ct);
        return
        [
            .. data.GetProperty("locations").GetProperty("nodes").EnumerateArray()
                .Select(node => new ShopifyLocation(
                    node.GetProperty("id").GetString()!,
                    node.GetProperty("name").GetString()!,
                    node.GetProperty("isActive").GetBoolean())),
        ];
    }

    private sealed record ShopifyLocation(string Id, string Name, bool IsActive);

    /// <summary>One variant and its units on hand, by warehouse code, in the order the file named them.</summary>
    private sealed class VariantLevels(string id)
    {
        public string Id { get; } = id;

        public Dictionary<string, int> OnHand { get; } = new(StringComparer.Ordinal);
    }
}
