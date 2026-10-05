using System.ComponentModel;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Features.ListProducts;

/// <param name="Text">Words of the product's name, or its code.</param>
/// <param name="Culture">The language to name the products in: es or en.</param>
public sealed record FindProductsArguments(
    [property: Description("Words of the product's name, or its code.")] string Text,
    [property: Description("The language to name the products in: es or en.")] string? Culture = null);

public sealed record FoundProducts(int Total, IReadOnlyList<FoundProduct> Products);

public sealed record FoundProduct(string ProductId, string Code, string Name, string Status, IReadOnlyList<string> MissingCultures);

/// <summary>
/// <c>find_products</c> (phase 12, S-3b): the catalogue's list, every status, by
/// a part of the name or the code — not the shop's search, which is the shopper's.
/// </summary>
public sealed class FindProductsTool(IQueryDispatcher queries) : ReadTool<FindProductsArguments, FoundProducts>(queries)
{
    public override string Name => "find_products";

    public override string Description =>
        "Find products in the catalogue, drafts included, by part of their name or their code. Returns each product's id, code, name, status and the languages its name is missing.";

    protected override async Task<(FoundProducts Data, int Rows, int? Total)> ReadAsync(
        FindProductsArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        // Nothing in particular is the whole catalogue fifty at a time: what is
        // missing everywhere is catalogue_gaps' question, answered in one read.
        if (string.IsNullOrWhiteSpace(arguments.Text))
            throw new ArgumentException(
                "find_products needs words of a name or a code. To list what the catalogue lacks, use catalogue_gaps.");

        var page = await queries.SendAsync(
            new ListProductsQuery(null, arguments.Culture ?? CultureNegotiation.Default, 1, ReadLimits.MaxRows, arguments.Text), cancellationToken);

        var found = new FoundProducts(page.Total,
        [
            .. page.Items.Select(item => new FoundProduct(item.ProductId, item.Code, item.Name, item.Status.ToString(), item.MissingCultures)),
        ]);
        return (found, found.Products.Count, page.Total);
    }

    protected override IReadOnlyList<ResultTable> Tables(FoundProducts data) =>
    [
        ResultTable.Of("foundProducts",
            [new("code", CellKind.Text), new("name", CellKind.Text), new("status", CellKind.Term), new("missingCultures", CellKind.Text)],
            data.Products.Select(row => (IReadOnlyList<object?>)[row.Code, row.Name, row.Status, string.Join(", ", row.MissingCultures)])),
    ];
}
