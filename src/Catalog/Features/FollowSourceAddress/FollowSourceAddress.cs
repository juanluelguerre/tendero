using System.Diagnostics;
using System.Diagnostics.Metrics;
using Carter;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Catalog.Features.FollowSourceAddress;

/// <summary>What an old address of a store named.</summary>
public enum SourceAddressKind
{
    Product,
    Collection,
}

/// <summary>Where a store's old address leads in the storefront, or null when nowhere.</summary>
public sealed record FollowSourceAddressQuery(SourceAddressKind Kind, string Handle, string Culture) : IQuery<string?>;

/// <summary>
/// A store's old addresses, answered after the shop has left it (phase 16,
/// P16-11; docs/specs/shopify-leave.md, decision 7): Shopify's
/// <c>/products/&lt;handle&gt;</c>, the same under a collection, and
/// <c>/collections/&lt;handle&gt;</c>, with or without the language in front as
/// a store with two languages writes them. Each is a 301 to the storefront's
/// page, from what the import kept; the storefront's host forwards these paths
/// here, as its development server does.
///
/// Not part of the API's document: they are addresses for browsers and
/// crawlers, never called by a client, and the document is the contract a
/// client calls and the frontends' types are generated from.
/// </summary>
public sealed class FollowSourceAddressEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        foreach (var prefix in new[] { "", "/{culture:regex(^(es|en)$)}" })
        {
            Map(app, $"{prefix}/products/{{handle}}", SourceAddressKind.Product);
            Map(app, $"{prefix}/collections/{{collection}}/products/{{handle}}", SourceAddressKind.Product);
            Map(app, $"{prefix}/collections/{{handle}}", SourceAddressKind.Collection);
        }
    }

    private static void Map(IEndpointRouteBuilder app, string pattern, SourceAddressKind kind) =>
        app.MapGet(pattern,
                async Task<Results<RedirectHttpResult, NotFound>> (
                    string handle, string? culture, HttpContext http,
                    IQueryDispatcher dispatcher, CancellationToken ct) =>
                {
                    var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                    // The answer depends on the language when the path does not carry it.
                    http.Response.Headers.Vary = "Accept-Language";

                    var location = await dispatcher.SendAsync(new FollowSourceAddressQuery(kind, handle, resolved), ct);
                    SourceAddressMetrics.Record(kind, location is not null);

                    return location is null
                        ? TypedResults.NotFound()
                        : TypedResults.Redirect(location, permanent: true);
                })
            .AllowAnonymous()   // an old address is public, as the page it leads to
            .ExcludeFromDescription()
            .WithTags("Catalog");
}

public sealed class FollowSourceAddressHandler(ISourceAddresses addresses)
    : IQueryHandler<FollowSourceAddressQuery, string?>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);

    public async Task<string?> HandleAsync(FollowSourceAddressQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.source_address");
        activity?.SetTag("catalog.address.kind", query.Kind.ToString());
        activity?.SetTag("catalog.address.handle", query.Handle);

        // Shopify answers a handle in any case; the import keeps them as the store writes them, lowercase.
        var handle = query.Handle.ToLowerInvariant();

        return query.Kind switch
        {
            SourceAddressKind.Product => await addresses.ProductAsync(handle, cancellationToken) is { Status: ProductStatus.Active } product
                // Only to a page that is there: a permanent redirect to a 404 is
                // cached by every browser and crawler that follows it.
                ? $"/{query.Culture}/p/{product.Slug.In(query.Culture)}/{product.Code}"
                : null,
            SourceAddressKind.Collection => await addresses.CategoryAsync(handle, cancellationToken) is { } category
                ? $"/{query.Culture}/c/{category}"
                : null,
        };
    }
}

/// <summary>
/// `tendero.catalog.source_addresses` (the tracking plan): are a store's old
/// addresses still visited after the shop left it, and do they still lead
/// somewhere? When the redirected count falls to nothing, the table can go.
/// </summary>
internal static class SourceAddressMetrics
{
    private static readonly Meter Meter = new(TelemetrySources.Catalog);

    private static readonly Counter<long> Followed = Meter.CreateCounter<long>(
        "tendero.catalog.source_addresses", unit: "{request}", description: "A store's old addresses asked for, by kind and whether they led somewhere.");

    public static void Record(SourceAddressKind kind, bool redirected) =>
        Followed.Add(
            1,
            new KeyValuePair<string, object?>("catalog.address.kind", kind == SourceAddressKind.Product ? "product" : "collection"),
            new KeyValuePair<string, object?>("catalog.address.outcome", redirected ? "redirected" : "missing"));
}
