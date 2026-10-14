using System.ComponentModel;
using ElGuerre.Tendero.Catalog.Features.GetProduct;
using ElGuerre.Tendero.Catalog.Features.ListCategories;
using ElGuerre.Tendero.Knowledge.Features.ProductSpecs;
using ElGuerre.Tendero.Ordering.Features.Returns;
using ElGuerre.Tendero.Pricing.Features.ListCampaigns;
using ElGuerre.Tendero.Pricing.Features.ListOffers;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features.BuildKit;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ElGuerre.Tendero.Mcp.Tools;

/// <summary>
/// Every tool the shop offers an external agent, in one file so that a change to
/// the surface is one edit — the rule the storefront's WebMCP tools already
/// follow, for the same reason.
///
/// **Each one is a query record, a dispatch and the answer.** There is no
/// mapping layer and no DTO of its own: the query results are already flat, and
/// giving MCP its own shapes would be a second contract to keep in step with the
/// first. The architecture rule that keeps this honest is in the test project —
/// nothing here may implement a handler or touch a <c>*.Domain</c> namespace, so
/// a tool cannot quietly become logic.
///
/// **Ten: the roadmap's five, the two that stood in for its missing two, the
/// shop's campaigns (R-8, H-5), and its kits (phase 13c, K-7).**
/// <c>get_product_specs</c> and <c>compare_products</c> answer from approved,
/// source-backed claims, so they waited for <c>Knowledge</c> (phase 10) — a
/// comparison tool with no claims cannot say "no data" with any authority, which
/// is its entire argument. Until then two things that existed and that an agent
/// genuinely needs took their place — what is on offer, and how long the shopper
/// has to send it back — and they stay. Same call phase 6 made when
/// <c>apply_filter</c> was dropped for being a tool over facets that did not
/// exist yet.
/// </summary>
[McpServerToolType]
public sealed class TenderoTools
{
    /// <summary>
    /// **The same chain as HTTP, and it is the same chain rather than an
    /// analogue of one.**
    ///
    /// The roadmap assumed this transport had no <c>Accept-Language</c>, and
    /// prescribed a session default captured at <c>initialize</c> to stand in
    /// for it. That was written with stdio in mind. Streamable HTTP is HTTP: the
    /// client's POST carries whatever headers it sets, so the middle link of
    /// ADR 0013's chain is not missing and does not have to be invented.
    /// <c>CultureNegotiation.Resolve</c> is called with both arguments, exactly
    /// as an endpoint calls it — tool parameter, then the header, then <c>es</c>.
    ///
    /// A client that sends no header, or a future stdio deployment where there
    /// is no request at all, falls to the default. That is the chain working,
    /// not a gap in it.
    ///
    /// It is not decoration either. The first version of these tools handed the
    /// caller's raw string to the query, so <c>EN-GB</c> — which every HTTP
    /// endpoint normalises to <c>en</c> before dispatching — would have arrived
    /// at the query as <c>EN-GB</c>. Two transports over one query must not
    /// disagree about what the shop speaks, and the way to guarantee that is to
    /// call the same function rather than to write the same rule twice.
    /// </summary>
    private static string Resolve(string? culture, IHttpContextAccessor requests) =>
        CultureNegotiation.Resolve(
            culture, requests.HttpContext?.Request.Headers.AcceptLanguage);

