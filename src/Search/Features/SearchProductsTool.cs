using System.ComponentModel;
using System.Text.Json.Serialization;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Search.Features.SearchProducts;

public sealed record SearchForShopperArguments(
    [property: Description("What the shopper is looking for, in their words: \"frying pan for induction\".")] string Text,
    [property: Description("The language to answer in: es or en.")] string? Culture = null,
    [property: Description("A category code to search inside, e.g. KITCHEN. Leave unset to search the whole shop.")] string? Category = null,
    [property: Description("A brand, as the shop spells it.")] string? Brand = null,
    // In the shop's currency as a person says it, and as this tool answers a
    // price: the reader is the shop's own small model, which says what it is
    // handed — the wire's minor units are for a program (ADR 0059).
    [property: Description("Lowest price, inclusive, in the shop's currency as a person says it: 40 is 40 EUR.")] decimal? MinPrice = null,
    [property: Description("Highest price, inclusive, in the shop's currency as a person says it: 40 is 40 EUR.")] decimal? MaxPrice = null,
    [property: Description("Only products on offer now.")] bool OnOffer = false,
    [property: Description("Only products that can be bought now.")] bool InStock = false);

/// <param name="Total">How many products the search found in all.</param>
/// <param name="Search">The search as it was run, so the shopper can be sent to all of its results.</param>
/// <param name="OnlyNearest">No product carries the words: these are only the nearest (the search's <c>Related</c>, ADR 0032).</param>
/// <param name="Culture">The language its facts are said in; not the model's to read, which was told it.</param>
public sealed record ProductsForShopper(
    int Total, IReadOnlyList<ProductForShopper> Products, SearchAsRun Search, bool OnlyNearest,
    [property: JsonIgnore] string Culture = CultureNegotiation.Default);

/// <summary>A product as the search found it. The price and whether it can be bought
/// are for choosing among them; the shopper reads both on the product's card.</summary>
/// <param name="Deals">The promotions it is in: said to the model as facts, not in its row.</param>
public sealed record ProductForShopper(
    string Code, string Name, string? Brand, string? Category, decimal Price, string Currency, bool InStock,
    [property: JsonIgnore] IReadOnlyList<ProductDeal>? Deals = null);

public sealed record SearchAsRun(
    string Text, string? Category, string? Brand, decimal? MinPrice, decimal? MaxPrice, bool OnOffer, bool InStock);

/// <summary>
/// <c>search_products</c> (phase 12b): the shop's own search, the one the storefront
/// and the MCP server ask — hybrid when the vector layer is up, lexical when it is
/// not, products on sale only. What it finds is which products there are, and what
/// is true of each is the other reads' — with one exception: asked for products on
/// offer, the filter is what makes each one true, so each is said to be on offer
/// now (<see cref="FactSource.Campaign"/>). Without it, a shopper who asked to see
/// the kitchenware with the campaign's 5 € off got nothing: every card said so of a
/// product nothing had been said of, and grounding dropped them all (phase 13c).
///
/// When no product carries the words the search answers with the nearest, and the
/// model is told so (<c>onlyNearest</c>). Under the on-offer filter the nearest are
/// not shown at all: asked for kitchenware on offer when none was, they were a
/// sweatshirt and gloves on offer, and the assistant said the gloves were. Nothing
/// on offer is like what was asked is the answer, and code gives it, not the model.
/// </summary>
public sealed class SearchProductsTool(IQueryDispatcher queries) : ReadTool<SearchForShopperArguments, ProductsForShopper>(queries)
{
    /// <summary>The most products one search hands the model: an answer names four at most.</summary>
    public const int MaxProducts = 12;

    public override ToolAudience Audience => ToolAudience.Shopper;

    public override string Name => "search_products";

