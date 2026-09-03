using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Catalog.Features.DescribeSkus;

/// <summary>
/// What a set of SKUs is called.
///
/// **This slice exists because of a boundary, and it is the boundary working
/// rather than leaking.** The backoffice's stock grid shows rows like
/// `B05DEFG606-DEFAULT`, which tells a shopkeeper nothing — but
/// `GET /api/inventory/stock` cannot add a product name, because `Inventory`
/// may reference the SharedKernel and nothing else. Stock exists without a
/// catalogue exactly as it exists without orders: goods arrive, shelves are
/// counted, and none of that needs a product to have been published.
///
/// The wrong fix is to let Inventory ask Catalog, which would make the general
/// thing depend on the specific one and turn an architecture rule red. The right
/// one is that the SCREEN asks both — the catalogue says what a SKU is, the
/// ledger says how many there are, and the two answers meet where a person is
/// looking at them.
///
/// It is the same crossing the product page makes, pointed the other way, and
/// the direction is what makes it safe.
/// </summary>
public sealed record DescribeSkusQuery(IReadOnlyList<string> Skus, string Culture)
    : IQuery<DescribeSkusResult>;

public sealed record DescribeSkusResult(IReadOnlyList<SkuDescriptionView> Items);

public sealed record SkuDescriptionView(
    string Sku,
    string ProductCode,
    string ProductName,
    string? VariantLabel,
    string Slug);

public sealed class DescribeSkusValidator : AbstractValidator<DescribeSkusQuery>
{
    /// <summary>
    /// A page of a stock grid, with room to spare. The cap is here because the
    /// SKUs arrive in the query string: without one, a caller could ask about
    /// the whole catalogue in a URL long enough to be its own problem.
    /// </summary>
    private const int MaximumSkus = 200;

    public DescribeSkusValidator()
    {
        RuleFor(query => query.Skus)
            .NotEmpty().WithMessage("At least one sku is required.")
            .Must(skus => skus.Count <= MaximumSkus)
                .WithMessage($"At most {MaximumSkus} skus per request.");

        RuleFor(query => query.Culture).Must(culture => culture is "es" or "en")
            .WithMessage("Supported cultures: es, en.");
    }
}

public sealed class DescribeSkusEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/catalog/skus?sku=A&sku=B&culture=es
        app.MapGet("/api/catalog/skus",
            async Task<Ok<DescribeSkusResult>> (
                   string[] sku, string? culture,
                   HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(
                    culture, http.Request.Headers.AcceptLanguage);

                var result = await dispatcher.SendAsync(
                    new DescribeSkusQuery(sku, resolved), ct);

                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return TypedResults.Ok(result);
            })
            // Shopkeeper, and it is a decision rather than caution. The names
            // themselves are public — the storefront shows them — but turning a
            // list of SKUs into catalogue rows is an inventory-shaped question,
            // and the only screen that asks it is already behind this policy.
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Catalog")
            .WithName("DescribeSkus");
    }
}

public sealed class DescribeSkusHandler(IProductCatalogReader products)
    : IQueryHandler<DescribeSkusQuery, DescribeSkusResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);

    public async Task<DescribeSkusResult> HandleAsync(
        DescribeSkusQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.describe_skus");
        activity?.SetTag("catalog.skus", query.Skus.Count);

        var descriptions = await products.DescribeSkusAsync(
            query.Skus, query.Culture, cancellationToken);

        // A SKU the catalogue does not know is simply absent from the answer,
        // and the caller is expected to notice. Returning a row with an empty
        // name would say "this product is called nothing", which is a different
        // and less true statement than "the catalogue has never heard of this".
        activity?.SetTag("catalog.skus_described", descriptions.Count);

        return new DescribeSkusResult(
            [.. descriptions.Select(description => new SkuDescriptionView(
                description.Sku,
                description.ProductCode,
                description.ProductName,
                description.VariantLabel,
                description.Slug))]);
    }
}
