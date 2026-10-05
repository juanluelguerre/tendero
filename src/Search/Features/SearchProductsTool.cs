using System.ComponentModel;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Search.Features.SearchProducts;

public sealed record SearchForShopperArguments(
    [property: Description("What the shopper is looking for, in their words: \"frying pan for induction\".")] string Text,
    [property: Description("The language to answer in: es or en.")] string? Culture = null,
    [property: Description("A category code to search inside, e.g. KITCHEN. Leave unset to search the whole shop.")] string? Category = null,
    [property: Description("A brand, as the shop spells it.")] string? Brand = null,
    [property: Description("Lowest price, inclusive.")] decimal? MinPrice = null,
    [property: Description("Highest price, inclusive.")] decimal? MaxPrice = null,
    [property: Description("Only products on offer now.")] bool OnOffer = false,
    [property: Description("Only products that can be bought now.")] bool InStock = false);

/// <param name="Total">How many products the search found in all.</param>
/// <param name="Search">The search as it was run, so the shopper can be sent to all of its results.</param>
public sealed record ProductsForShopper(int Total, IReadOnlyList<ProductForShopper> Products, SearchAsRun Search);

/// <summary>A product as the search found it. The price and whether it can be bought
/// are for choosing among them; the shopper reads both on the product's card.</summary>
public sealed record ProductForShopper(
    string Code, string Name, string? Brand, string? Category, decimal Price, string Currency, bool InStock);

public sealed record SearchAsRun(
    string Text, string? Category, string? Brand, decimal? MinPrice, decimal? MaxPrice, bool OnOffer, bool InStock);

/// <summary>
/// <c>search_products</c> (phase 12b): the shop's own search, the one the storefront
/// and the MCP server ask — hybrid when the vector layer is up, lexical when it is
/// not, products on sale only. It hands an answer no facts: what it finds is which
/// products there are, and what is true of each is the other reads'.
/// </summary>
public sealed class SearchProductsTool(IQueryDispatcher queries) : ReadTool<SearchForShopperArguments, ProductsForShopper>(queries)
{
    /// <summary>The most products one search hands the model: an answer names four at most.</summary>
    public const int MaxProducts = 12;

    public override ToolAudience Audience => ToolAudience.Shopper;

    public override string Name => "search_products";

    public override string Description =>
        "Search the shop for products, by what the shopper wants. Returns up to 12 products — code, name, brand, category, price, whether it can be bought — and how many there are in all. Narrow with category, brand, price, on offer or in stock.";

    protected override async Task<(ProductsForShopper Data, int Rows, int? Total)> ReadAsync(
        SearchForShopperArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(arguments.Text))
            throw new ArgumentException("search_products needs what the shopper is looking for.");

        var filters = new SearchFilters
        {
            Brands = arguments.Brand is { Length: > 0 } brand ? [brand] : [],
            MinPrice = arguments.MinPrice,
            MaxPrice = arguments.MaxPrice,
            OnOffer = arguments.OnOffer,
            InStock = arguments.InStock,
        };
        var page = await queries.SendAsync(
            new SearchProductsQuery(arguments.Text, CultureNegotiation.Resolve(arguments.Culture, default), 1, MaxProducts,
                arguments.Category, ProductSort.Relevance, filters),
            cancellationToken);

        var total = (int)Math.Min(page.Total, int.MaxValue);
        var found = new ProductsForShopper(total,
            [
                .. page.Hits.Take(MaxProducts).Select(hit => new ProductForShopper(
                    hit.Code, hit.Name, hit.Brand, hit.Category, hit.PriceAmount, hit.PriceCurrency, hit.InStock)),
            ],
            new SearchAsRun(arguments.Text, arguments.Category, arguments.Brand, arguments.MinPrice, arguments.MaxPrice,
                arguments.OnOffer, arguments.InStock));
        return (found, found.Products.Count, total);
    }

    protected override IReadOnlyList<Subject> Subjects(ProductsForShopper data) =>
        [.. data.Products.Select(product => new Subject(product.Code, product.Name))];
}
