using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ElGuerre.Tendero.Knowledge.Extraction;

/// <summary>
/// A generative model proposes claims, its answer held to a JSON Schema: the
/// server forbids, token by token, anything the schema does not allow, so the
/// shape is guaranteed by construction and the content is what gets checked.
///
/// Temperature 0 and a fixed seed: the same description, build and hardware give
/// the same claims, which is what lets the claims eval be a gate (P10-0 measured
/// two runs identical).
/// </summary>
public sealed class ChatClaimExtractor(IChatClient chat) : IClaimExtractor
{
    /// <summary>The prompt version, recorded with every claim once claims are stored (roadmap B4, provenance).</summary>
    public const string PromptVersion = "claims-2026-09-26";

    /// <summary>The most a claim list may take, prompt included where the runtime counts it so (ONNX Runtime GenAI's max_length).</summary>
    public const int MaxTokens = 3072;

    private const string Instructions = """
        You read a product description from a shop and propose CLAIMS about the product.

        A claim is ONE checkable property a shopper could filter or compare on: a feature or component,
        a compatibility (induction, oven, dishwasher...), a measure (weight, size, battery hours...).
        Do NOT propose suggested uses ("for sauces", "for summer running") or tone ("classic", "cosy").

        For every claim:
        - quote_es: the SHORTEST span copied EXACTLY, character for character, from the Spanish description.
        - quote_en: the same, from the English description.
        - statement_es / statement_en: the claim as a short sentence, in each language.
        - attribute and value: ONLY when the text states a value for one of these attributes, else null:
        {0}
          Convert units to the attribute's unit (450 ml -> CAPACITY_L 0.45). A Boolean is true or false.
        - confidence: from 0 to 1.

        Never propose a property the description does not state, however likely it is.
        """;

    public async Task<IReadOnlyList<ProposedClaim>> ExtractAsync(
        ProductText text, IReadOnlyList<AttributeSpec> attributes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(attributes);

        var vocabulary = string.Join('\n', attributes.Select(attribute =>
            $"  {attribute.Code} ({attribute.Label}): " + (attribute.Options.Count > 0
                ? $"options [{string.Join(", ", attribute.Options)}]"
                : attribute.Kind + (attribute.Unit is { Length: > 0 } unit ? $" in {unit}" : ""))));

        var options = new ChatOptions
        {
            Temperature = 0,
            Seed = 42,
            // A ceiling, not a budget: twenty products never needed more than ~800
            // tokens of claims. Without it ONNX Runtime GenAI reserves the model's
            // whole context up front — Phi-4-mini's 131,072 tokens, ~16 GB — and
            // the first run of P10-0 took the machine down with it.
            MaxOutputTokens = MaxTokens,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(Schema(attributes), "claims"),
        };

        var response = await chat.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, string.Format(CultureInfo.InvariantCulture, Instructions, vocabulary)),
                new ChatMessage(ChatRole.User, $"Spanish description:\n{text.Spanish}\n\nEnglish description:\n{text.English}"),
            ],
            options, cancellationToken);

        try
        {
            using var answer = JsonDocument.Parse(response.Text);
            return [.. answer.RootElement.GetProperty("claims").EnumerateArray().Select(Read)];
        }
        catch (JsonException exception)
        {
            // What the model actually said is the only way to tell a runtime that
            // did not hold the schema from a model that could not keep to it.
            var said = response.Text;
            throw new InvalidOperationException(
                $"The model's answer is not the claims document: {exception.Message} It ended: …{said[Math.Max(0, said.Length - 160)..]}",
                exception);
        }
    }

    private static ProposedClaim Read(JsonElement claim) => new(
        claim.GetProperty("quote_es").GetString() ?? "",
        claim.GetProperty("quote_en").GetString() ?? "",
        claim.GetProperty("statement_es").GetString() ?? "",
        claim.GetProperty("statement_en").GetString() ?? "",
        claim.GetProperty("attribute").ValueKind == JsonValueKind.String ? claim.GetProperty("attribute").GetString() : null,
        claim.GetProperty("value") switch
        {
            { ValueKind: JsonValueKind.Null } => null,
            { ValueKind: JsonValueKind.String } value => value.GetString(),
            { ValueKind: JsonValueKind.True } => "true",
            { ValueKind: JsonValueKind.False } => "false",
            var value => value.GetRawText(),
        },
        claim.GetProperty("confidence").GetDouble());

    internal static JsonElement Schema(IReadOnlyList<AttributeSpec> attributes)
    {
        string[] required = ["quote_es", "quote_en", "statement_es", "statement_en", "attribute", "value", "confidence"];
        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["required"] = new[] { "claims" },
            ["properties"] = new Dictionary<string, object>
            {
                ["claims"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["required"] = required,
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["quote_es"] = new { type = "string" },
                            ["quote_en"] = new { type = "string" },
                            ["statement_es"] = new { type = "string" },
                            ["statement_en"] = new { type = "string" },
                            ["attribute"] = new { anyOf = new object[] { new { @enum = attributes.Select(a => a.Code).ToArray() }, new { type = "null" } } },
                            ["value"] = new { anyOf = new object[] { new { type = "number" }, new { type = "boolean" }, new { type = "string" }, new { type = "null" } } },
                            ["confidence"] = new { type = "number" },
                        },
                    },
                },
            },
        };
        return JsonSerializer.SerializeToElement(schema);
    }
}
