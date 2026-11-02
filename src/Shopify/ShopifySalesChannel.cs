using System.Globalization;
using System.Net;
using System.Text.Json;
using ElGuerre.Tendero.Channels;
using ElGuerre.Tendero.Channels.Ports;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Shopify;

/// <summary>
/// A Shopify store as a sales channel (phase 16, S3; ADR 0070): Tendero publishes
/// its products and stock here and reads back the orders the store took. The writes
/// are the ones <c>tools/shopify-seed</c> proved against the lab store — productSet,
/// publishablePublish, inventorySetQuantities — sent with their data as variables.
///
/// A product is found by its handle, the code in lower case: stable when the product
/// is renamed, and the handle the seed gave the lab store's products, so publishing
/// updates them rather than making a second copy.
/// </summary>
internal sealed class ShopifySalesChannel(ShopifyAdmin admin, IOptions<ShopifyOptions> options) : ISalesChannel
{
    public const string Key = "shopify";

    private bool? taxesIncluded;
    private string? onlineStore;

    public string Channel => Key;

    public async Task<ChannelListingIds> PublishAsync(ChannelProduct product, CancellationToken cancellationToken = default)
    {
        await EnsurePricesMeanTheSameAsync(cancellationToken);

        var handle = HandleOf(product.Code);
        var found = await admin.QueryAsync(
            "query ($handle: String!) { productByIdentifier(identifier: { handle: $handle }) { id } }",
            new { handle },
            cancellationToken);
        var existing = found.GetProperty("productByIdentifier") is { ValueKind: JsonValueKind.Object } held
            ? held.GetProperty("id").GetString()
            : null;

        if (product.Status == ChannelProductStatus.Archived && product.Variants.Count == 0)
            return await ArchiveAsync(existing, product.Code, cancellationToken);

        var answer = (await admin.QueryAsync(
            """
            mutation ($input: ProductSetInput!, $identifier: ProductSetIdentifiers) {
              productSet(input: $input, identifier: $identifier, synchronous: true) {
                product { id variants(first: 250) { nodes { id sku } } }
                userErrors { field message }
              }
            }
            """,
            new { input = Input(product, handle), identifier = existing is null ? null : new { id = existing } },
            cancellationToken)).GetProperty("productSet");

        Refuse(answer, product.Code);

        var ids = IdsOf(answer.GetProperty("product"));

        if (product.Status == ChannelProductStatus.OnSale)
            await PutOnTheOnlineStoreAsync(ids.ProductId, product.Code, cancellationToken);

        return ids;
    }

    public async Task<StockPublication> SetAvailableAsync(ChannelStock stock, CancellationToken cancellationToken = default)
    {
        if (LocationOf(stock.Location) is not { } location)
            return new StockPublication.Refused($"No Shopify location is mapped to the warehouse {stock.Location}.");

        if (await InventoryItemOfAsync(stock.Sku, cancellationToken) is not { } item)
            return new StockPublication.Refused($"The store has no variant {stock.Sku}.");

        // From 2026-04 the expected quantity is changeFromQuantity, and an idempotency
        // key is required: the same publication retried changes nothing twice.
        var answer = (await admin.QueryAsync(
            """
            mutation ($input: InventorySetQuantitiesInput!, $key: String!) {
              inventorySetQuantities(input: $input) @idempotent(key: $key) {
                userErrors { code field message }
              }
            }
            """,
            new
            {
                key = stock.IdempotencyKey,
                input = new
                {
                    name = "available",
                    reason = "correction",
                    referenceDocumentUri = $"tendero://channels/stock/{Uri.EscapeDataString(stock.Sku)}",
                    quantities = new[]
                    {
                        new { inventoryItemId = item, locationId = location, quantity = stock.Available, changeFromQuantity = stock.Expected },
                    },
                },
            },
            cancellationToken)).GetProperty("inventorySetQuantities");

        var errors = answer.GetProperty("userErrors");
        if (errors.GetArrayLength() == 0)
            return new StockPublication.Set();

        if (errors.EnumerateArray().Any(error => error.GetProperty("code").GetString() == "CHANGE_FROM_QUANTITY_STALE"))
            return new StockPublication.Stale(await AvailableAsync(item, location, cancellationToken));

        return new StockPublication.Refused(string.Join("; ", errors.EnumerateArray().Select(error => error.GetProperty("message").GetString())));
    }

