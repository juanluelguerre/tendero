using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// A product is found by the id Tendero last published it as, then by the id the
/// import recorded, then by its SKUs, and last by its handle — the code in lower case,
/// the one the seed gave the lab store's products — so publishing updates what the
/// store holds rather than making a second copy (the round trip, P16-28).
/// </summary>
internal sealed class ShopifySalesChannel(ShopifyAdmin admin, IOptions<ShopifyOptions> options) : ISalesChannel
{
    public const string Key = "shopify";
    private const string OrderGid = "gid://shopify/Order/";

    private bool? taxesIncluded;
    private string? onlineStore;

    public string Channel => Key;

    /// <summary>The order in the store's admin, by its number however it was named (P16-29).</summary>
    public string? OrderPage(string orderExternalId)
    {
        var id = orderExternalId.StartsWith(OrderGid, StringComparison.Ordinal) ? orderExternalId[OrderGid.Length..] : orderExternalId;
        return $"https://{options.Value.Store}/admin/orders/{id}";
    }

    public async Task<ChannelListingIds> PublishAsync(ChannelProduct product, CancellationToken cancellationToken = default)
    {
        await EnsurePricesMeanTheSameAsync(cancellationToken);

        var held = await HeldAsync(product, cancellationToken);
        var existing = held?.Id;

        if (product.Status == ChannelProductStatus.Archived && product.Variants.Count == 0)
            return await ArchiveAsync(existing, product.Code, cancellationToken);

        // The store's handle is kept: its URLs, and what search engines know them by,
        // are the merchant's. A product the store never held is named by its code.
        var handle = held?.Handle ?? HandleOf(product.Code);

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
        await WriteMetafieldsAsync(ids.ProductId, product, cancellationToken);

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

    private sealed record HeldProduct(string Id, string Handle);

    /// <summary>
    /// The store's own product for this one, if it holds it: by the id Tendero last
    /// published it as, then by the id it was imported from this store with (S1), then
    /// by its SKUs — the identity the merchant shares between the two systems — and
    /// last by the handle Tendero names the products it makes with, its code. A code
    /// is Tendero's and a handle the store's, so the code alone does not find what the
    /// store made: the lab store's round trip (2026-11-03) made a second copy that way.
    /// </summary>
    private async Task<HeldProduct?> HeldAsync(ChannelProduct product, CancellationToken cancellationToken)
    {
        foreach (var id in new[] { product.ListedAs, product.References.GetValueOrDefault(Key) })
            if (id is { Length: > 0 } && await ProductAsync(id, cancellationToken) is { } listed)
                return listed;

        return await BySkusAsync(product, cancellationToken) ?? await ByHandleAsync(HandleOf(product.Code), cancellationToken);
    }

    private async Task<HeldProduct?> ByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        var product = (await admin.QueryAsync(
            "query ($handle: String!) { productByIdentifier(identifier: { handle: $handle }) { id handle } }",
            new { handle },
            cancellationToken)).GetProperty("productByIdentifier");

        return product.ValueKind == JsonValueKind.Object
            ? new HeldProduct(product.GetProperty("id").GetString()!, product.GetProperty("handle").GetString()!)
            : null;
    }

    private async Task<HeldProduct?> ProductAsync(string id, CancellationToken cancellationToken)
    {
        var product = (await admin.QueryAsync(
            "query ($id: ID!) { product(id: $id) { id handle } }",
            new { id },
            cancellationToken)).GetProperty("product");

        return product.ValueKind == JsonValueKind.Object
            ? new HeldProduct(product.GetProperty("id").GetString()!, product.GetProperty("handle").GetString()!)
            : null;
    }

