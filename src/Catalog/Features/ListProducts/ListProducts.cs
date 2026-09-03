using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Catalog.Features.ListProducts;

/// <summary>
/// Lists the catalogue, filtered by status. It is what was missing for the
/// backoffice review queue to be more than an empty state: the API had five
/// endpoints and none of them let you see what was in Draft, so reviewing an
/// imported catalogue meant opening Postgres.
///
/// It reads from the catalogue, NOT from the index, and that is deliberate: what
/// is under review is precisely what is not indexed yet, because only Active is
/// indexed. A review queue served from Elasticsearch would always be empty.
/// </summary>
public sealed record ListProductsQuery(string? Status, string Culture, int Page, int PageSize)
    : IQuery<ListProductsResult>;

public sealed record ProductSummary(
    string ProductId,
    string Code,
    string Name,
    string Slug,
    string? Brand,
    string? Category,
    decimal PriceAmount,
    string PriceCurrency,
    string? ImageId,
    string Status,
    IReadOnlyList<string> MissingCultures,
    DateTimeOffset UpdatedAt);

public sealed record ListProductsResult(
    IReadOnlyList<ProductSummary> Items, int Total, int Page, int PageSize);

public sealed class ListProductsValidator : AbstractValidator<ListProductsQuery>
{
    public ListProductsValidator()
    {
        // An unknown status is rejected rather than ignored: filtering by
        // "pending" and getting the whole catalogue back is worse than a 400,
        // because it looks like it worked.
        RuleFor(x => x.Status)
            .Must(status => status is null || Enum.TryParse<ProductStatus>(status, ignoreCase: true, out _))
            .WithMessage("Status must be one of: draft, active, archived.");

        RuleFor(x => x.Culture).Must(c => c is "es" or "en")
            .WithMessage("Supported cultures: es, en.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class ListProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/catalog/products?status=draft&culture=es&page=1&pageSize=20
        // The return type is declared on the lambda rather than with
        // .Produces<T>(): that way the compiler is what keeps the OpenAPI
        // document in step with what the endpoint actually returns. A
        // .Produces<T>() can go on lying after a change and nobody finds out.
        app.MapGet("/api/catalog/products",
            async Task<Ok<ListProductsResult>> (
                   string? status, string? culture, int? page, int? pageSize,
                   HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                // Culture: explicit query parameter > Accept-Language > "es".
                //
                // The parameter wins because a URL carrying a language is
                // shareable, cacheable and the ONLY route for a UCP agent, which
                // has no browser locale. The header decides when there is no
                // parameter, which is what makes somebody who arrives without
                // asking see their language and not ours.
                //
                // It was copied from /api/search under a comment saying when
                // that would stop being fine: "with a third it moves to a shared
                // binder". Price quoting was the third.
                var resolved = CultureNegotiation.Resolve(
                    culture, http.Request.Headers.AcceptLanguage);

                var result = await dispatcher.SendAsync(
                    new ListProductsQuery(status, resolved, page ?? 1, pageSize ?? 20), ct);

                // The client asks, the server declares. Vary because the response
                // depends on Accept-Language when the parameter is absent.
                //
                // It declares the NEGOTIATED culture, not each row's:
                // LocalizedText falls back in a chain, so a product with no
                // English served in "en" comes back in Spanish. A header cannot
                // say "this row yes and that one no"; that is what per-item
                // `missingCultures` carries.
                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return TypedResults.Ok(result);
            })
            .AllowAnonymous()   // the catalogue is public, and this says so in code
            .WithTags("Catalog")
            .WithName("ListProducts");
    }
}

public sealed class ListProductsHandler(IProductCatalogReader products)
    : IQueryHandler<ListProductsQuery, ListProductsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);
    private static readonly string[] Cultures = ["es", "en"];

    public async Task<ListProductsResult> HandleAsync(
        ListProductsQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.list");
        activity?.SetTag("catalog.status", query.Status);

        var status = query.Status is null
            ? (ProductStatus?)null
            : Enum.Parse<ProductStatus>(query.Status, ignoreCase: true);

        var page = await products.ListAsync(status, query.Page, query.PageSize, cancellationToken);

        activity?.SetTag("catalog.total", page.Total);

        return new ListProductsResult(
            [.. page.Items.Select(product => Summarise(product, query.Culture))],
            page.Total,
            query.Page,
            query.PageSize);
    }

    private static ProductSummary Summarise(Product product, string culture) =>
        new(product.Id.Value.ToString(),
            product.Code,
            product.Name.In(culture),
            product.Slug.In(culture),
            product.Brand,
            product.Category,
            product.Price.Amount,
            product.Price.Currency,
            product.PrimaryImage?.Id.Value,
            product.Status.ToString().ToLowerInvariant(),
            // Which languages the product is missing, resolved here and not in
            // the client: it is the fact the queue exists for. LocalizedText
            // falls back in a chain (culture -> en -> first), so without this the
            // record looks complete in English and nobody notices Spanish is gone.
            [.. Cultures.Where(c => !product.Name.Cultures.Contains(c))],
            product.UpdatedAt);
}
