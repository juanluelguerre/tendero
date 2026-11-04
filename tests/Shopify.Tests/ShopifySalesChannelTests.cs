using System.Text.Json.Nodes;
using ElGuerre.Tendero.Channels;
using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.Channels.Tests.Contract;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Shopify.Tests;

internal static class ChannelStore
{
    public const string Madrid = "gid://shopify/Location/71";

    public static (ShopifySalesChannel Channel, FakeShopify Store) Open(Uri? images = null)
    {
        var store = new FakeShopify();
        var clock = new TestClock();
        var options = Options.Create(new ShopifyOptions
        {
            Store = FakeShopify.Store,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            ThrottleBackoff = TimeSpan.Zero,
            Locations = new(StringComparer.Ordinal) { [Madrid] = "MAD" },
            PublicImagesBaseUrl = images,
        });
        var http = new HttpClient(store);
        return (new ShopifySalesChannel(new ShopifyAdmin(http, new ShopifyAccessTokens(http, options, clock), options, clock), options), store);
    }
}

/// <summary>Shopify passes the channel's contract the in-memory channel does (phase 16, S3).</summary>
public sealed class ShopifySalesChannelContractTests : SalesChannelContractTests
{
    private readonly Dictionary<ISalesChannel, FakeShopify> stores = [];

    protected override ISalesChannel CreateChannel()
    {
        var (channel, store) = ChannelStore.Open();
        stores[channel] = store;
        return channel;
    }

    protected override Task<ChannelProductStatus?> StatusOnChannelAsync(ISalesChannel channel, string code) =>
        Task.FromResult<ChannelProductStatus?>(stores[channel].StatusOf(ShopifySalesChannel.HandleOf(code)) switch
        {
            "ACTIVE" => ChannelProductStatus.OnSale,
            "DRAFT" => ChannelProductStatus.NotOnSale,
            "ARCHIVED" => ChannelProductStatus.Archived,
            _ => null,
        });

    /// <summary>A Tendero warehouse the store maps a location to.</summary>
    protected override string Location => "MAD";
}

