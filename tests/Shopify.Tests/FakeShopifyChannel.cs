using System.Text.Json;
using System.Text.Json.Nodes;

namespace ElGuerre.Tendero.Shopify.Tests;

/// <summary>
/// The store's writes as a sales channel (phase 16, S3), held in memory: products
/// set by handle, put on the Online Store, and inventory set with compare-and-swap —
/// enough state for the channel's contract to be asked of Shopify's adapter.
/// </summary>
internal sealed partial class FakeShopify
{
    private const string OnlineStore = "gid://shopify/Publication/1";

    private static readonly object NoErrors = Array.Empty<object>();

    private readonly Dictionary<string, JsonObject> shelf = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Item, string Location), int> available = [];
    private readonly HashSet<string> keysSeen = new(StringComparer.Ordinal);
    private int ids;

    /// <summary>Whether the store's prices include tax, as Tendero's do; false is a store the channel refuses.</summary>
    public bool TaxesIncluded { get; set; } = true;

    /// <summary>The products put on the Online Store, by id.</summary>
    public HashSet<string> OnTheOnlineStore { get; } = new(StringComparer.Ordinal);

    /// <summary>Each order's fulfilment orders, by the order's gid, as the store holds them.</summary>
    public Dictionary<string, List<FulfillmentOrder>> FulfillmentOrders { get; } = new(StringComparer.Ordinal);

    /// <summary>What each fulfillmentCreate sent, in order.</summary>
    public List<JsonElement> Fulfilled { get; } = [];

    public sealed record FulfillmentOrder(string Id, string Status);

    /// <summary>What each productSet sent as its input, in order.</summary>
    public List<JsonObject> ProductInputs { get; } = [];

    /// <summary>A product's status in the store (ACTIVE, DRAFT, ARCHIVED), by handle.</summary>
    public string? StatusOf(string handle) => shelf.TryGetValue(handle, out var product) ? (string?)product["status"] : null;

    /// <summary>A sale the store made on its own: what turns the channel's next publication stale.</summary>
    public void Sell(string sku, string location, int quantity) => available[(ItemOf(sku)!, location)] -= quantity;

    private static string Data(object data) => JsonSerializer.Serialize(new { data });

    private string? ItemOf(string sku) =>
        shelf.Values.SelectMany(product => product["variants"]!.AsArray())
            .Where(variant => (string?)variant!["sku"] == sku)
            .Select(variant => (string?)variant!["item"])
            .FirstOrDefault();

    private string? AnswerTheChannel(string query, JsonElement variables)
    {
        if (query.Contains("{ shop { taxesIncluded } }", StringComparison.Ordinal))
            return Data(new { shop = new { taxesIncluded = TaxesIncluded } });

        if (query.Contains("productByIdentifier(identifier: { handle: $handle })", StringComparison.Ordinal))
        {
            var handle = variables.GetProperty("handle").GetString()!;
            return Data(new { productByIdentifier = shelf.TryGetValue(handle, out var held) ? new { id = (string?)held["id"] } : null });
        }

        if (query.Contains("productSet(", StringComparison.Ordinal))
            return SetProduct(variables);

        if (query.Contains("productUpdate(product: $product)", StringComparison.Ordinal))
        {
            var update = variables.GetProperty("product");
            var product = shelf.Values.Single(held => (string?)held["id"] == update.GetProperty("id").GetString());
            product["status"] = update.GetProperty("status").GetString();
            return Data(new { productUpdate = new { product = Answer(product), userErrors = NoErrors } });
        }

        if (query.Contains("{ publications(", StringComparison.Ordinal))
            return Data(new
            {
                publications = new
                {
                    nodes = new[] { new { id = "gid://shopify/Publication/9", name = "Point of Sale" }, new { id = OnlineStore, name = "Online Store" } },
                },
            });

        if (query.Contains("publishablePublish", StringComparison.Ordinal))
        {
            OnTheOnlineStore.Add(variables.GetProperty("id").GetString()!);
            return Data(new { publishablePublish = new { userErrors = NoErrors } });
        }

        if (query.Contains("productVariants(first: 1, query: $query)", StringComparison.Ordinal))
        {
            var sku = variables.GetProperty("query").GetString()!.Split('"')[1];
            var nodes = ItemOf(sku) is { } item ? new[] { new { sku, inventoryItem = new { id = item } } } : [];
            return Data(new { productVariants = new { nodes } });
        }

        if (query.Contains("inventorySetQuantities", StringComparison.Ordinal))
            return SetQuantity(variables);

        if (query.Contains("order(id: $id) { fulfillmentOrders", StringComparison.Ordinal))
        {
            var order = variables.GetProperty("id").GetString()!;
            if (!FulfillmentOrders.TryGetValue(order, out var nodes))
                return Data(new { order = (object?)null });

            return Data(new { order = new { fulfillmentOrders = new { nodes = nodes.Select(node => new { id = node.Id, status = node.Status }) } } });
        }

        if (query.Contains("fulfillmentCreate(fulfillment: $fulfillment)", StringComparison.Ordinal))
        {
            var sent = variables.GetProperty("fulfillment");
            Fulfilled.Add(sent.Clone());
            foreach (var line in sent.GetProperty("lineItemsByFulfillmentOrder").EnumerateArray())
            {
                var id = line.GetProperty("fulfillmentOrderId").GetString();
                foreach (var nodes in FulfillmentOrders.Values)
                    for (var i = 0; i < nodes.Count; i++)
                        if (nodes[i].Id == id)
                            nodes[i] = nodes[i] with { Status = "CLOSED" };
            }

            return Data(new { fulfillmentCreate = new { fulfillment = new { id = "gid://shopify/Fulfillment/1" }, userErrors = NoErrors } });
        }

        if (query.Contains("inventoryLevel(locationId: $location)", StringComparison.Ordinal))
        {
            var key = (variables.GetProperty("item").GetString()!, variables.GetProperty("location").GetString()!);
            var level = available.TryGetValue(key, out var held) ? new { quantities = new[] { new { quantity = held } } } : null;
            return Data(new { inventoryItem = new { inventoryLevel = level } });
        }

        return null;
    }

    private string SetProduct(JsonElement variables)
    {
        var input = JsonNode.Parse(variables.GetProperty("input").GetRawText())!.AsObject();
        ProductInputs.Add(input);
        var handle = (string)input["handle"]!;

        if (!shelf.TryGetValue(handle, out var product))
        {
            product = new JsonObject { ["id"] = $"gid://shopify/Product/{++ids}", ["variants"] = new JsonArray() };
            shelf[handle] = product;
        }

        product["status"] = (string?)input["status"];

        // Variants by SKU keep their ids; the ones not sent are gone, as productSet does.
        var before = product["variants"]!.AsArray().ToDictionary(variant => (string)variant!["sku"]!, variant => variant!.DeepClone());
        var variants = new JsonArray();
        foreach (var sent in input["variants"]!.AsArray())
        {
            var sku = (string)sent!["sku"]!;
            variants.Add(before.TryGetValue(sku, out var kept)
                ? kept
                : new JsonObject { ["id"] = $"gid://shopify/ProductVariant/{++ids}", ["sku"] = sku, ["item"] = $"gid://shopify/InventoryItem/{ids}" });
        }

        product["variants"] = variants;

        return Data(new { productSet = new { product = Answer(product), userErrors = NoErrors } });
    }

    private static object Answer(JsonObject product) => new
    {
        id = (string?)product["id"],
        variants = new { nodes = product["variants"]!.AsArray().Select(variant => new { id = (string?)variant!["id"], sku = (string?)variant["sku"] }) },
    };

    private string SetQuantity(JsonElement variables)
    {
        var key = variables.GetProperty("key").GetString()!;
        var quantity = variables.GetProperty("input").GetProperty("quantities")[0];
        var at = (quantity.GetProperty("inventoryItemId").GetString()!, quantity.GetProperty("locationId").GetString()!);
        int? expected = quantity.GetProperty("changeFromQuantity").ValueKind == JsonValueKind.Null
            ? null
            : quantity.GetProperty("changeFromQuantity").GetInt32();

        // A key already used is the same request again: answered as it was, changing nothing.
        if (!keysSeen.Add(key))
            return Data(new { inventorySetQuantities = new { userErrors = NoErrors } });

        int? held = available.TryGetValue(at, out var current) ? current : null;
        if (held != expected)
            return Data(new
            {
                inventorySetQuantities = new
                {
                    userErrors = new[]
                    {
                        new { code = "CHANGE_FROM_QUANTITY_STALE", field = new[] { "input", "quantities", "0", "changeFromQuantity" }, message = "The quantity has changed since it was read." },
                    },
                },
            });

        available[at] = quantity.GetProperty("quantity").GetInt32();
        return Data(new { inventorySetQuantities = new { userErrors = NoErrors } });
    }
}
