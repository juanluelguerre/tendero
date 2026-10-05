using System.ComponentModel;
using System.Globalization;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Features.ProductSpecs;

public sealed record ProductSpecsArguments(
    [property: Description("One to four product codes, as search_products returned them.")] IReadOnlyList<string> Codes,
    [property: Description("The language to answer in: es or en.")] string? Culture = null);

public sealed record SpecsForShopper(IReadOnlyList<ProductSpecsForShopper> Products);

/// <param name="Specs">What the shop holds of the product, a line each: "Material: cast aluminium".</param>
public sealed record ProductSpecsForShopper(string Code, string Name, IReadOnlyList<SpecForShopper> Specs);

/// <param name="Approved">Whether a person approved it from the product's own words (a claim), rather than the catalogue holding it.</param>
/// <param name="Quotes">For an approved one, the words it was approved on.</param>
/// <param name="Support">For an approved one, how well those words support it, 0 to 1.</param>
public sealed record SpecForShopper(string Text, bool Approved, IReadOnlyList<string> Quotes, double? Support);

/// <summary>
/// <c>get_product_specs</c> (phase 12b): what the shop holds of up to four products —
/// the catalogue's attributes and the claims a person approved (ADR 0018), the same
/// sheet the product page and the MCP server read. Every line is a fact an answer
/// may cite, and it says which of the two it is: what the catalogue holds reads as
/// the data sheet's, what a person approved reads as approved, with its evidence.
/// A value the shop does not hold is no line at all — not a "no".
/// </summary>
public sealed class ProductSpecsTool(IQueryDispatcher queries) : ReadTool<ProductSpecsArguments, SpecsForShopper>(queries)
{
    public override ToolAudience Audience => ToolAudience.Shopper;

    public override string Name => "get_product_specs";

    public override string Description =>
        "What the shop knows about one to four products: their attributes and the statements a person approved, a line each. A product with no line about something means the shop does not hold it.";

    protected override async Task<(SpecsForShopper Data, int Rows, int? Total)> ReadAsync(
        ProductSpecsArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        var culture = CultureNegotiation.Resolve(arguments.Culture, default);
        var sheet = await queries.SendAsync(new ProductSpecsQuery(arguments.Codes ?? [], culture), cancellationToken);
        if (sheet.Products.Count == 0)
            throw new ArgumentException("No product on sale has any of those codes. Use codes search_products returned.");

        var products = sheet.Products.Select((product, column) => new ProductSpecsForShopper(product.Code, product.Name,
        [
            .. sheet.Rows
                .Select(row => (row.Label, Cell: row.Cells[column]))
                .Where(line => line.Cell.Source != SpecSources.None)
                .Select(line => Spec($"{line.Label}: {Phrase(line.Cell, culture)}",
                    line.Cell.Source == SpecSources.Description ? line.Cell.Evidence : null)),
            .. sheet.Statements
                .Where(statement => statement.ProductId == product.ProductId)
                .Select(statement => Spec(statement.Text, statement.Evidence)),
        ])).ToList();

        var specs = new SpecsForShopper(products);
        return (specs, products.Sum(product => product.Specs.Count), null);
    }

    protected override IReadOnlyList<Fact> Facts(SpecsForShopper data) =>
    [
        .. data.Products.SelectMany(product => product.Specs.Select(spec => new Fact(
            product.Code, spec.Approved ? FactSource.Claim : FactSource.DataSheet, spec.Text,
            spec.Approved ? spec.Quotes : null, spec.Support))),
    ];

    protected override IReadOnlyList<Subject> Subjects(SpecsForShopper data) =>
        [.. data.Products.Select(product => new Subject(product.Code, product.Name))];

    private static SpecForShopper Spec(string text, SpecEvidence? evidence) =>
        new(text, evidence is not null, [.. evidence?.Quotes.Select(quote => quote.Text) ?? []], evidence?.Support);

    /// <summary>A value as a shopper reads it: a flag as a word of their language, a number with its unit.</summary>
    private static string Phrase(SpecCell cell, string culture) => cell switch
    {
        { Flag: { } flag } => (flag, culture) switch
        {
            (true, "en") => "yes",
            (true, _) => "sí",
            _ => "no",
        },
        { Number: { } number } => string.Create(CultureInfo.InvariantCulture, $"{number:0.##}") + (cell.Unit is { Length: > 0 } unit ? $" {unit}" : ""),
        _ => cell.Value ?? "",
    };
}