    /// <summary>
    /// The order's open fulfilment orders fulfilled, the store's customer told by the store.
    /// Idempotent: a second call finds none open and sends nothing.
    /// </summary>
    public async Task MarkShippedAsync(ChannelShipment shipment, CancellationToken cancellationToken = default)
    {
        var id = shipment.OrderExternalId.StartsWith("gid://", StringComparison.Ordinal)
            ? shipment.OrderExternalId
            : $"gid://shopify/Order/{shipment.OrderExternalId}";

        var order = (await admin.QueryAsync(
            "query ($id: ID!) { order(id: $id) { fulfillmentOrders(first: 10) { nodes { id status } } } }",
            new { id },
            cancellationToken)).GetProperty("order");
        if (order.ValueKind != JsonValueKind.Object)
            throw new ChannelRefusedException($"The store has no order {shipment.OrderExternalId} to mark shipped.");

        var open = order.GetProperty("fulfillmentOrders").GetProperty("nodes").EnumerateArray()
            .Where(node => node.GetProperty("status").GetString() is "OPEN" or "IN_PROGRESS")
            .Select(node => new { fulfillmentOrderId = node.GetProperty("id").GetString() })
            .ToList();
        if (open.Count == 0)
            return;

        var answer = (await admin.QueryAsync(
            """
            mutation ($fulfillment: FulfillmentInput!) {
              fulfillmentCreate(fulfillment: $fulfillment) { fulfillment { id } userErrors { field message } }
            }
            """,
            new
            {
                fulfillment = new
                {
                    lineItemsByFulfillmentOrder = open,
                    notifyCustomer = true,
                    trackingInfo = shipment.TrackingNumber is null ? null : new { number = shipment.TrackingNumber, company = shipment.Carrier },
                },
            },
            cancellationToken)).GetProperty("fulfillmentCreate");

        Refuse(answer, shipment.OrderExternalId);
    }

    internal static string HandleOf(string code) => code.ToLowerInvariant();

    /// <summary>
    /// The status alone, so the variants the store holds — which its past orders name —
    /// stay: productSet would delete every variant a product was sent without.
    /// </summary>
    private async Task<ChannelListingIds> ArchiveAsync(string? id, string code, CancellationToken cancellationToken)
    {
        if (id is null)
            throw new ChannelRefusedException($"The store never held {code}: there is nothing to archive.");

        var answer = (await admin.QueryAsync(
            """
            mutation ($product: ProductUpdateInput!) {
              productUpdate(product: $product) {
                product { id variants(first: 250) { nodes { id sku } } }
                userErrors { field message }
              }
            }
            """,
            new { product = new { id, status = "ARCHIVED" } },
            cancellationToken)).GetProperty("productUpdate");

        Refuse(answer, code);
        return IdsOf(answer.GetProperty("product"));
    }

    private static ChannelListingIds IdsOf(JsonElement product) => new(
        product.GetProperty("id").GetString()!,
        product.GetProperty("variants").GetProperty("nodes").EnumerateArray()
            .Where(variant => variant.GetProperty("sku").GetString() is { Length: > 0 })
            .ToDictionary(variant => variant.GetProperty("sku").GetString()!, variant => variant.GetProperty("id").GetString()!, StringComparer.Ordinal));

    /// <summary>
    /// Tendero's prices include VAT (ADR 0028). A store that adds it at the till would
    /// sell every price above what Tendero means, so it is not connected: refused once,
    /// with the reason, before anything is published.
    /// </summary>
    private async Task EnsurePricesMeanTheSameAsync(CancellationToken cancellationToken)
    {
        taxesIncluded ??= (await admin.QueryAsync("{ shop { taxesIncluded } }", cancellationToken))
            .GetProperty("shop").GetProperty("taxesIncluded").GetBoolean();

        if (taxesIncluded is false)
            throw new ChannelRefusedException(
                "The store adds VAT at the till (taxesIncluded is false), and Tendero's prices include it (ADR 0028): "
                + "published, every price would sell above what it means. Turn on 'All prices include tax' in the store's settings.");
    }

    private object Input(ChannelProduct product, string handle)
    {
        var axes = product.Variants.SelectMany(variant => variant.Options).Select(option => option.Axis).Distinct().ToList();