    public override string Description =>
        "Search the shop for products, by what the shopper wants. Returns up to 12 products — code, name, brand, category, price, whether it can be bought — and how many there are in all. onlyNearest is true when no product is what was asked, only like it. A product in one of the shop's promotions comes with a fact saying which, and what it takes off. Narrow with category, brand, price, on offer or in stock; on offer, nothing like it on offer returns no products.";

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
        var culture = CultureNegotiation.Resolve(arguments.Culture, default);
        var page = await queries.SendAsync(
            new SearchProductsQuery(arguments.Text, culture, 1, MaxProducts, arguments.Category, ProductSort.Relevance, filters),
            cancellationToken);

        var onlyNearest = page.Answer?.Match == SearchMatch.Related;
        var hits = onlyNearest && arguments.OnOffer ? [] : page.Hits;
        var onOffer = arguments.OnOffer;
        var total = hits.Count == 0 ? 0 : (int)Math.Min(page.Total, int.MaxValue);

        // Nothing like it has its own price cut — but it may be in a promotion on the
        // order, as kitchenware is in the 5 € from 40 €. Asked again without the
        // filter, what matches and is in a promotion is the answer, said with its
        // terms; and "see all" leads to that search. Code decides, not the model.
        if (onlyNearest && arguments.OnOffer)
        {
            var plain = await queries.SendAsync(
                new SearchProductsQuery(arguments.Text, culture, 1, MaxProducts, arguments.Category, ProductSort.Relevance,
                    filters with { OnOffer = false }),
                cancellationToken);
            List<SearchHit> inDeals = plain.Answer?.Match == SearchMatch.Related ? [] : [.. plain.Hits.Where(hit => hit.Deals.Count > 0)];
            if (inDeals.Count > 0)
                (hits, total, onOffer, onlyNearest) = (inDeals, inDeals.Count, false, false);
        }

        var found = new ProductsForShopper(total,
            [
                .. hits.Take(MaxProducts).Select(hit => new ProductForShopper(
                    hit.Code, hit.Name, hit.Brand, hit.Category, hit.Price.Amount, hit.Price.Currency, hit.InStock,
                    hit.Deals.Count == 0 ? null : hit.Deals)),
            ],
            new SearchAsRun(arguments.Text, arguments.Category, arguments.Brand, arguments.MinPrice, arguments.MaxPrice,
                onOffer, arguments.InStock),
            onlyNearest,
            culture);
        return (found, found.Products.Count, total);
    }

    /// <summary>
    /// Each product found on offer is on offer; and when nothing on offer is like
    /// what was asked, that is said of the shop — what "no hay menaje en oferta"
    /// rests on, since there is no card to say it of.
    /// </summary>
    protected override IReadOnlyList<Fact> Facts(ProductsForShopper data) =>
    [
        .. data.Search.OnOffer
            ? data.Products.Count == 0 && data.OnlyNearest
                ? [new Fact(Fact.TheShop, FactSource.Campaign, NoneOnOffer(data.Search.Text, data.Culture))]
                : data.Products.Select(product => new Fact(product.Code, FactSource.Campaign, OnOffer(product.Name, data.Culture)))
            : [],
        // And each promotion a product is in, with its terms (phase 13c).
        .. data.Products.SelectMany(product => (product.Deals ?? [])
            .Select(deal => new Fact(product.Code, FactSource.Campaign, InDeal(product.Name, deal, data.Culture)))),
    ];

    private static string InDeal(string name, ProductDeal deal, string culture) => culture == "en"
        ? $"{name} is in “{deal.Name}”: {DealText.OfAProductInIt(deal.Terms, culture)}."
        : $"{name} entra en «{deal.Name}»: {DealText.OfAProductInIt(deal.Terms, culture)}.";

    private static string OnOffer(string name, string culture) =>
        culture == "en" ? $"{name} is on offer now." : $"{name} está en oferta ahora.";

    private static string NoneOnOffer(string text, string culture) =>
        culture == "en" ? $"Nothing on offer now matches “{text}”." : $"No hay nada en oferta ahora que coincida con «{text}».";

    protected override IReadOnlyList<Subject> Subjects(ProductsForShopper data) =>
        [.. data.Products.Select(product => new Subject(product.Code, product.Name))];
}
