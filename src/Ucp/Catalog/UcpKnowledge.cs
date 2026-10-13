using System.Text.Json.Serialization;
using ElGuerre.Tendero.Knowledge.Features.ProductSpecs;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Ucp.Capabilities;
using ElGuerre.Tendero.Ucp.Negotiation;

namespace ElGuerre.Tendero.Ucp.Catalog;

// The shop's own extension of UCP's catalogue, `com.elguerre.tendero.knowledge`
// (P11-8): a product's specs as the shop knows them — each value with where it
// came from and, when a person approved it from the product's own text, the words
// it rests on and how well they support it. Its schema is ours, served at
// `/ucp/schemas/com.elguerre.tendero.knowledge.json`; the member names below are
// that schema's.

/// <summary>What the shop knows about one product: its specs, and the sentences that are not a value.</summary>
public sealed record UcpKnowledge
{
    [JsonPropertyName("specs")]
    public required IReadOnlyList<UcpSpec> Specs { get; init; }

    [JsonPropertyName("statements")]
    public required IReadOnlyList<UcpStatement> Statements { get; init; }
}

/// <summary>One attribute's value, and its source: <c>catalogue</c> or <c>description</c>. A value the shop does not know is absent, never guessed.</summary>
public sealed record UcpSpec
{
    [JsonPropertyName("attribute")]
    public required string Attribute { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("evidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UcpEvidence? Evidence { get; init; }
}

public sealed record UcpStatement
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("evidence")]
    public required UcpEvidence Evidence { get; init; }
}

/// <summary>How well the quoted words support the value, 0 to 1 — measured, not declared (phase 10) — and the words.</summary>
public sealed record UcpEvidence
{
    [JsonPropertyName("support")]
    public required double Support { get; init; }

    [JsonPropertyName("quotes")]
    public required IReadOnlyList<string> Quotes { get; init; }
}

/// <summary>
/// The extension's behaviour: whether this caller negotiated it, and the knowledge
/// to attach when it did.
///
/// **Negotiation here is optional, and never a refusal.** The catalogue is
/// anonymous: an agent that sends no profile, or one on a host the shop does not
/// fetch from, still reads the catalogue — it just does not get the extension.
/// Only a trusted profile, at the protocol version the shop implements, that speaks
/// the extension at our version turns it on.
/// </summary>
public static class UcpKnowledgeExtension
{
    public static async Task<bool> NegotiatedAsync(string? profileUrl, IPlatformProfiles? profiles, CancellationToken cancellationToken)
    {
        if (profileUrl is null || profiles is null)
            return false;

        var (profile, _) = await profiles.GetAsync(profileUrl, cancellationToken);
        return profile is not null
               && profile.Version == UcpSpecification.Version
               && UcpNegotiation.Intersect(UcpCapabilities.Declared, profile).Contains(KnowledgeCapability.Name);
    }

    /// <summary>
    /// The knowledge of each of <paramref name="codes"/>, by code. Asked four at a time
    /// — the most one specs query compares — because a lookup may carry fifty.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, UcpKnowledge>> ForAsync(
        IQueryDispatcher dispatcher, IEnumerable<string> codes, string culture, CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, UcpKnowledge>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in codes.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(ProductSpecsValidator.MaxProducts))
        {
            var sheet = await dispatcher.SendAsync(new ProductSpecsQuery(chunk, culture), cancellationToken);
            foreach (var (code, knowledge) in Of(sheet))
                found[code] = knowledge;
        }

        return found;
    }

    /// <summary>A sheet's columns, one product each, as the extension's shape: known values only.</summary>
    public static IEnumerable<(string Code, UcpKnowledge Knowledge)> Of(SpecSheet sheet) =>
        sheet.Products.Select((product, column) => (product.Code, new UcpKnowledge
        {
            Specs =
            [
                .. sheet.Rows
                    .Select(row => (row, cell: row.Cells[column]))
                    .Where(pair => pair.cell.Source != SpecSources.None && pair.cell.Value is not null)
                    .Select(pair => new UcpSpec
                    {
                        Attribute = pair.row.AttributeCode,
                        Label = pair.row.Label,
                        Value = pair.cell.Value!,
                        Source = pair.cell.Source,
                        Evidence = pair.cell.Evidence is { } evidence ? Evidence(evidence) : null,
                    })
            ],
            Statements =
            [
                .. sheet.Statements
                    .Where(statement => statement.ProductId == product.ProductId)
                    .Select(statement => new UcpStatement { Text = statement.Text, Evidence = Evidence(statement.Evidence) })
            ],
        }));

    /// <summary>Each product with its knowledge, and the envelope naming the extension it now carries.</summary>
    public static UcpCatalogPage Attach(UcpCatalogPage page, IReadOnlyDictionary<string, UcpKnowledge> knowledge) => page with
    {
        Ucp = Named(page.Ucp),
        Products = [.. page.Products.Select(product => With(product, knowledge))],
    };

    public static UcpProductAnswer Attach(UcpProductAnswer answer, IReadOnlyDictionary<string, UcpKnowledge> knowledge) => answer with
    {
        Ucp = Named(answer.Ucp),
        Product = With(answer.Product, knowledge),
    };

    /// <summary>
    /// A product the shop knows nothing about still carries the member, empty:
    /// "nothing known" is an answer, and an absent key would read as "not negotiated".
    /// </summary>
    private static UcpProduct With(UcpProduct product, IReadOnlyDictionary<string, UcpKnowledge> knowledge) =>
        product with { Knowledge = knowledge.GetValueOrDefault(product.Id) ?? new UcpKnowledge { Specs = [], Statements = [] } };

    private static UcpEnvelope Named(UcpEnvelope envelope) => envelope with
    {
        Capabilities = new Dictionary<string, IReadOnlyList<Checkout.UcpVersionOnly>>
        {
            [KnowledgeCapability.Name] = [new Checkout.UcpVersionOnly { Version = new KnowledgeCapability().Version }],
        },
    };

    private static UcpEvidence Evidence(SpecEvidence evidence) =>
        new() { Support = evidence.Support, Quotes = [.. evidence.Quotes.Select(quote => quote.Text)] };
}