    /// <summary>
    /// The filters are the storefront's (R-7.3, F-5), under the names the
    /// record gives them, and the answer carries the facets: what the results
    /// can be narrowed by, each option with its code and how many it would
    /// leave. An agent learns what to send from the answer it already has,
    /// without a second call and without guessing a code from a label.
    /// </summary>
    [McpServerTool(Name = "search_products", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        Search the shop's catalogue. Returns products, each carrying the variant that
        matched with its SKU, price and whether it can be bought right now, and the
        facets the results can be narrowed by: each facet's options with the value to
        send back and how many products it would leave. Leave the query empty to
        browse a category instead of searching. Within a filter the choices add up
        (two brands show both); across filters they narrow.
        """)]
    public static Task<Answered<SearchResultPage>> SearchProductsAsync(
        IQueryDispatcher dispatcher,
        IHttpContextAccessor requests,
        [Description("What to search for. Empty browses; one character is refused.")]
        string query = "",
        [Description("Answer in this language: es or en. Anything else falls back to es.")]
        string? culture = null,
        [Description("Category code to browse or narrow to, e.g. KITCHEN.")]
        string? category = null,
        [Description("Newest, PriceAsc or PriceDesc; leave unset for Relevance.")]
        ProductSort? sort = null,
        [Description("1-based page number.")]
        int page = 1,
        [Description("Results per page, 1 to 50.")]
        int pageSize = 20,
        [Description("Brands, as the brand facet spells them, e.g. [\"Vertex\"].")]
        string[]? brands = null,
        [Description("Category codes to narrow a search to, from the category facet.")]
        string[]? within = null,
        [Description("Attribute values as CODE:VALUE, from the facets, e.g. [\"COLOR:BLACK\", \"SIZE:42\"].")]
        string[]? facets = null,
        [Description("Lowest price. " + SearchPrices.InMinorUnits)]
        long? minPrice = null,
        [Description("Highest price. " + SearchPrices.InMinorUnits)]
        long? maxPrice = null,
        [Description("Only products on offer now.")]
        bool onOffer = false,
        [Description("Only products listed in the last 30 days.")]
        bool newArrivals = false,
        [Description("Only products that can be bought now.")]
        bool inStock = false,
        [Description("A campaign's code, e.g. TENDERO_DAYS: only the products its promotions reach while it runs; nothing when it is not running.")]
        string? campaign = null,
        CancellationToken cancellationToken = default)
    {
        var filters = new SearchFilters
        {
            Brands = brands ?? [],
            Within = within ?? [],
            Facets = facets ?? [],
            MinPrice = SearchPrices.FromMinorUnits(minPrice),
            MaxPrice = SearchPrices.FromMinorUnits(maxPrice),
            OnOffer = onOffer,
            NewArrivals = newArrivals,
            InStock = inStock,
            Campaign = string.IsNullOrWhiteSpace(campaign) ? null : campaign.Trim(),
        };

        return Answered.In(Resolve(culture, requests), answered => Refusals.Readable(answered, () => dispatcher.SendAsync(
            new SearchProductsQuery(query, answered, page, pageSize, category, sort ?? ProductSort.Relevance,
                filters.IsEmpty ? null : filters), cancellationToken)));
    }

    /// <summary>
    /// Named <c>get_product</c> until the UCP binding arrived and took that name.
    /// Two tools cannot share one on a single server, and <c>MapMcp</c> offers one
    /// server per process, so the name we chose yields to the name the
    /// specification gave us. This one answers in Tendero's own shape, which is
    /// richer than UCP's Product for a shopper and narrower for an agent.
    /// </summary>
    [McpServerTool(Name = "get_product_detail", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        This shop's own view of one product: its structured attributes, its images, and
        every variant including the ones that are out of stock, each with its SKU.
        Agents speaking UCP should use `get_product` instead.
        """)]
    public static async Task<Answered<ProductDetail>> GetProductAsync(
        IQueryDispatcher dispatcher,
        IHttpContextAccessor requests,
        [Description("The product's public code, as it appears in search results.")]
        string code,
        [Description("Answer in this language: es or en. Anything else falls back to es.")]
        string? culture = null,
        CancellationToken cancellationToken = default)
    {
        var answered = Resolve(culture, requests);

        var product = await Refusals.Readable(answered, () => dispatcher.SendAsync(
            new GetProductQuery(code, answered), cancellationToken));

        // A code nobody owns is a refusal with the code in it, not an empty
        // answer. An agent that gets null guesses; one that gets told will look
        // the product up again.
        return new Answered<ProductDetail>(answered, product
            ?? throw Refusals.Problem(ToolRefusals.ProductNotFound, answered,
                es: $"No hay ningún producto con el código «{code}».", en: $"There is no product with code '{code}'."));
    }

