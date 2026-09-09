using System.ComponentModel;
using System.Text.Json;
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
        Refuse(UcpCatalogOperations.Check(meta));

        var request = new UcpSearchRequest
        {
            Query = query,
            Filters = filters,
            Pagination = pagination,
            Meta = meta?.ToDictionary(pair => pair.Key, pair => pair.Value)
        };

        return await Refusals.Readable(() => UcpCatalogOperations.SearchAsync(
            dispatcher, request, Culture(requests), BaseUrl(requests), cancellationToken));
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
        Refuse(UcpCatalogOperations.Check(meta, ids.Count));

        var request = new UcpLookupRequest
        {
            Ids = ids,
            Meta = meta?.ToDictionary(pair => pair.Key, pair => pair.Value)
        };

        return await Refusals.Readable(() => UcpCatalogOperations.LookupAsync(
            dispatcher, request, Culture(requests), BaseUrl(requests), cancellationToken));
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
        Refuse(UcpCatalogOperations.Check(meta));

        var request = new UcpGetProductRequest
        {
            Id = id,
            Meta = meta?.ToDictionary(pair => pair.Key, pair => pair.Value)
        };

        // Over REST a miss is a 404; over JSON-RPC there is no status line, so
        // it is an error carrying the id. Same operation, two spellings, which
        // is the only thing a transport is allowed to decide.
        return await Refusals.Readable(() => UcpCatalogOperations.GetProductAsync(
            dispatcher, request, Culture(requests), BaseUrl(requests), cancellationToken))
            ?? throw new McpException($"There is no product with id '{id}'.");
    }

    /// <summary>
    /// The binding's own refusals, in this transport's words. What is refused is
    /// decided once in <see cref="UcpCatalogOperations.Check"/>, so a REST 400
    /// and a JSON-RPC error say the same sentence about the same request.
    /// </summary>
    private static void Refuse(UcpCatalogOperations.Refusal? refusal)
    {
        if (refusal is not null)
            throw new McpException(refusal.Reason);
    }

    private static string Culture(IHttpContextAccessor requests) =>
        CultureNegotiation.Resolve(null, requests.HttpContext?.Request.Headers.AcceptLanguage);

    private static string BaseUrl(IHttpContextAccessor requests)
    {
        var request = requests.HttpContext?.Request;

        return request is null ? string.Empty : $"{request.Scheme}://{request.Host}";
    }
}