/// <summary>What only Shopify's adapter does: its store's settings, its sales channels, its writes' shape.</summary>
public sealed class ShopifySalesChannelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ChannelProduct Mug(ChannelProductStatus status = ChannelProductStatus.OnSale, string? description = "Gres esmaltado.\n\nApta para lavavajillas.") => new(
        "TAZ-001",
        status,
        "Taza \"de\" gres",
        description,
        "Alfar",
        [new ChannelPicture("taz-001-1", "La taza")],
        [new ChannelVariant("TAZ-001-BLU", "8400000000017", new Money(12.5m, "EUR"), [new ChannelOption("Color", "Azul")])]);

    /// <summary>
    /// Tendero's prices include VAT (ADR 0028): a store that adds it at the till would
    /// sell every price above what it means, and is refused before anything is sent.
    /// </summary>
    [Fact]
    public async Task A_store_that_adds_tax_at_the_till_is_refused_before_anything_is_published()
    {
        var (channel, store) = ChannelStore.Open();
        store.TaxesIncluded = false;

        var refused = await Assert.ThrowsAsync<ChannelRefusedException>(() => channel.PublishAsync(Mug(), Ct));

        Assert.Contains("taxesIncluded", refused.Message, StringComparison.Ordinal);
        Assert.Empty(store.ProductInputs);
    }

    [Fact]
    public async Task A_product_on_sale_is_active_and_on_the_online_store_and_a_draft_is_neither()
    {
        var (channel, store) = ChannelStore.Open();

        var onSale = await channel.PublishAsync(Mug(), Ct);
        Assert.Equal("ACTIVE", store.StatusOf("taz-001"));
        Assert.Contains(onSale.ProductId, store.OnTheOnlineStore);

        var (other, draftStore) = ChannelStore.Open();
        var draft = await other.PublishAsync(Mug(ChannelProductStatus.NotOnSale), Ct);
        Assert.Equal("DRAFT", draftStore.StatusOf("taz-001"));
        Assert.DoesNotContain(draft.ProductId, draftStore.OnTheOnlineStore);
    }

    /// <summary>The handle is the code in lower case — the lab store's seed used the same, so publishing updates them.</summary>
    [Fact]
    public async Task The_store_is_sent_the_shops_values_as_its_own_fields()
    {
        var (channel, store) = ChannelStore.Open();

        await channel.PublishAsync(Mug(), Ct);

        var input = store.ProductInputs.Single();
        Assert.Equal("taz-001", (string?)input["handle"]);
        Assert.Equal("Taza \"de\" gres", (string?)input["title"]);
        Assert.Equal("<p>Gres esmaltado.</p><p>Apta para lavavajillas.</p>", (string?)input["descriptionHtml"]);
        Assert.Equal("Alfar", (string?)input["vendor"]);

        var variant = input["variants"]![0]!;
        Assert.Equal(("TAZ-001-BLU", "8400000000017", "12.50", "DENY"),
            ((string?)variant["sku"], (string?)variant["barcode"], (string?)variant["price"], (string?)variant["inventoryPolicy"]));
        Assert.True((bool?)variant["inventoryItem"]!["tracked"]);
        Assert.Equal("Azul", (string?)variant["optionValues"]![0]!["name"]);
        Assert.Equal("Color", (string?)input["productOptions"]![0]!["name"]);
        Assert.Equal("TAZ-001", (string?)input["metafields"]![0]!["value"]);
    }

    /// <summary>A description written as markup is the shopkeeper's text, not HTML the store would run.</summary>
    [Fact]
    public async Task A_description_is_escaped_not_run()
    {
        var (channel, store) = ChannelStore.Open();

        await channel.PublishAsync(Mug(description: "<script>alert(1)</script>"), Ct);

        Assert.Equal("<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>", (string?)store.ProductInputs.Single()["descriptionHtml"]);
    }

    /// <summary>A store cannot fetch a laptop's pictures: without a public address the product keeps its own.</summary>
    [Fact]
    public async Task Pictures_travel_only_when_the_store_can_fetch_them()
    {
        var (withoutAddress, first) = ChannelStore.Open();
        await withoutAddress.PublishAsync(Mug(), Ct);
        // Not sent at all: "files": null is a value in GraphQL, and productSet's files
        // replace the product's, so null could take its pictures away.
        Assert.False(first.ProductInputs.Single().ContainsKey("files"));

        var (withAddress, second) = ChannelStore.Open(new Uri("https://shop.example.com/"));
        await withAddress.PublishAsync(Mug(), Ct);
        var file = second.ProductInputs.Single()["files"]!.AsArray().Single()!;
        Assert.Equal(("https://shop.example.com/api/images/taz-001-1", "La taza"), ((string?)file["originalSource"], (string?)file["alt"]));
    }

    /// <summary>The channel sold: stale, with the store's own number, so the next try expects it.</summary>
    [Fact]
    public async Task A_stale_answer_says_what_the_store_holds_now()
    {
        var (channel, store) = ChannelStore.Open();
        await channel.PublishAsync(Mug(), Ct);
        await channel.SetAvailableAsync(new ChannelStock("TAZ-001-BLU", "MAD", 3, null, "k1"), Ct);
        store.Sell("TAZ-001-BLU", ChannelStore.Madrid, 1);

        var stale = await channel.SetAvailableAsync(new ChannelStock("TAZ-001-BLU", "MAD", 5, 3, "k2"), Ct);

        Assert.Equal(new StockPublication.Stale(2), stale);
    }

    /// <summary>A retry of the same publication changes nothing twice: the key is the publication's.</summary>
    [Fact]
    public async Task The_same_key_twice_is_one_change()
    {
        var (channel, _) = ChannelStore.Open();
        await channel.PublishAsync(Mug(), Ct);

        await channel.SetAvailableAsync(new ChannelStock("TAZ-001-BLU", "MAD", 3, null, "k1"), Ct);
        var again = await channel.SetAvailableAsync(new ChannelStock("TAZ-001-BLU", "MAD", 3, null, "k1"), Ct);

        Assert.IsType<StockPublication.Set>(again);
    }

    /// <summary>
    /// Tendero shipped an order the store took: its open fulfilment orders are fulfilled and
    /// the store tells its customer; a second call finds none open and sends nothing.
    /// </summary>
    [Fact]
    public async Task An_order_shipped_here_is_fulfilled_there_once()
    {
        var (channel, store) = ChannelStore.Open();
        store.FulfillmentOrders["gid://shopify/Order/7001"] =
            [new("gid://shopify/FulfillmentOrder/1", "OPEN"), new("gid://shopify/FulfillmentOrder/2", "CLOSED")];

        await channel.MarkShippedAsync(new ChannelShipment("7001", null, null), Ct);
        await channel.MarkShippedAsync(new ChannelShipment("gid://shopify/Order/7001", null, null), Ct);

        var sent = Assert.Single(store.Fulfilled);
        Assert.True(sent.GetProperty("notifyCustomer").GetBoolean());
        Assert.Equal(
            ["gid://shopify/FulfillmentOrder/1"],
            sent.GetProperty("lineItemsByFulfillmentOrder").EnumerateArray().Select(line => line.GetProperty("fulfillmentOrderId").GetString()));
    }

    [Fact]
    public async Task Marking_shipped_an_order_the_store_does_not_have_is_refused()
    {
        var (channel, _) = ChannelStore.Open();

        await Assert.ThrowsAsync<ChannelRefusedException>(() => channel.MarkShippedAsync(new ChannelShipment("404", null, null), Ct));
    }

    [Fact]
    public async Task A_warehouse_no_location_is_mapped_to_is_refused()
    {
        var (channel, _) = ChannelStore.Open();
        await channel.PublishAsync(Mug(), Ct);

        var refused = await channel.SetAvailableAsync(new ChannelStock("TAZ-001-BLU", "BCN", 3, null, "k1"), Ct);

        Assert.IsType<StockPublication.Refused>(refused);
    }

    // What follows was found by S3's Stryker close: each test holds a line no other one did.

    /// <summary>The first publication creates the product; the next names the one the store holds, so it is updated, not copied.</summary>
    [Fact]
    public async Task The_next_publication_names_the_product_the_store_holds()
    {
        var (channel, store) = ChannelStore.Open();

        var first = await channel.PublishAsync(Mug(), Ct);
        await channel.PublishAsync(Mug(), Ct);

        Assert.Equal([null, first.ProductId], store.Identifiers);
    }

    /// <summary>A write the store answers with a user error is the channel refusing it, with the store's own words.</summary>
    [Theory]
    [InlineData("productSet")]
    [InlineData("productUpdate")]
    [InlineData("publishablePublish")]
    [InlineData("fulfillmentCreate")]
    public async Task A_write_the_store_refuses_is_refused_with_its_reason(string mutation)
    {
        var (channel, store) = ChannelStore.Open();
        store.FulfillmentOrders["gid://shopify/Order/7001"] = [new("gid://shopify/FulfillmentOrder/1", "OPEN")];
        if (mutation == "productUpdate")
            await channel.PublishAsync(Mug(), Ct);
        store.Refusing[mutation] = "Handle has already been taken";

        var refused = await Assert.ThrowsAsync<ChannelRefusedException>(() => mutation switch
        {
            "productUpdate" => channel.PublishAsync(ChannelProduct.Archived("TAZ-001"), Ct),
            "fulfillmentCreate" => channel.MarkShippedAsync(new ChannelShipment("7001", null, null), Ct),
            _ => channel.PublishAsync(Mug(), Ct),
        });

        Assert.Contains("Handle has already been taken", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>Archived with its variants is still a full publication: only a bare archive is the status alone.</summary>
    [Fact]
    public async Task An_archived_product_sent_whole_is_set_like_any_other()
    {
        var (channel, store) = ChannelStore.Open();

        await channel.PublishAsync(Mug(ChannelProductStatus.Archived), Ct);

        Assert.Equal("ARCHIVED", store.StatusOf("taz-001"));
        Assert.Single(store.ProductInputs);
    }

    /// <summary>Each axis offers its own values, and a variant without a SKU is not one Tendero can stock.</summary>
    [Fact]
    public async Task Each_axis_offers_its_own_values_and_a_variant_without_a_sku_is_not_listed()
    {
        var (channel, store) = ChannelStore.Open();
        var product = Mug() with
        {
            Variants =
            [
                new ChannelVariant("TAZ-001-BLU-S", null, new Money(12.5m, "EUR"), [new ChannelOption("Color", "Azul"), new ChannelOption("Talla", "S")]),
                new ChannelVariant("", null, new Money(12.5m, "EUR"), [new ChannelOption("Color", "Rojo"), new ChannelOption("Talla", "M")]),
            ],
        };

        var ids = await channel.PublishAsync(product, Ct);

        var options = store.ProductInputs.Single()["productOptions"]!.AsArray()
            .ToDictionary(option => (string)option!["name"]!, option => option!["values"]!.AsArray().Select(value => (string?)value!["name"]).ToList());
        Assert.Equal(["Azul", "Rojo"], options["Color"]);
        Assert.Equal(["S", "M"], options["Talla"]);
        Assert.Equal(["TAZ-001-BLU-S"], ids.VariantIdsBySku.Keys);
    }

    /// <summary>
    /// The lab store's round trip (2026-11-03): the store sold the mug under its own
    /// handle, Tendero knows it by a code of its own, and looking for the code made a
    /// second copy. The SKUs are what both share: the product is found by them and
    /// updated, and its handle — its URLs — stays the store's.
    /// </summary>
    [Fact]
    public async Task A_product_the_store_sells_under_its_own_handle_is_found_by_its_skus_and_keeps_that_handle()
    {
        var (channel, store) = ChannelStore.Open();
        var held = store.Hold("b13bag0711", "TAZ-001-BLU");

        var ids = await channel.PublishAsync(Mug(), Ct);

        Assert.Equal(held, ids.ProductId);
        Assert.Equal([held], store.Identifiers);
        Assert.Equal(["b13bag0711"], store.Handles);
        Assert.Equal("b13bag0711", (string?)store.ProductInputs.Single()["handle"]);
    }

    /// <summary>What Tendero last published it as is asked first: the SKUs are not searched.</summary>
    [Fact]
    public async Task A_product_listed_before_is_updated_by_its_listed_id()
    {
        var (channel, store) = ChannelStore.Open();
        var held = store.Hold("taza-de-gres", "OTHER-SKU");

        await channel.PublishAsync(Mug() with { ListedAs = held }, Ct);

        Assert.Equal([held], store.Identifiers);
        Assert.Equal(0, store.SkuSearches);
    }

    /// <summary>Imported from this store (S1), the product is found by the id it came with, even with SKUs Tendero gave it.</summary>
    [Fact]
    public async Task A_product_imported_from_the_store_is_found_by_the_id_it_came_with()
    {
        var (channel, store) = ChannelStore.Open();
        var held = store.Hold("taza-de-gres", "SHOPIFY-OWN-SKU");

        await channel.PublishAsync(Mug() with { References = new Dictionary<string, string> { ["seed"] = "B0X", ["shopify"] = held } }, Ct);

        Assert.Equal([held], store.Identifiers);
    }

    /// <summary>A listed product deleted in the store's admin is looked for again, and made anew when it is gone.</summary>
    [Fact]
    public async Task A_listed_product_the_store_deleted_is_made_anew()
    {
        var (channel, store) = ChannelStore.Open();
        var gone = store.Hold("taza-de-gres", "OTHER-SKU");
        store.Delete("taza-de-gres");

        var ids = await channel.PublishAsync(Mug() with { ListedAs = gone }, Ct);

        Assert.Equal([null], store.Identifiers);
        Assert.NotEqual(gone, ids.ProductId);
        Assert.Equal(["taz-001"], store.Handles);
    }

    /// <summary>Two of the store's products sell its SKUs: which is the merchant's to say, so nothing is written.</summary>
    [Fact]
    public async Task Skus_the_store_sells_under_two_products_are_refused()
    {
        var (channel, store) = ChannelStore.Open();
        store.Hold("taza-azul", "TAZ-001-BLU");
        store.Hold("taza-roja", "TAZ-001-BLU");

        var refused = await Assert.ThrowsAsync<ChannelRefusedException>(() => channel.PublishAsync(Mug(), Ct));

        Assert.Contains("taza-azul", refused.Message, StringComparison.Ordinal);
        Assert.Empty(store.ProductInputs);
    }

    // The three below were found by the Stryker run after P16-28 and P16-29.

    /// <summary>Every SKU is asked for: the store may hold the product under its second variant's alone.</summary>
    [Fact]
    public async Task A_product_is_found_by_any_of_its_skus()
    {
        var (channel, store) = ChannelStore.Open();
        var held = store.Hold("taza-de-gres", "TAZ-001-RED");
        var twoColours = Mug() with
        {
            Variants =
            [
                new ChannelVariant("TAZ-001-BLU", null, new Money(12.5m, "EUR"), [new("Color", "Azul")]),
                new ChannelVariant("TAZ-001-RED", null, new Money(12.5m, "EUR"), [new("Color", "Rojo")]),
            ],
        };

        await channel.PublishAsync(twoColours, Ct);

        Assert.Equal([held], store.Identifiers);
    }

    /// <summary>The SKUs are what both sides share: a store product holding them wins over another that happens to have the code's handle.</summary>
    [Fact]
    public async Task The_skus_are_asked_before_the_codes_handle()
    {
        var (channel, store) = ChannelStore.Open();
        store.Hold("taz-001", "SOMETHING-ELSE");
        var bySkus = store.Hold("taza-de-gres", "TAZ-001-BLU");

        await channel.PublishAsync(Mug(), Ct);

        Assert.Equal([bySkus], store.Identifiers);
    }

    /// <summary>A variant without a SKU is not searched for: an empty SKU would match whatever the store left without one.</summary>
    [Fact]
    public async Task A_variant_without_a_sku_is_not_searched_for()
    {
        var (channel, store) = ChannelStore.Open();
        store.Hold("unrelated", "");
        var withoutSku = Mug() with { Variants = [.. Mug().Variants, new ChannelVariant("", null, new Money(12.5m, "EUR"), [new("Color", "Verde")])] };

        await channel.PublishAsync(withoutSku, Ct);

        Assert.Equal([null], store.Identifiers);
    }

    /// <summary>
    /// A product without axes still has an option in Shopify, its default one: the store
    /// refused all 101 such products on the lab store's round trip (2026-11-03) with
    /// "Product options input is required when updating variants". The seed sends the same.
    /// </summary>
    [Fact]
    public async Task A_product_without_axes_is_sent_the_stores_default_option()
    {
        var (channel, store) = ChannelStore.Open();
        var plain = Mug() with { Variants = [new ChannelVariant("TAZ-001-DEFAULT", null, new Money(12.5m, "EUR"), [])] };

        var ids = await channel.PublishAsync(plain, Ct);

        var input = store.ProductInputs.Single();
        Assert.Equal("Title", (string?)input["productOptions"]![0]!["name"]);
        Assert.Equal("Default Title", (string?)input["productOptions"]![0]!["values"]![0]!["name"]);
        var value = input["variants"]![0]!["optionValues"]![0]!;
        Assert.Equal(("Title", "Default Title"), ((string?)value["optionName"], (string?)value["name"]));
        Assert.Equal(["TAZ-001-DEFAULT"], ids.VariantIdsBySku.Keys);
    }

    /// <summary>The code travels as a metafield the importer reads back, and a picture is declared an image.</summary>
    [Fact]
    public async Task The_code_is_a_text_metafield_and_a_picture_an_image()
    {
        var (channel, store) = ChannelStore.Open(new Uri("https://shop.example.com/"));

        await channel.PublishAsync(Mug(), Ct);

        var input = store.ProductInputs.Single();
        var metafield = input["metafields"]![0]!;
        Assert.Equal(("code", "single_line_text_field"), ((string?)metafield["key"], (string?)metafield["type"]));
        Assert.Equal("IMAGE", (string?)input["files"]![0]!["contentType"]);
    }

    /// <summary>Paragraphs split on either line ending, trimmed, blank ones dropped; a blank description is none.</summary>
    [Theory]
    [InlineData(" Uno. \n\n\n\n Dos. ", "<p>Uno.</p><p>Dos.</p>")]
    [InlineData("Uno.\r\n\r\nDos.", "<p>Uno.</p><p>Dos.</p>")]
    [InlineData("   ", null)]
    public async Task A_description_becomes_paragraphs(string description, string? html)
    {
        var (channel, store) = ChannelStore.Open();

        await channel.PublishAsync(Mug(description: description), Ct);

        Assert.Equal(html, (string?)store.ProductInputs.Single()["descriptionHtml"]);
    }

    /// <summary>The store's settings and its Online Store are asked once per adapter, not once per product.</summary>
    [Fact]
    public async Task The_stores_settings_are_asked_once()
    {
        var (channel, store) = ChannelStore.Open();

        await channel.PublishAsync(Mug(), Ct);
        await channel.PublishAsync(Mug() with { Code = "TAZ-002" }, Ct);

        Assert.Single(store.Queries, query => query.Contains("{ shop { taxesIncluded } }", StringComparison.Ordinal));
        Assert.Single(store.Queries, query => query.Contains("{ publications(", StringComparison.Ordinal));
        Assert.Equal(["gid://shopify/Publication/1", "gid://shopify/Publication/1"], store.PublishedTo);
    }

    [Fact]
    public async Task A_store_without_an_online_store_is_refused()
    {
        var (channel, store) = ChannelStore.Open();
        store.HasOnlineStore = false;

        var refused = await Assert.ThrowsAsync<ChannelRefusedException>(() => channel.PublishAsync(Mug(), Ct));

        Assert.Contains("Online Store", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>Stock is set as available, a correction, naming the SKU it is for.</summary>
    [Fact]
    public async Task Stock_is_sent_as_an_available_correction_for_its_sku()
    {
        var (channel, store) = ChannelStore.Open();
        await channel.PublishAsync(Mug(), Ct);

        await channel.SetAvailableAsync(new ChannelStock("TAZ-001-BLU", "MAD", 3, null, "k1"), Ct);

        var input = store.InventoryInputs.Single();
        Assert.Equal(
            ("available", "correction", "tendero://channels/stock/TAZ-001-BLU"),
            (input.GetProperty("name").GetString(), input.GetProperty("reason").GetString(), input.GetProperty("referenceDocumentUri").GetString()));
    }

    /// <summary>
    /// The first publication expects nothing, and Shopify then compares nothing: the
    /// store's own number — the lab store's seed held some — is replaced, as the round
    /// trip on the lab store showed (2026-11-03: 196 levels set, none stale).
    /// </summary>
    [Fact]
    public async Task The_first_publication_replaces_what_the_store_held_of_its_own()
    {
        var (channel, store) = ChannelStore.Open();
        await channel.PublishAsync(Mug(), Ct);
        store.Stocks("TAZ-001-BLU", ChannelStore.Madrid, 6);

        var first = await channel.SetAvailableAsync(new ChannelStock("TAZ-001-BLU", "MAD", 3, null, "k1"), Ct);

        Assert.IsType<StockPublication.Set>(first);
    }

    /// <summary>Stale where the store holds no level at all: the channel's number is none, not zero.</summary>
    [Fact]
    public async Task Stale_where_the_store_holds_nothing_is_none()
    {
        var (channel, _) = ChannelStore.Open();
        await channel.PublishAsync(Mug(), Ct);

        var stale = await channel.SetAvailableAsync(new ChannelStock("TAZ-001-BLU", "MAD", 5, 3, "k1"), Ct);

        Assert.Equal(new StockPublication.Stale(null), stale);
    }

    /// <summary>An order being fulfilled is still open to ship, and a tracking number travels with its carrier.</summary>
    [Fact]
    public async Task A_shipment_fulfils_what_is_in_progress_and_carries_its_tracking()
    {
        var (channel, store) = ChannelStore.Open();
        store.FulfillmentOrders["gid://shopify/Order/7001"] = [new("gid://shopify/FulfillmentOrder/3", "IN_PROGRESS")];
        store.FulfillmentOrders["gid://shopify/Order/7002"] = [new("gid://shopify/FulfillmentOrder/4", "OPEN")];

        await channel.MarkShippedAsync(new ChannelShipment("7001", "UPS", "1Z999"), Ct);
        await channel.MarkShippedAsync(new ChannelShipment("7002", null, null), Ct);

        var tracked = store.Fulfilled[0];
        Assert.Equal("gid://shopify/FulfillmentOrder/3", tracked.GetProperty("lineItemsByFulfillmentOrder")[0].GetProperty("fulfillmentOrderId").GetString());
        Assert.Equal(("1Z999", "UPS"), (tracked.GetProperty("trackingInfo").GetProperty("number").GetString(), tracked.GetProperty("trackingInfo").GetProperty("company").GetString()));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, store.Fulfilled[1].GetProperty("trackingInfo").ValueKind);
    }

    /// <summary>A held order links to its page in the store's admin, where it is mended or closed (P16-29), by its number or its gid.</summary>
    [Theory]
    [InlineData("8720535781562")]
    [InlineData("gid://shopify/Order/8720535781562")]
    public void An_orders_page_is_in_the_stores_admin(string id) =>
        Assert.Equal("https://tendero-dev.myshopify.com/admin/orders/8720535781562", ((ISalesChannel)ChannelStore.Open().Channel).OrderPage(id));
}
