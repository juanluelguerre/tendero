using ElGuerre.Tendero.Catalog.Features.GetProduct;

namespace ElGuerre.Tendero.Ucp.Catalog;

/// <summary>
/// The response envelopes the catalogue binding returns.
///
/// They live here rather than in the transport for one reason: they are the
/// protocol's shape, and the protocol's shape is what this project can be wrong
/// about. Composing them in a pure method means the answers can be validated
/// against UCP's own JSON Schemas with no database, no server and no MCP client
/// — which is what turned three confident guesses into three corrections.
/// </summary>
public static class UcpCatalogAnswers
{
    /// <summary>What the specification recommends when a caller asks for nothing.</summary>
    public const int DefaultPageSize = 10;

    /// <summary>What this shop will return however much is asked for.</summary>
    public const int MaxPageSize = 50;

    private static UcpEnvelope Envelope => new() { Version = UcpSpecification.Version };

    public static UcpCatalogPage Search(
        IEnumerable<ProductDetail> products,
        string baseUrl,
        int limit,
        int offset,
        long total,
        IReadOnlyList<UcpMessage> notices)
    {
        var mapped = products.Select(product => UcpCatalogMapper.From(product, baseUrl)).ToArray();
        var next = offset + limit;

        return new UcpCatalogPage
        {
            Ucp = Envelope,
            Products = mapped,
            Pagination = new UcpPagination
            {
                HasNextPage = next < total,

                // "MUST be present when has_next_page is true", and absent
                // otherwise — a cursor to a page that does not exist is an
                // invitation to a round trip that returns nothing.
                Cursor = next < total ? next.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                TotalCount = total
            },

            // A search that found nothing is a business outcome. The
            // specification draws that line itself: JSON-RPC errors are for "I
            // could not process this". Notices (no exact match, a help topic)
            // travel alongside products when there are any; when there are none,
            // "no_exact_match" is dropped — "here are the closest matches"
            // beside a page with nothing on it contradicts the "no_results"
            // message that follows it — while "help_available" still says
            // something true and travels with it.
            Messages = mapped.Length > 0
                ? (notices.Count > 0 ? notices : null)
                :
                [
                    .. notices.Where(notice => notice.Code != "no_exact_match"),
                    new UcpMessage
                    {
                        Type = "info",
                        Code = "no_results",
                        Content = "No products matched.",
                        Path = "$.query"
                    }
                ]
        };
    }

    public static UcpCatalogPage Lookup(
        IReadOnlyList<ProductDetail> found,
        IEnumerable<string> asked,
        string baseUrl)
    {
        // A lookup answer's variants MUST say which requested identifier they
        // resolved to. Every one here is `featured`: the caller named a product
        // and got its variants, which is precisely the case the specification
        // reserves that word for.
        var mapped = found
            .Select(product => UcpCatalogMapper.From(product, baseUrl))
            .Select(product => product with
            {
                Variants =
                [
                    .. product.Variants.Select(variant => variant with
                    {
                        Inputs = [new UcpInputCorrelation { Id = product.Id, Match = "featured" }]
                    })
                ]
            })
            .ToArray();

        var missing = asked
            .Except(mapped.Select(product => product.Id), StringComparer.Ordinal)
            .ToArray();

        return new UcpCatalogPage
        {
            Ucp = Envelope,
            Products = mapped,

            // An id nobody owns is named rather than silently dropped: an agent
            // comparing counts would otherwise have to work out which one.
            Messages = missing.Length == 0
                ? null
                :
                [
                    .. missing.Select(id => new UcpMessage
                    {
                        Type = "warning",
                        Code = "not_found",
                        Content = $"There is no product with id '{id}'.",
                        Path = "$.ids"
                    })
                ]
        };
    }

    public static UcpProductAnswer Product(ProductDetail product, string baseUrl) => new()
    {
        Ucp = Envelope,
        Product = UcpCatalogMapper.From(product, baseUrl)
    };
}
