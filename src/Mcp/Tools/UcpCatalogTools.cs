using System.ComponentModel;
using System.Text.Json;
using ElGuerre.Tendero.Catalog.Features.GetProduct;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Ucp;
using ElGuerre.Tendero.Ucp.Catalog;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ElGuerre.Tendero.Mcp.Tools;

/// <summary>
/// The catalogue capability's **MCP transport binding**, as the specification
/// defines it — which is a different thing from the shop's own tools next door,
/// and finding that out is what this file is for.
///
/// `TenderoTools` was written from Tendero's queries: `search_products` returns
/// a `SearchResultPage`, because that is the good answer this shop already had.
/// UCP's binding is not a matter of taste — it names `search_catalog`,
/// `lookup_catalog` and `get_product`, requires a `meta` carrying
/// `ucp-agent.profile`, answers with a `ucp` envelope and a `products` array of
/// its own Product shape, and puts money in minor units. Declaring
/// `dev.ucp.shopping` in the manifest while serving our shapes under its tool
/// names would be a lie in the one document whose entire value is being
/// trustworthy.
///
/// So both exist. An agent that speaks UCP uses these three; an agent that just
/// wants this shop uses the ones beside them, which answer things UCP has no
/// capability for — what is on offer, and how long you have to send it back.
///
/// **One collision had to be resolved, and the protocol won.** UCP's binding
/// names a tool `get_product` and so did ours; two tools cannot share a name on
/// one server, and `MapMcp` offers one server per process. Ours is
/// `get_product_detail` now. A name we chose yields to a name we were given.
/// </summary>
[McpServerToolType]
public sealed class UcpCatalogTools
{
    /// <summary>
    /// The specification's own default, and the reason the N+1 below is bounded
    /// rather than open: a search costs at most this many lookups.
    /// </summary>
    private const int DefaultLimit = 10;

    /// <summary>
    /// How many identifiers one lookup may carry. Beyond it the answer is a
    /// JSON-RPC error, which is what the specification asks for — a batch that
    /// is too large is a malformed request, not a business outcome.
    /// </summary>
    private const int MaxBatch = 50;

    /// <summary>The key every request must carry, per the binding.</summary>
    private const string AgentProfile = "ucp-agent.profile";

    [McpServerTool(Name = "search_catalog")]
    [Description("""
        UCP catalog search. Free-text product discovery over this shop's catalogue,
        answering with UCP Product objects. `meta` must carry `ucp-agent.profile`.
        """)]
    public static async Task<UcpCatalogPage> SearchCatalogAsync(
        IQueryDispatcher dispatcher,
        IHttpContextAccessor requests,
        [Description("Agent metadata. Must include the key `ucp-agent.profile`.")]
        IReadOnlyDictionary<string, JsonElement> meta,
        [Description("Free-text search terms. Omit to browse.")]
        string? query = null,
        [Description("Narrowing criteria. `category` is the one this shop honours.")]
        IReadOnlyDictionary<string, string>? filters = null,
        [Description("`limit` (default 10, max 50) and `offset`.")]
        UcpPageRequest? pagination = null,
        CancellationToken cancellationToken = default)
    {
        RequireAgentProfile(meta);

        var limit = Math.Clamp(pagination?.Limit ?? DefaultLimit, 1, 50);
        var offset = Math.Max(pagination?.Offset ?? 0, 0);
        var culture = Culture(requests);

        var categoryCode = filters is not null && filters.TryGetValue("category", out var value)
            ? value
            : null;

        var page = await Refusals.Readable(() => dispatcher.SendAsync(
            new SearchProductsQuery(
                query ?? string.Empty, culture,
                Page: (offset / limit) + 1, PageSize: limit, Category: categoryCode),
            cancellationToken));

        var products = await Details(dispatcher, page.Hits.Select(hit => hit.Code), culture,
            requests, cancellationToken);

        return new UcpCatalogPage
        {
            Ucp = Envelope(),
            Products = products,
            Pagination = new UcpPagination { Limit = limit, Offset = offset, Total = page.Total },

            // A search that found nothing is a business outcome, not a transport
            // failure: the specification is explicit that JSON-RPC errors are for
            // "I could not process this".
            Messages = products.Count > 0
                ? null
                : [new UcpMessage
                {
                    Type = "info",
                    Code = "no_results",
                    Content = "No products matched.",
                    Path = "$.query"
                }]
        };
    }

