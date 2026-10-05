using System.Text.Json;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Assist.Tino;

/// <summary>
/// The shape an answer must have, built for one turn from what that turn read
/// (<c>docs/analysis/tino-models.md</c>). A product's <c>code</c> is an enum of the
/// products read and a reason's <c>cites</c> an enum of the facts issued, so a model
/// held to a grammar cannot name what was not read. One object per kind and nothing
/// nullable: a single object with every kind's fields was valid and wrong.
///
/// A kind that this turn cannot use is not offered: with no product read there is
/// no <c>products</c> and no <c>noData</c>, and with no fact about the shop no
/// <c>shop</c>. What is always offered is asking back and saying the shop has no
/// such information.
/// </summary>
public static class AnswerSchema
{
    public const string Name = "tino_answer";

    public const int MaxProducts = 4;

    public const int MaxReasons = 3;

    public static JsonElement For(FactSheet sheet)
    {
        var codes = sheet.Products.Select(product => product.Key).ToArray();
        var productFacts = sheet.Facts.Where(fact => fact.Fact.Subject != Fact.TheShop).Select(fact => fact.Id).ToArray();
        var shopFacts = sheet.OfTheShop.Select(fact => fact.Id).ToArray();

        var kinds = new JsonArray();
        if (codes.Length > 0)
        {
            // A product read with nothing citable about it can still be named: a plain card.
            var reasons = productFacts.Length > 0
                ? Array(Cited(productFacts), 0, MaxReasons)
                : Array(Cited(["none"]), 0, 0);
            kinds.Add(Kind("products", new JsonObject
            {
                ["products"] = Array(Object(new JsonObject { ["code"] = Enum(codes), ["reasons"] = reasons }), 1, MaxProducts),
            }));
            kinds.Add(Kind("noData", new JsonObject
            {
                ["code"] = Enum(codes),
                ["about"] = Text(),
                ["language"] = Enum(CultureNegotiation.Supported),
                ["nearest"] = productFacts.Length > 0 ? Array(Cited(productFacts), 0, MaxReasons) : Array(Cited(["none"]), 0, 0),
            }));
        }

        if (shopFacts.Length > 0)
            kinds.Add(Kind("shop", new JsonObject { ["says"] = Array(Cited(shopFacts), 1, MaxReasons) }));

        kinds.Add(Kind("unknown", new JsonObject { ["language"] = Enum(CultureNegotiation.Supported) }));
        kinds.Add(Kind("ask", new JsonObject { ["ask"] = Text() }));

        return JsonSerializer.SerializeToElement(new JsonObject { ["anyOf"] = kinds });
    }

    private static JsonObject Kind(string kind, JsonObject properties)
    {
        var all = new JsonObject { ["kind"] = new JsonObject { ["const"] = kind } };
        foreach (var (name, schema) in properties.ToList())
        {
            properties.Remove(name);
            all[name] = schema;
        }

        return Object(all);
    }

    /// <summary>An object with exactly these properties, every one required.</summary>
    private static JsonObject Object(JsonObject properties) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray([.. properties.Select(property => (JsonNode)property.Key)]),
        ["properties"] = properties,
    };

    private static JsonObject Cited(IReadOnlyList<string> ids) =>
        Object(new JsonObject { ["says"] = Text(), ["cites"] = Enum(ids) });

    private static JsonObject Array(JsonObject items, int least, int most) =>
        new() { ["type"] = "array", ["minItems"] = least, ["maxItems"] = most, ["items"] = items };

    private static JsonObject Enum(IReadOnlyList<string> values) =>
        new() { ["type"] = "string", ["enum"] = new JsonArray([.. values.Select(value => (JsonNode)value)]) };

    private static JsonObject Text() => new() { ["type"] = "string" };
}