    [McpServerTool(Name = "get_product_specs", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        One product's specs, each value with its source: 'catalogue', or 'description'
        when a person approved it from the product's own text — then with the exact
        words it rests on and how well they support it (0 to 1, measured, not
        declared). Sentences that are not a value come apart, in 'statements'.
        """)]
    public static Task<Answered<SpecSheet>> GetProductSpecsAsync(
        IQueryDispatcher dispatcher,
        IHttpContextAccessor requests,
        [Description("The product's public code, as it appears in search results.")]
        string code,
        [Description("Answer in this language: es or en. Anything else falls back to es.")]
        string? culture = null,
        CancellationToken cancellationToken = default) =>
        SpecsAsync(dispatcher, requests, [code], culture, cancellationToken);

    [McpServerTool(Name = "compare_products", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        Two to four products side by side: one row per attribute, one cell per product,
        in the order given. A cell whose source is 'none' means the shop does not know
        that value for that product — say so; never fill it in. Only values a person
        approved or the catalogue holds appear.
        """)]
    public static Task<Answered<SpecSheet>> CompareProductsAsync(
        IQueryDispatcher dispatcher,
        IHttpContextAccessor requests,
        [Description("Two to four public product codes, as they appear in search results.")]
        string[] codes,
        [Description("Answer in this language: es or en. Anything else falls back to es.")]
        string? culture = null,
        CancellationToken cancellationToken = default)
    {
        if (codes is not { Length: >= 2 })
            throw Refusals.Problem(ToolRefusals.TooFewToCompare, Resolve(culture, requests));

        return SpecsAsync(dispatcher, requests, codes, culture, cancellationToken);
    }

    /// <summary>The one query behind both tools, as behind the product page and the comparison page.</summary>
    private static async Task<Answered<SpecSheet>> SpecsAsync(
        IQueryDispatcher dispatcher, IHttpContextAccessor requests, string[] codes, string? culture, CancellationToken cancellationToken)
    {
        var answered = Resolve(culture, requests);

        var sheet = await Refusals.Readable(answered, () => dispatcher.SendAsync(
            new ProductSpecsQuery([.. codes.Select(code => code.Trim())], answered), cancellationToken));

        // Told, not left empty: an agent handed an empty sheet guesses.
        return sheet.Products.Count == 0
            ? throw Refusals.Problem(ToolRefusals.ProductNotFound, answered,
                es: $"No hay ningún producto con el código {string.Join(" ni ", codes.Select(code => $"«{code}»"))}.",
                en: $"There is no product with code {string.Join(" or ", codes.Select(code => $"'{code}'"))}.")
            : new Answered<SpecSheet>(answered, sheet);
    }

    [McpServerTool(Name = "list_categories", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        The shop's category tree, each entry naming its parent and its full path,
        in the requested language. Use a code from here to browse with search_products.
        """)]
    public static Task<Answered<ListCategoriesResult>> ListCategoriesAsync(
        IQueryDispatcher dispatcher,
        IHttpContextAccessor requests,
        [Description("Answer in this language: es or en. Anything else falls back to es.")]
        string? culture = null,
        CancellationToken cancellationToken = default) =>
        Answered.In(Resolve(culture, requests), answered => Refusals.Readable(answered, () => dispatcher.SendAsync(
            new ListCategoriesQuery(answered), cancellationToken)));

    [McpServerTool(Name = "list_offers", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        The promotions running right now, named in the requested language, each with
        its kind and the date it ends. What actually applies to a basket, and what is
        suppressed and why, is decided when the basket is priced.
        """)]
    public static Task<Answered<ListOffersResult>> ListOffersAsync(
        IQueryDispatcher dispatcher,
        IHttpContextAccessor requests,
        [Description("Answer in this language: es or en. Anything else falls back to es.")]
        string? culture = null,
        CancellationToken cancellationToken = default) =>
        Answered.In(Resolve(culture, requests), answered => Refusals.Readable(answered, () => dispatcher.SendAsync(
            new ListOffersQuery(answered), cancellationToken)));

