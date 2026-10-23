using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Inventory.Tests.Sources;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Shopify.Tests;

/// <summary>The Shopify adapter of the stock port passes the contract every stock source does.</summary>
public sealed class ShopifyStockConnectorContractTests : StockSourceConnectorContractTests
{
    protected override IStockSourceConnector CreateConnector() => ShopifyStockConnectorTests.AConnector().Connector;
}

/// <summary>
/// A store's stock in the shop's words (phase 16, S2a, P16-10): each location by
/// the warehouse code a person mapped it to (a file, as the categories), each
/// variant by the SKU the catalogue sells it under — asked of the catalogue,
/// since our SKU is the store's only when no sibling shared it. What cannot be
/// put in those words is a note, never a guess.
/// </summary>
public sealed class ShopifyStockConnectorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static (ShopifyStockConnector Connector, FakeShopify Store) AConnector() => AConnector(new CatalogueSkus());

    private static (ShopifyStockConnector Connector, FakeShopify Store) AConnector(ISourceVariantSkus catalogue)
    {
        var store = new FakeShopify();
        var clock = new TestClock();
        var options = Options.Create(new ShopifyOptions
        {
            Store = FakeShopify.Store,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            BulkPollInterval = TimeSpan.Zero,
            ThrottleBackoff = TimeSpan.Zero,
            Locations = new Dictionary<string, string>
            {
                ["gid://shopify/Location/71"] = "MAD",
                ["gid://shopify/Location/72"] = "BCN",
            },
        });
        var http = new HttpClient(store);
        var admin = new ShopifyAdmin(http, new ShopifyAccessTokens(http, options, clock), options, clock);
        return (new ShopifyStockConnector(admin, options, catalogue), store);
    }

    private static async Task<List<SourceStockLevel>> Levels(ShopifyStockConnector connector)
    {
        var levels = new List<SourceStockLevel>();
        await foreach (var level in connector.StreamLevelsAsync(Ct))
            levels.Add(level);
        return levels;
    }

    [Fact]
    public async Task Each_level_comes_as_our_sku_in_our_warehouse()
    {
        var levels = await Levels(AConnector().Connector);

        Assert.Equal(
            [new SourceStockLevel("MOKA-OLV", "MAD", 4), new SourceStockLevel("MOKA-OLV", "BCN", 2), new SourceStockLevel("8001-44002", "MAD", 0)],
            levels);
    }

    /// <summary>
    /// A location nobody mapped is said once, and its stock not read; a variant
    /// the catalogue never imported is said, and its stock not read either —
    /// counting it under a guessed SKU would invent a shelf.
    /// </summary>
    [Fact]
    public async Task What_cannot_be_said_in_the_shops_words_is_noted()
    {
        var (connector, _) = AConnector();

        await Levels(connector);

        Assert.Equal(
            [new StockNote(StockNoteCode.LocationNotMapped, null, "Tienda de Bilbao"), new StockNote(StockNoteCode.VariantNotInCatalogue, null, "44999")],
            connector.SourceNotes);
    }

    /// <summary>
    /// The store's locations as warehouses: the mapped ones by their codes, an
    /// unmapped one by a code made from its name, for a person to adopt or change.
    /// </summary>
    [Fact]
    public async Task The_stores_locations_come_as_warehouses_by_their_codes()
    {
        var (connector, _) = AConnector();

        var warehouses = new List<SourceWarehouse>();
        await foreach (var warehouse in connector.StreamWarehousesAsync(Ct))
            warehouses.Add(warehouse);

        Assert.Equal(
            [
                new SourceWarehouse("MAD", "Almacén central (Madrid)", true),
                new SourceWarehouse("BCN", "Almacén norte (Barcelona)", true),
                new SourceWarehouse("TIENDA-DE-BILBAO", "Tienda de Bilbao", true),
            ],
            warehouses);
    }

    /// <summary>Reading the store twice says each of its notes once.</summary>
    [Fact]
    public async Task Reading_the_store_again_says_its_notes_once()
    {
        var (connector, _) = AConnector();

        await Levels(connector);
        await Levels(connector);

        Assert.Equal(2, connector.SourceNotes.Count);
    }

    /// <summary>
    /// The catalogue is asked a batch of variants at a time, not one by one and not
    /// all at once: 251 variants are two questions. Found by phase 16's Stryker close.
    /// </summary>
    [Fact]
    public async Task The_catalogue_is_asked_in_batches_of_250()
    {
        var file = Path.Combine(Path.GetTempPath(), $"inventory-{Guid.NewGuid():N}.jsonl");
        File.WriteAllLines(file, Enumerable.Range(1, 251).SelectMany(n => new[]
        {
            $$"""{"id":"gid://shopify/ProductVariant/{{n}}"}""",
            $$"""{"location":{"id":"gid://shopify/Location/71"},"quantities":[{"name":"on_hand","quantity":1}],"__parentId":"gid://shopify/ProductVariant/{{n}}"}""",
        }));
        try
        {
            var catalogue = new EveryVariantKnown();
            var (connector, store) = AConnector(catalogue);
            store.InventoryFixture = file;

            var levels = await Levels(connector);

            Assert.Equal((251, 2), (levels.Count, catalogue.Asked));
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>A level that does not follow its variant means the file was not grouped: refused, never guessed.</summary>
    [Fact]
    public async Task A_level_away_from_its_variant_is_refused()
    {
        var file = Path.Combine(Path.GetTempPath(), $"inventory-{Guid.NewGuid():N}.jsonl");
        File.WriteAllLines(file,
        [
            """{"id":"gid://shopify/ProductVariant/44001"}""",
            """{"id":"gid://shopify/ProductVariant/44002"}""",
            """{"location":{"id":"gid://shopify/Location/71"},"quantities":[{"name":"on_hand","quantity":4}],"__parentId":"gid://shopify/ProductVariant/44001"}""",
        ]);
        try
        {
            var (connector, store) = AConnector();
            store.InventoryFixture = file;

            var refused = await Assert.ThrowsAsync<ShopifyException>(() => Levels(connector));

            Assert.Equal(ShopifyFailure.UngroupedBulkFile, refused.Failure);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>A catalogue that sells every variant under its id, counting how often it is asked.</summary>
    private sealed class EveryVariantKnown : ISourceVariantSkus
    {
        public int Asked { get; private set; }

        public Task<IReadOnlyDictionary<string, string>> SkusOfAsync(string source, IReadOnlyCollection<string> externalVariantIds, CancellationToken ct)
        {
            Asked++;
            return Task.FromResult<IReadOnlyDictionary<string, string>>(externalVariantIds.ToDictionary(id => id, id => $"SKU-{id}"));
        }
    }

    /// <summary>The catalogue's answer, as the store's catalogue was imported: 44999 never was.</summary>
    private sealed class CatalogueSkus : ISourceVariantSkus
    {
        private static readonly Dictionary<string, string> Imported = new() { ["44001"] = "MOKA-OLV", ["44002"] = "8001-44002" };

        public Task<IReadOnlyDictionary<string, string>> SkusOfAsync(string source, IReadOnlyCollection<string> externalVariantIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                externalVariantIds.Where(Imported.ContainsKey).ToDictionary(id => id, id => Imported[id]));
    }
}
