using System.ComponentModel;
using ElGuerre.Tendero.Catalog.Features.GetProduct;
using ElGuerre.Tendero.Catalog.Features.ListCategories;
using ElGuerre.Tendero.Ordering.Features.Returns;
using ElGuerre.Tendero.Pricing.Features.ListOffers;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using ElGuerre.Tendero.SharedKernel;
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
/// **Five, and two of the roadmap's five are not among them.**
/// <c>get_product_specs</c> and <c>compare_products</c> answer from approved,
/// source-backed claims, and <c>Knowledge</c> is phase 10 — a comparison tool
/// with no claims cannot say "no data" with any authority, which is its entire
/// argument. They are replaced by two things that exist and that an agent
/// genuinely needs: what is on offer, and how long the shopper has to send it
/// back. Same call phase 6 made when <c>apply_filter</c> was dropped for being a
/// tool over facets that did not exist yet.
/// </summary>
[McpServerToolType]
public sealed class TenderoTools
{
    /// <summary>
    /// The default when a tool is not told which language to answer in. It is
    /// the last link of ADR 0013's chain, and it is written here as a literal
    /// only because a parameter default has to be a compile-time constant;
    /// <see cref="Resolve"/> is what actually applies the chain.
    /// </summary>
    private const string DefaultCulture = CultureNegotiation.Default;

    /// <summary>
    /// The same chain every HTTP endpoint runs, minus the link this transport
    /// does not have: there is no <c>Accept-Language</c> over JSON-RPC. The
    /// middle link — the language a session declares when it says hello — is
    /// P9-4, and it goes in here.
    ///
    /// It is not decoration. The first version of these tools handed the
    /// caller's raw string to the query, so <c>EN-GB</c> — which every HTTP
    /// endpoint normalises to <c>en</c> before dispatching — would have arrived
    /// at the query as <c>EN-GB</c>, and a language the shop does not speak
    /// would have reached a handler instead of falling back. Two transports over
    /// one query must not disagree about what the shop speaks, and the way to
    /// guarantee that is to call the same function rather than to write the same
    /// rule twice.
    /// </summary>
    private static string Resolve(string? culture) => CultureNegotiation.Resolve(culture);

    [McpServerTool(Name = "search_products")]
    [Description("""
        Search the shop's catalogue. Returns products, each carrying the variant that
        matched with its SKU, price and whether it can be bought right now.
        Leave the query empty to browse a category instead of searching.
        """)]
    public static Task<SearchResultPage> SearchProductsAsync(
        IQueryDispatcher dispatcher,
        [Description("What to search for. Empty browses; one character is refused.")]
        string query = "",
        [Description("Answer in this language: es or en. Anything else falls back to es.")]
        string culture = DefaultCulture,
        [Description("Category code to browse or narrow to, e.g. KITCHEN.")]
        string? category = null,
        [Description("Order by 'newest', or leave unset for relevance.")]
        string? sort = null,
        [Description("1-based page number.")]
        int page = 1,
        [Description("Results per page, 1 to 50.")]
        int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Refusals.Readable(() => dispatcher.SendAsync(
            new SearchProductsQuery(query, Resolve(culture), page, pageSize, category, sort), cancellationToken));

    [McpServerTool(Name = "get_product")]
    [Description("""
        Everything about one product: its structured attributes, its images, and every
        variant it sells including the ones that are out of stock, each with its SKU.
        This is what says which SKUs exist.
        """)]
    public static async Task<ProductDetail> GetProductAsync(
        IQueryDispatcher dispatcher,
        [Description("The product's public code, as it appears in search results.")]
        string code,
        [Description("Answer in this language: es or en. Anything else falls back to es.")]
        string culture = DefaultCulture,
        CancellationToken cancellationToken = default)
    {
        var product = await Refusals.Readable(() => dispatcher.SendAsync(
            new GetProductQuery(code, Resolve(culture)), cancellationToken));

        // A code nobody owns is a refusal with the code in it, not an empty
        // answer. An agent that gets null guesses; one that gets told will look
        // the product up again.
        return product
            ?? throw new McpException($"There is no product with code '{code}'.");
    }

    [McpServerTool(Name = "list_categories")]
    [Description("""
        The shop's category tree, each entry naming its parent and its full path,
        in the requested language. Use a code from here to browse with search_products.
        """)]
    public static Task<ListCategoriesResult> ListCategoriesAsync(
        IQueryDispatcher dispatcher,
        [Description("Answer in this language: es or en. Anything else falls back to es.")]
        string culture = DefaultCulture,
        CancellationToken cancellationToken = default) =>
        Refusals.Readable(() => dispatcher.SendAsync(
            new ListCategoriesQuery(Resolve(culture)), cancellationToken));

    [McpServerTool(Name = "list_offers")]
    [Description("""
        The promotions running right now, named in the requested language, each with
        its kind and the date it ends. What actually applies to a basket, and what is
        suppressed and why, is decided when the basket is priced.
        """)]
    public static Task<ListOffersResult> ListOffersAsync(
        IQueryDispatcher dispatcher,
        [Description("Answer in this language: es or en. Anything else falls back to es.")]
        string culture = DefaultCulture,
        CancellationToken cancellationToken = default) =>
        Refusals.Readable(() => dispatcher.SendAsync(
            new ListOffersQuery(Resolve(culture)), cancellationToken));

    [McpServerTool(Name = "get_return_policy")]
    [Description("""
        How many days a shopper has to send something back. The window runs from
        DELIVERY, not from the day of purchase.
        """)]
    public static Task<ReturnPolicy> GetReturnPolicyAsync(
        IQueryDispatcher dispatcher,
        CancellationToken cancellationToken = default) =>
        Refusals.Readable(() => dispatcher.SendAsync(new ReturnPolicyQuery(), cancellationToken));
}
