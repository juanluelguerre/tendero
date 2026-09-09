using System.Globalization;
using ElGuerre.Tendero.Catalog.Features.GetProduct;
using ElGuerre.Tendero.Search.Features.SearchProducts;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ucp.Catalog;

/// <summary>
/// The catalogue capability's behaviour, once — and both transports call it.
///
/// **This is the whole reason it exists.** UCP defines a capability and then
/// names the transports it rides on; MCP and REST are two of them, and the
/// difference between them is how arguments arrive and how a refusal is
/// spelled. If each transport did its own paging, its own culture resolution
/// and its own lookup loop, the shop would have two implementations of one
/// protocol and only one of them would be the one the tests happened to run.
///
/// So a transport marshals and maps errors; the semantics are here.
/// </summary>
public static class UcpCatalogOperations
{
    /// <summary>Refused, in the caller's terms. Each transport spells it its own way.</summary>
    public sealed record Refusal(string Reason);

    public static async Task<UcpCatalogPage> SearchAsync(
        IQueryDispatcher dispatcher,
        UcpSearchRequest request,
        string culture,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(
            request.Pagination?.Limit ?? UcpCatalogAnswers.DefaultPageSize,
            1,
            UcpCatalogAnswers.MaxPageSize);

        var offset = Offset(request.Pagination?.Cursor);

        var category = request.Filters is not null && request.Filters.TryGetValue("category", out var value)
            ? value
            : null;

        var page = await dispatcher.SendAsync(
            new SearchProductsQuery(
                request.Query ?? string.Empty, culture,
                Page: (offset / limit) + 1, PageSize: limit, Category: category),
            cancellationToken);

        var details = await DetailsAsync(
            dispatcher, page.Hits.Select(hit => hit.Code), culture, cancellationToken);

        return UcpCatalogAnswers.Search(details, baseUrl, limit, offset, page.Total);
    }

    public static async Task<UcpCatalogPage> LookupAsync(
        IQueryDispatcher dispatcher,
        UcpLookupRequest request,
        string culture,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        var found = await DetailsAsync(dispatcher, request.Ids, culture, cancellationToken);

        return UcpCatalogAnswers.Lookup(found, request.Ids, baseUrl);
    }

    /// <summary>
    /// One product, or nothing. "Nothing" is not a 200 with an empty body: the
    /// response schema REQUIRES <c>product</c>, so there is no valid shape for
    /// a get_product that found none — which is why this is the one catalogue
    /// operation whose miss is not a business outcome.
    /// </summary>
    public static async Task<UcpProductAnswer?> GetProductAsync(
        IQueryDispatcher dispatcher,
        UcpGetProductRequest request,
        string culture,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        var detail = await dispatcher.SendAsync(new GetProductQuery(request.Id, culture), cancellationToken);

        return detail is null ? null : UcpCatalogAnswers.Product(detail, baseUrl);
    }

    /// <summary>How many identifiers one lookup may carry.</summary>
    public const int MaxBatch = 50;

    /// <summary>
    /// What both transports must refuse before they do any work, so that a REST
    /// 400 and an MCP error say the same sentence about the same request.
    /// </summary>
    public static Refusal? Check(IReadOnlyDictionary<string, System.Text.Json.JsonElement>? meta, int? batch = null)
    {
        try
        {
            UcpAgentMetadata.ProfileOf(meta);
        }
        catch (ArgumentException exception)
        {
            return new Refusal(exception.Message);
        }

        return batch > MaxBatch
            ? new Refusal($"A lookup carries at most {MaxBatch} identifiers; this one carried {batch}.")
            : null;
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
    private static async Task<IReadOnlyList<ProductDetail>> DetailsAsync(
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
}