    [McpServerTool(Name = "lookup_catalog")]
    [Description("""
        UCP catalog lookup. Resolve products by identifier — the codes that appear as
        `id` on a Product. `meta` must carry `ucp-agent.profile`.
        """)]
    public static async Task<UcpCatalogPage> LookupCatalogAsync(
        IQueryDispatcher dispatcher,
        IHttpContextAccessor requests,
        [Description("Product identifiers to resolve.")]
        IReadOnlyList<string> ids,
        [Description("Agent metadata. Must include the key `ucp-agent.profile`.")]
        IReadOnlyDictionary<string, JsonElement> meta,
        CancellationToken cancellationToken = default)
    {
        RequireAgentProfile(meta);

        if (ids.Count > MaxBatch)
        {
            throw new McpException(
                $"A lookup carries at most {MaxBatch} identifiers; this one carried {ids.Count}.");
        }

        var culture = Culture(requests);
        var found = await Details(dispatcher, ids, culture, requests, cancellationToken);
        var missing = ids.Except(found.Select(product => product.Id), StringComparer.Ordinal).ToArray();

        return new UcpCatalogPage
        {
            Ucp = Envelope(),
            Products = found,

            // An id nobody owns is named rather than silently dropped. An agent
            // comparing counts would otherwise have to work out which one it was.
            Messages = missing.Length == 0
                ? null
                : [.. missing.Select(id => new UcpMessage
                {
                    Type = "warning",
                    Code = "not_found",
                    Content = $"There is no product with id '{id}'.",
                    Path = "$.ids"
                })]
        };
    }

    [McpServerTool(Name = "get_product")]
    [Description("""
        UCP product detail: one product with every option and every variant, so an
        agent can narrow to a purchasable SKU. `meta` must carry `ucp-agent.profile`.
        """)]
    public static async Task<UcpProductAnswer> GetProductAsync(
        IQueryDispatcher dispatcher,
        IHttpContextAccessor requests,
        [Description("The product identifier.")]
        string id,
        [Description("Agent metadata. Must include the key `ucp-agent.profile`.")]
        IReadOnlyDictionary<string, JsonElement> meta,
        CancellationToken cancellationToken = default)
    {
        RequireAgentProfile(meta);

        var culture = Culture(requests);

        var detail = await Refusals.Readable(() => dispatcher.SendAsync(
            new GetProductQuery(id, culture), cancellationToken))
            ?? throw new McpException($"There is no product with id '{id}'.");

        return new UcpProductAnswer
        {
            Ucp = Envelope(),
            Product = UcpCatalogMapper.From(detail, BaseUrl(requests))
        };
    }

    /// <summary>
    /// The lookups behind a search, bounded by the page size the specification
    /// defaults to ten. It is an N+1 and it is deliberate: UCP requires a
    /// description on every product and a search hit does not carry one, so the
    /// alternative was widening this shop's own search contract to satisfy
    /// somebody else's protocol.
    /// </summary>
    private static async Task<IReadOnlyList<UcpProduct>> Details(
        IQueryDispatcher dispatcher,
        IEnumerable<string> codes,
        string culture,
        IHttpContextAccessor requests,
        CancellationToken cancellationToken)
    {
        var baseUrl = BaseUrl(requests);
        List<UcpProduct> products = [];

        foreach (var code in codes)
        {
            var detail = await dispatcher.SendAsync(new GetProductQuery(code, culture), cancellationToken);

            if (detail is not null)
                products.Add(UcpCatalogMapper.From(detail, baseUrl));
        }

        return products;
    }

    /// <summary>
    /// A request with no agent profile is malformed, and saying so is more use
    /// than answering it. The binding makes this the one thing every call must
    /// carry, which is how a shop knows who is asking before there is any
    /// authorization at all.
    /// </summary>
    private static void RequireAgentProfile(IReadOnlyDictionary<string, JsonElement> meta)
    {
        if (!meta.ContainsKey(AgentProfile))
            throw new McpException($"`meta` must carry `{AgentProfile}`.");
    }

    private static UcpEnvelope Envelope() => new()
    {
        Version = UcpSpecification.Version,
        RequestId = Guid.CreateVersion7().ToString(),
        Timestamp = DateTimeOffset.UtcNow
    };

    private static string Culture(IHttpContextAccessor requests) =>
        CultureNegotiation.Resolve(null, requests.HttpContext?.Request.Headers.AcceptLanguage);

    private static string BaseUrl(IHttpContextAccessor requests)
    {
        var request = requests.HttpContext?.Request;

        return request is null
            ? string.Empty
            : $"{request.Scheme}://{request.Host}";
    }
}

/// <summary>Cursor-free paging, which is what this shop's search offers.</summary>
public sealed record UcpPageRequest(int? Limit, int? Offset);
