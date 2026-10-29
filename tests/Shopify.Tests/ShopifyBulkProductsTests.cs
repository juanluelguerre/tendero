using System.Text.Json;
using ElGuerre.Tendero.Shopify;
using Xunit;

namespace ElGuerre.Tendero.Shopify.Tests;

/// <summary>
/// A bulk operation's file, read as products (phase 16, P16-3). Asked for with
/// <c>groupObjects: true</c>, each product's variants and pictures follow it on
/// lines of their own carrying <c>__parentId</c>; a product is complete when the
/// next one starts, so the file is read as a stream and never held whole.
/// </summary>
public sealed class ShopifyBulkProductsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async IAsyncEnumerable<JsonElement> Lines(string fixture)
    {
        foreach (var line in await File.ReadAllLinesAsync(FakeShopify.Fixture(fixture), Ct))
        {
            if (line.Length > 0)
                yield return JsonDocument.Parse(line).RootElement.Clone();
        }
    }

    private static async Task<List<ShopifyProduct>> Gathered(IAsyncEnumerable<JsonElement> lines)
    {
        var products = new List<ShopifyProduct>();
        await foreach (var product in ShopifyBulkProducts.GatherAsync(lines, Ct))
            products.Add(product);
        return products;
    }

    /// <summary>A Shopify id is its number; a missing one is nothing, never an error.</summary>
    [Theory]
    [InlineData("gid://shopify/Product/8001", "8001")]
    [InlineData(null, "")]
    public void A_shopify_id_is_its_number(string? gid, string id) =>
        Assert.Equal(id, ShopifyBulkProducts.IdOf(gid));

    [Fact]
    public async Task Each_products_variants_and_pictures_are_gathered_under_it()
    {
        var products = await Gathered(Lines("products.jsonl"));

        Assert.Equal(["8001", "8002"], products.Select(product => product.Id));

        var moka = products[0];
        Assert.Equal(("cafetera-moka", "Cafetera Moka", "Moka"), (moka.Handle, moka.Title, moka.Vendor));
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero), moka.PublishedAt);
        Assert.Equal("gid://shopify/TaxonomyCategory/hg-11-1-3", moka.Category?.Id);
        Assert.Equal(
            [("44001", "MOKA-OLV", "8400000000011", 29.90m), ("44002", "MOKA-BLK", "8400000000028", 32.50m)],
            moka.Variants.Select(variant => (variant.Id, variant.Sku, variant.Barcode, variant.Price)));
        Assert.Equal([("Color", "Oliva")], moka.Variants[0].Options.Select(option => (option.Name, option.Value)));
        Assert.All(moka.Variants, variant => Assert.True(variant.Taxable));
        Assert.Equal("https://cdn.shopify.com/s/files/1/moka-negro.jpg", moka.Variants[1].ImageUrl);
        Assert.Equal(
            [("https://cdn.shopify.com/s/files/1/moka-oliva.jpg", "Cafetera Moka en oliva"), ("https://cdn.shopify.com/s/files/1/moka-negro.jpg", "Cafetera Moka en negro")],
            moka.Images.Select(image => (image.Url, image.AltText)));
        Assert.Equal(new Dictionary<string, string> { ["material"] = "vidrio y acero", ["capacidad_tazas"] = "10" }, moka.Metafields);
        Assert.Equal("Moka coffee maker", moka.Translations["en"].Title);
        Assert.Equal("<p>Drip, for <strong>ten</strong> cups.</p><p>Glass jug.</p>", moka.Translations["en"].DescriptionHtml);
    }

    /// <summary>
    /// A product sold one way still has a variant at the source — Shopify's
    /// "Default Title" — with no SKU, no barcode and no picture of its own.
    /// </summary>
    [Fact]
    public async Task A_product_sold_one_way_has_its_one_variant_and_nothing_it_did_not_say()
    {
        var mug = (await Gathered(Lines("products.jsonl")))[1];

        var variant = Assert.Single(mug.Variants);
        Assert.Equal(("44003", null, null, 9.00m, null), (variant.Id, variant.Sku, variant.Barcode, variant.Price, variant.ImageUrl));
        Assert.False(variant.Taxable);
        Assert.Empty(mug.Images);
        Assert.Null(mug.PublishedAt);
        Assert.Null(mug.Category);
        Assert.Empty(mug.Translations);
        Assert.Empty(mug.Metafields);
    }

    /// <summary>
    /// A line whose parent is not the product being read means the file was not
    /// grouped — the default since 2026-01 — and reading it as a stream would put
    /// a variant on the wrong product. It is refused, never guessed.
    /// </summary>
    [Fact]
    public async Task A_line_whose_parent_is_not_the_product_being_read_is_refused()
    {
        var lines = await File.ReadAllLinesAsync(FakeShopify.Fixture("products.jsonl"), Ct);
        string[] ungrouped = [lines[0], lines[7], lines[1]];

        var refused = await Assert.ThrowsAsync<ShopifyException>(() => Gathered(Parsed(ungrouped)));

        Assert.Equal(ShopifyFailure.UngroupedBulkFile, refused.Failure);
    }

    private static async IAsyncEnumerable<JsonElement> Parsed(IEnumerable<string> lines)
    {
        await Task.Yield();
        foreach (var line in lines)
            yield return JsonDocument.Parse(line).RootElement.Clone();
    }
}