    /// <summary>
    /// The shop's events and seasons (R-8, H-5): the same query as
    /// <c>GET /api/pricing/campaigns</c>, so an agent asked "are there deals this
    /// week?" reads what the home's card is drawn from rather than scraping it.
    /// </summary>
    [McpServerTool(Name = "list_campaigns", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        The shop's campaigns running now or starting within 30 days, the leading one
        first: each with its name and line in the requested language, its kind (Event
        or Season), its scope (Shop, or one Category), its phase (Running or Upcoming),
        its window (startsAt inclusive, endsAt exclusive) and what it holds — each
        promotion's name and, when one needs a code, the code to type. Browse what a
        running campaign reaches with search_products and its code as `campaign`.
        An empty list means no campaign is near.
        """)]
    public static Task<Answered<ListCampaignsResult>> ListCampaignsAsync(
        IQueryDispatcher dispatcher,
        IHttpContextAccessor requests,
        [Description("Answer in this language: es or en. Anything else falls back to es.")]
        string? culture = null,
        CancellationToken cancellationToken = default) =>
        Answered.In(Resolve(culture, requests), answered => Refusals.Readable(answered, () => dispatcher.SendAsync(
            new ListCampaignsQuery(answered), cancellationToken)));

    /// <summary>
    /// The kits the shop puts together (phase 13c, K-7): the same query as
    /// <c>GET /api/kits</c>, so an agent learns a kit's code and what it can be
    /// asked to work with before it asks for one.
    /// </summary>
    [McpServerTool(Name = "list_kits", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        The kits this shop puts together to a budget, in the requested language: each
        kit's code, its name, its slots (what goes in it, and whether each is needed or
        optional), and its requirements — what the kit can be asked to work with, as
        CODE:VALUE (e.g. INDUCTION:true). Build one with build_kit.
        """)]
    public static Task<Answered<IReadOnlyList<KitTemplateView>>> ListKitsAsync(
        IQueryDispatcher dispatcher,
        IHttpContextAccessor requests,
        [Description("Answer in this language: es or en. Anything else falls back to es.")]
        string? culture = null,
        CancellationToken cancellationToken = default) =>
        Answered.In(Resolve(culture, requests), answered => Refusals.Readable(answered, () => dispatcher.SendAsync(
            new ListKitsQuery(answered), cancellationToken)));

    /// <summary>
    /// A kit solved for a budget (phase 13c, K-7; the spec's decision 7): the
    /// page's query, so an agent buying for somebody gets the arithmetic the
    /// page shows — the total, what is left, what held the kit back, and each
    /// slot with its product or the reason it is empty — as data, not a
    /// paragraph. The budget is in minor units, as the wire answers prices and
    /// as <c>search_products</c> takes them. Pricing it at the basket is the
    /// checkout's job, as on the page.
    /// </summary>
    [McpServerTool(Name = "build_kit", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        Puts one of the shop's kits together for a budget: one product a slot, the best
        ranked that is in stock and fits, never over the budget. Answers the total, what
        is left, what held the kit back (budget, or the shop having nothing for a slot),
        each slot with its product (code, SKU, price) or why it is empty, and the shop's
        own sentences explaining it. Prices are shown prices; promotions apply in the
        basket. Codes and requirements come from list_kits.
        """)]
    public static async Task<Answered<KitView>> BuildKitAsync(
        IQueryDispatcher dispatcher,
        IHttpContextAccessor requests,
        [Description("The kit's code, from list_kits, e.g. KITCHEN_STARTER.")]
        string template,
        [Description("The most the kit may cost. " + SearchPrices.InMinorUnits)]
        long budget,
        [Description("What every fitting product must work with, as CODE:VALUE from the kit's requirements, e.g. [\"INDUCTION:true\"].")]
        string[]? requirements = null,
        [Description("Answer in this language: es or en. Anything else falls back to es.")]
        string? culture = null,
        CancellationToken cancellationToken = default)
    {
        var answered = Resolve(culture, requests);
        var money = new Money(
            SearchPrices.FromMinorUnits(budget, BuildKitHandler.Currency)!.Value, BuildKitHandler.Currency);

        var result = await Refusals.Readable(answered, () => dispatcher.SendAsync(
            new BuildKitQuery(template, money, answered, requirements ?? []), cancellationToken));

        // Told, by name and with where to look, never handed an empty kit.
        return new Answered<KitView>(answered, result.Kit
            ?? throw Refusals.Problem(ToolRefusals.KitNotFound, answered,
                es: $"La tienda no tiene ningún kit «{template}»; list_kits dice cuáles hay.",
                en: $"The shop has no kit '{template}'; list_kits says which there are."));
    }

    [McpServerTool(Name = "get_return_policy", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        How many days a shopper has to send something back. The window runs from
        DELIVERY, not from the day of purchase.
        """)]
    public static Task<ReturnPolicy> GetReturnPolicyAsync(
        IQueryDispatcher dispatcher,
        CancellationToken cancellationToken = default) =>
        Refusals.Readable(CultureNegotiation.Default, () => dispatcher.SendAsync(new ReturnPolicyQuery(), cancellationToken));
}
