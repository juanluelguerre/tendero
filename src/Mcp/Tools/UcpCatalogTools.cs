using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ElGuerre.Tendero.Catalog.Features.GetProduct;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Ucp.Catalog;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ElGuerre.Tendero.Mcp.Tools;

/// <summary>
/// The catalogue capability's **MCP transport binding**, as the specification
/// defines it — a different contract from the shop's own tools next door.
///
/// `TenderoTools` was written from Tendero's queries and answers its shapes. UCP
/// fixes the tool names, the argument names, the response envelope and the units
/// money is in. Declaring `dev.ucp.shopping` in the manifest while serving our
/// shapes under its names would be a lie in the one document whose entire value
/// is being trustworthy — so both exist, and an agent picks.
///
/// **One collision had to be resolved, and the protocol won.** UCP names a tool
/// `get_product` and so did ours; two cannot share a name on one server and
/// `MapMcp` offers one server per process. Ours is `get_product_detail` now.
///
/// **`UseStructuredContent` is not a nicety.** The MCP transport envelope
/// requires the UCP payload in `result.structuredContent`; `result.content` is
/// compatibility output. Without it the answers were well-formed MCP and not
/// UCP, which is the shape of every mistake in this file's history: plausible,
/// and wrong in a way only the schema says out loud.
/// </summary>
[McpServerToolType]
public sealed class UcpCatalogTools
{
    /// <summary>How many identifiers one lookup may carry.</summary>
    private const int MaxBatch = 50;

    [McpServerTool(Name = "search_catalog", UseStructuredContent = true)]
    [Description("""
        UCP catalog search. Free-text product discovery, answering with UCP Product
        objects and cursor pagination. All arguments are optional.
        """)]
    public static async Task<UcpCatalogPage> SearchCatalogAsync(
        IQueryDispatcher dispatcher,
        IHttpContextAccessor requests,
        [Description("Free-text search terms. Omit to browse.")]
        string? query = null,
        [Description("Narrowing criteria. `category` is the one this shop honours.")]
        IReadOnlyDictionary<string, string>? filters = null,
        [Description("`limit` (default 10, max 50) and `cursor` from a previous page.")]
        UcpPageRequest? pagination = null,
        [Description("Transport metadata. Optional; when present, `ucp-agent` must carry a `profile`.")]
        IReadOnlyDictionary<string, JsonElement>? meta = null,
        CancellationToken cancellationToken = default)
    {
        Refusals.Guard(() => UcpAgentMetadata.ProfileOf(meta));

        var limit = Math.Clamp(pagination?.Limit ?? UcpCatalogAnswers.DefaultPageSize, 1, UcpCatalogAnswers.MaxPageSize);
        var offset = Offset(pagination?.Cursor);
        var culture = Culture(requests);

        var categoryCode = filters is not null && filters.TryGetValue("category", out var value)
            ? value
            : null;

        var page = await Refusals.Readable(() => dispatcher.SendAsync(
            new SearchProductsQuery(
                query ?? string.Empty, culture,
                Page: (offset / limit) + 1, PageSize: limit, Category: categoryCode),
            cancellationToken));

        var details = await Details(dispatcher, page.Hits.Select(hit => hit.Code), culture, cancellationToken);

        return UcpCatalogAnswers.Search(details, BaseUrl(requests), limit, offset, page.Total);
    }

    [McpServerTool(Name = "lookup_catalog", UseStructuredContent = true)]
    [Description("""
        UCP catalog lookup. Resolve products by identifier — the values that appear as
        `id` on a Product. Identifiers nobody owns come back as warnings, not errors.
        """)]
    public static async Task<UcpCatalogPage> LookupCatalogAsync(
        IQueryDispatcher dispatcher,
        IHttpContextAccessor requests,
        [Description("Product identifiers to resolve.")]
        IReadOnlyList<string> ids,
        [Description("Transport metadata. Optional; when present, `ucp-agent` must carry a `profile`.")]
        IReadOnlyDictionary<string, JsonElement>? meta = null,
        CancellationToken cancellationToken = default)
    {
        Refusals.Guard(() => UcpAgentMetadata.ProfileOf(meta));

        if (ids.Count > MaxBatch)
        {
            throw new McpException(
                $"A lookup carries at most {MaxBatch} identifiers; this one carried {ids.Count}.");
        }

        var culture = Culture(requests);
        var found = await Details(dispatcher, ids, culture, cancellationToken);

        return UcpCatalogAnswers.Lookup(found, ids, BaseUrl(requests));
    }

    [McpServerTool(Name = "get_product", UseStructuredContent = true)]
    [Description("""
        UCP product detail: one product with every option and every variant, so an
        agent can narrow to a purchasable SKU.
        """)]
    public static async Task<UcpProductAnswer> GetProductAsync(
        IQueryDispatcher dispatcher,
        IHttpContextAccessor requests,
        [Description("The product identifier.")]
        string id,
        [Description("Transport metadata. Optional; when present, `ucp-agent` must carry a `profile`.")]
        IReadOnlyDictionary<string, JsonElement>? meta = null,
        CancellationToken cancellationToken = default)
    {
        Refusals.Guard(() => UcpAgentMetadata.ProfileOf(meta));

        var detail = await Refusals.Readable(() => dispatcher.SendAsync(
            new GetProductQuery(id, Culture(requests)), cancellationToken))
            ?? throw new McpException($"There is no product with id '{id}'.");

        return UcpCatalogAnswers.Product(detail, BaseUrl(requests));
    }

    /// <summary>
    /// The cursor this shop hands out is the next offset. Opaque by contract,
    /// legible by accident, and anything unparseable starts at the beginning
    /// rather than failing — a cursor is the agent quoting us back to ourselves,
    /// and a shop should not error because it changed its own paging.
    /// </summary>
    private static int Offset(string? cursor) =>
        int.TryParse(cursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset) && offset > 0
            ? offset
            : 0;

    /// <summary>
    /// The lookups behind a search, bounded by the page size — ten by default,
    /// fifty at most. UCP requires a description on every Product and a search
    /// hit does not carry one, so the alternative was widening this shop's own
    /// search contract to satisfy somebody else's protocol.
    /// </summary>
    private static async Task<IReadOnlyList<ProductDetail>> Details(
        IQueryDispatcher dispatcher,
        IEnumerable<string> codes,
        string culture,
        CancellationToken cancellationToken)
    {
        List<ProductDetail> found = [];

        foreach (var code in codes)
        {
            var detail = await dispatcher.SendAsync(new GetProductQuery(code, culture), cancellationToken);

            if (detail is not null)
                found.Add(detail);
        }

        return found;
    }

    private static string Culture(IHttpContextAccessor requests) =>
        CultureNegotiation.Resolve(null, requests.HttpContext?.Request.Headers.AcceptLanguage);

    private static string BaseUrl(IHttpContextAccessor requests)
    {
        var request = requests.HttpContext?.Request;

        return request is null ? string.Empty : $"{request.Scheme}://{request.Host}";
    }
}

/// <summary>
/// Pagination as the specification asks for it in a REQUEST: an opaque cursor
/// and a requested page size.
/// </summary>
public sealed record UcpPageRequest(int? Limit, string? Cursor);