    /// <summary>The one product of the store that sells these SKUs; two are refused rather than guessed between.</summary>
    private async Task<HeldProduct?> BySkusAsync(ChannelProduct product, CancellationToken cancellationToken)
    {
        var skus = product.Variants.Select(variant => variant.Sku).Where(sku => sku.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (skus.Count == 0)
            return null;

        var answer = await admin.QueryAsync(
            "query ($query: String!) { productVariants(first: 250, query: $query) { nodes { sku product { id handle } } } }",
            new { query = string.Join(" OR ", skus.Select(SkuQuery)) },
            cancellationToken);

        var holders = answer.GetProperty("productVariants").GetProperty("nodes").EnumerateArray()
            // The search matches loosely; only the SKU itself counts.
            .Where(variant => variant.GetProperty("sku").GetString() is { } sku && skus.Contains(sku))
            .Select(variant => variant.GetProperty("product"))
            .Select(holder => new HeldProduct(holder.GetProperty("id").GetString()!, holder.GetProperty("handle").GetString()!))
            .Distinct()
            .ToList();

        return holders.Count switch
        {
            0 => null,
            1 => holders[0],
            _ => throw new ChannelRefusedException(
                $"The store sells {product.Code}'s SKUs under {holders.Count} products ({string.Join(", ", holders.Select(holder => holder.Handle))}): "
                + "which one is it is the merchant's to say, not Tendero's to guess."),
        };
    }

    private static string SkuQuery(string sku) => $"sku:\"{sku.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

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

    /// <summary>
    /// The option a Shopify product without axes still has: its variants must be values
    /// of some option, or productSet answers "Product options input is required when
    /// updating variants" — every such product on the lab store's round trip. The seed
    /// and Shopify's own admin use this one.
    /// </summary>
    private static readonly IReadOnlyList<ChannelOption> DefaultOption = [new("Title", "Default Title")];

    private JsonObject Input(ChannelProduct product, string handle)
    {
        var variants = product.Variants
            .Select(variant => variant.Options.Count == 0 ? variant with { Options = DefaultOption } : variant)
            .ToList();
        product = product with { Variants = variants };
        var axes = product.Variants.SelectMany(variant => variant.Options).Select(option => option.Axis).Distinct().ToList();

        var input = JsonSerializer.SerializeToNode(new
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
        })!.AsObject();

        // Only when there are pictures the store can fetch: productSet's files replace
        // the product's, and "files": null is a value in GraphQL, not an absence.
        if (Pictures(product) is { } files)
            input["files"] = JsonSerializer.SerializeToNode(files);

        return input;
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
            new { query = SkuQuery(sku) },
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

    // Shopify's limit for one metafieldsSet.
    private const int MetafieldsPerWrite = 25;

    /// <summary>
    /// The code the importer reads back and every attribute, as the store's own metafields:
    /// under the key its filters list (<see cref="ShopifyOptions.AttributeKeys"/>) or the code
    /// in lower case. Written with metafieldsSet, which leaves every other metafield alone —
    /// productSet's metafields replace them all, and publishing that way wiped the lab
    /// store's attributes (2026-11-04).
    /// </summary>
    private async Task WriteMetafieldsAsync(string productId, ChannelProduct product, CancellationToken cancellationToken)
    {
        var metafields = product.Attributes
            .Select(attribute => (Key: options.Value.AttributeKeys.GetValueOrDefault(attribute.Code) ?? attribute.Code.ToLowerInvariant(), attribute.Value))
            .Prepend((Key: ShopifyOptions.CodeKey, Value: product.Code))
            .Select(metafield => new
            {
                ownerId = productId,
                @namespace = options.Value.AttributesNamespace,
                key = metafield.Key,
                type = "single_line_text_field",
                value = metafield.Value,
            });

        foreach (var chunk in metafields.Chunk(MetafieldsPerWrite))
        {
            var answer = (await admin.QueryAsync(
                """
                mutation ($metafields: [MetafieldsSetInput!]!) {
                  metafieldsSet(metafields: $metafields) { userErrors { field message } }
                }
                """,
                new { metafields = chunk },
                cancellationToken)).GetProperty("metafieldsSet");
            Refuse(answer, product.Code);
        }
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