        return new
        {
            handle,
            title = product.Name,
            descriptionHtml = Html(product.Description),
            vendor = product.Brand,
            status = product.Status switch
            {
                ChannelProductStatus.OnSale => "ACTIVE",
                ChannelProductStatus.NotOnSale => "DRAFT",
                ChannelProductStatus.Archived => "ARCHIVED",
            },
            productOptions = axes.Select(axis => new
            {
                name = axis,
                values = product.Variants.SelectMany(variant => variant.Options).Where(option => option.Axis == axis)
                    .Select(option => option.Value).Distinct().Select(value => new { name = value }),
            }),
            files = Pictures(product),
            metafields = new[] { new { @namespace = options.Value.AttributesNamespace, key = "code", type = "single_line_text_field", value = product.Code } },
            variants = product.Variants.Select(variant => new
            {
                sku = variant.Sku,
                barcode = variant.Barcode,
                price = variant.Price.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                // Shopify must not sell what it has not got: the margin bounds the rest.
                inventoryPolicy = "DENY",
                inventoryItem = new { tracked = true },
                optionValues = variant.Options.Select(option => new { optionName = option.Axis, name = option.Value }),
            }),
        };
    }

    /// <summary>
    /// The pictures only when the store can fetch them: Shopify downloads a file from
    /// its URL, and a laptop's image store is not on the internet. Without a public
    /// address the product keeps the pictures it has.
    /// </summary>
    private object[]? Pictures(ChannelProduct product) =>
        options.Value.PublicImagesBaseUrl is { } root
            ? [.. product.Pictures.Select(picture => new
            {
                originalSource = new Uri(root, $"api/images/{Uri.EscapeDataString(picture.ImageId)}").ToString(),
                alt = picture.Alt,
                contentType = "IMAGE",
            })]
            : null;

    private async Task PutOnTheOnlineStoreAsync(string id, string code, CancellationToken cancellationToken)
    {
        onlineStore ??= (await admin.QueryAsync("{ publications(first: 20) { nodes { id name } } }", cancellationToken))
            .GetProperty("publications").GetProperty("nodes").EnumerateArray()
            .FirstOrDefault(publication => publication.GetProperty("name").GetString() == "Online Store") is { ValueKind: JsonValueKind.Object } store
                ? store.GetProperty("id").GetString()
                : throw new ChannelRefusedException("The store has no Online Store channel to put the product on.");

        var answer = (await admin.QueryAsync(
            """
            mutation ($id: ID!, $input: [PublicationInput!]!) {
              publishablePublish(id: $id, input: $input) { userErrors { field message } }
            }
            """,
            new { id, input = new[] { new { publicationId = onlineStore } } },
            cancellationToken)).GetProperty("publishablePublish");

        Refuse(answer, code);
    }

    private string? LocationOf(string warehouse) =>
        options.Value.Locations.FirstOrDefault(pair => string.Equals(pair.Value, warehouse, StringComparison.Ordinal)).Key;

    private async Task<string?> InventoryItemOfAsync(string sku, CancellationToken cancellationToken)
    {
        var answer = await admin.QueryAsync(
            "query ($query: String!) { productVariants(first: 1, query: $query) { nodes { sku inventoryItem { id } } } }",
            new { query = $"sku:\"{sku.Replace("\"", "\\\"", StringComparison.Ordinal)}\"" },
            cancellationToken);

        return answer.GetProperty("productVariants").GetProperty("nodes").EnumerateArray()
            .Where(variant => variant.GetProperty("sku").GetString() == sku)
            .Select(variant => variant.GetProperty("inventoryItem").GetProperty("id").GetString())
            .FirstOrDefault();
    }

    private async Task<int?> AvailableAsync(string item, string location, CancellationToken cancellationToken)
    {
        var level = (await admin.QueryAsync(
            """
            query ($item: ID!, $location: ID!) {
              inventoryItem(id: $item) { inventoryLevel(locationId: $location) { quantities(names: ["available"]) { quantity } } }
            }
            """,
            new { item, location },
            cancellationToken)).GetProperty("inventoryItem").GetProperty("inventoryLevel");

        return level.ValueKind == JsonValueKind.Object
            ? level.GetProperty("quantities").EnumerateArray().Select(quantity => quantity.GetProperty("quantity").GetInt32()).FirstOrDefault()
            : null;
    }

    private static void Refuse(JsonElement payload, string code)
    {
        var errors = payload.GetProperty("userErrors");
        if (errors.GetArrayLength() > 0)
            throw new ChannelRefusedException(
                $"The store refused {code}: {string.Join("; ", errors.EnumerateArray().Select(error => error.GetProperty("message").GetString()))}");
    }

    /// <summary>A description's paragraphs as Shopify's HTML, every character escaped: the text is the shopkeeper's.</summary>
    private static string? Html(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : string.Concat(text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(paragraph => $"<p>{WebUtility.HtmlEncode(paragraph)}</p>"));
}
