using System.ComponentModel;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Features.GetProduct;

public sealed record GetProductArguments(
    [property: Description("The product's code, as search_products returned it.")] string Code,
    [property: Description("The language to answer in: es or en.")] string? Culture = null);

/// <param name="Description">The product's description, a sentence an item.</param>
public sealed record ProductPageForShopper(
    string Code, string Name, string? Brand, string? Category, IReadOnlyList<string> Description);

/// <summary>
/// <c>get_product</c> (phase 12b): one product on sale, as its page describes it.
/// Each sentence of the description is a fact an answer may cite — as the
/// description's, which the shopper is told: it was published with the product,
/// and nobody approved it line by line.
/// </summary>
public sealed class GetProductTool(IQueryDispatcher queries) : ReadTool<GetProductArguments, ProductPageForShopper>(queries)
{
    /// <summary>The most sentences of a description that become facts.</summary>
    public const int MaxSentences = 12;

    public override ToolAudience Audience => ToolAudience.Shopper;

    public override string Name => "get_product";

    public override string Description =>
        "One product's page: its name, brand, category and its description, sentence by sentence. Needs the product's code.";

    protected override async Task<(ProductPageForShopper Data, int Rows, int? Total)> ReadAsync(
        GetProductArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(arguments.Code))
            throw new ArgumentException("get_product needs a product's code.");

        // A product that is not on sale is not found, by the query's own rule: a
        // draft is not the shopper's to read, and the model is told only that.
        var product = await queries.SendAsync(
            new GetProductQuery(arguments.Code, CultureNegotiation.Resolve(arguments.Culture, default)), cancellationToken)
            ?? throw new ArgumentException($"No product on sale has the code {arguments.Code}. Use a code search_products returned.");

        var page = new ProductPageForShopper(
            product.Code, product.Name, product.Brand, product.Category.Count > 0 ? product.Category[^1].Name : null,
            Sentences.Of(product.Description, MaxSentences));
        return (page, 1, null);
    }

    protected override IReadOnlyList<Fact> Facts(ProductPageForShopper data) =>
        [.. data.Description.Select(sentence => new Fact(data.Code, FactSource.Description, sentence))];

    protected override IReadOnlyList<Subject> Subjects(ProductPageForShopper data) => [new Subject(data.Code, data.Name)];
}
