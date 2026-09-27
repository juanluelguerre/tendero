using System.Text.Json;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Ucp.Signing;

namespace ElGuerre.Tendero.Ucp.Ap2;

/// <summary>
/// Whether the checkout a mandate approved is the checkout a session is now — the
/// last check before completing (UCP §Business Verification, "terms match").
/// </summary>
public static class UcpCheckoutTerms
{
    /// <summary>
    /// UCP's "terms match": the same session, the same currency, the same goods in
    /// the same quantities at the same amounts, and the same totals. Not the words —
    /// a title in another language is the same purchase — and not the status or the
    /// expiry, which move without the purchase moving.
    /// </summary>
    public static bool Same(JsonObject mandated, JsonObject current)
    {
        static JsonNode? Goods(JsonObject checkout) => new JsonArray([.. (checkout["line_items"]?.AsArray() ?? []).Select(line => (JsonNode)new JsonObject
        {
            ["id"] = line?["id"]?.DeepClone(),
            ["item"] = line?["item"]?["id"]?.DeepClone(),
            ["price"] = line?["item"]?["price"]?.DeepClone(),
            ["quantity"] = line?["quantity"]?.DeepClone(),
            ["totals"] = Amounts(line?["totals"]),
        })]);

        static JsonNode Amounts(JsonNode? totals) => new JsonArray([.. (totals?.AsArray() ?? []).Select(total => (JsonNode)new JsonObject
        {
            ["type"] = total?["type"]?.DeepClone(),
            ["amount"] = total?["amount"]?.DeepClone(),
        })]);

        static byte[] Canonical(JsonNode? node) => Jcs.Canonicalize(JsonSerializer.SerializeToElement(node));

        return JsonNode.DeepEquals(mandated["id"], current["id"])
               && JsonNode.DeepEquals(mandated["currency"], current["currency"])
               && Canonical(Goods(mandated)).AsSpan().SequenceEqual(Canonical(Goods(current)))
               && Canonical(Amounts(mandated["totals"])).AsSpan().SequenceEqual(Canonical(Amounts(current["totals"])));
    }

}
