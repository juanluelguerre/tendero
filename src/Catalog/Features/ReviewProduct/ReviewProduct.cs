using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Catalog.Rendering;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Catalog.Features.ReviewProduct;

/// <summary>
/// A product as the shopkeeper reviews it before it goes on sale (the
/// redesign's R-4.3): **every culture side by side**, whatever its status.
///
/// The product page cannot be this. It serves only what is Active — a draft is a
/// 404 to the public, on purpose — and it resolves one culture, falling back
/// along a chain, so a product with no English reads as complete in English.
/// Reviewing means seeing exactly what is missing and exactly what would be
/// published, so this answers with the texts as stored, per culture, and is the
/// shopkeeper's alone.
/// </summary>
public sealed record ReviewProductQuery(string Code, string Culture) : IQuery<ProductForReview?>;

/// <summary>What a person approves: the texts per culture, the images, the attributes, the price.</summary>
public sealed record ProductForReview(
    string ProductId,
    string Code,
    ProductStatus Status,
    /// <summary>Culture to text, as stored: a culture that is absent is missing.</summary>
    IReadOnlyDictionary<string, string> Names,
    IReadOnlyDictionary<string, string> Descriptions,
    string? Brand,
    /// <summary>The category's name in the requested culture, or its code when the tree does not know it.</summary>
    string? Category,
    decimal PriceAmount,
    string PriceCurrency,
    IReadOnlyList<string> ImageIds,
    IReadOnlyList<ProductAttributeView> Attributes,
    IReadOnlyList<string> MissingCultures,
    DateTimeOffset UpdatedAt);

public sealed class ReviewProductValidator : AbstractValidator<ReviewProductQuery>
{
    public ReviewProductValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.Culture).Must(c => c is "es" or "en").WithMessage("Supported cultures: es, en.");
    }
}

public sealed class ReviewProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/catalog/products/{code}/review?culture=es
        app.MapGet("/api/catalog/products/{code}/review",
            async Task<Results<Ok<ProductForReview>, NotFound>> (
                   string code, string? culture, HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                var product = await dispatcher.SendAsync(new ReviewProductQuery(code, resolved), ct);

                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return product is null ? TypedResults.NotFound() : TypedResults.Ok(product);
            })
            // Drafts are in it, so it is the shopkeeper's, as the list is.
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Catalog")
            .WithName("ReviewProduct");
    }
}

public sealed class ReviewProductHandler(
    IProductCatalogReader products,
    IAttributeDefinitionReader definitions,
    ICategoryReader categories)
    : IQueryHandler<ReviewProductQuery, ProductForReview?>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);
    private static readonly string[] Cultures = ["es", "en"];

    public async Task<ProductForReview?> HandleAsync(ReviewProductQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.review_product");
        activity?.SetTag("catalog.product_code", query.Code);

        var product = await products.FindByCodeAsync(query.Code, cancellationToken);
        if (product is null)
            return null;

        activity?.SetTag("catalog.status", product.Status.ToString());

        var attributes = await definitions.AllAsync(cancellationToken);
        var tree = await categories.AllAsync(cancellationToken);

        return new ProductForReview(
            product.Id.Value.ToString(),
            product.Code,
            product.Status,
            AsStored(product.Name),
            product.Description is { } description ? AsStored(description) : new Dictionary<string, string>(),
            product.Brand,
            product.Category is { } category ? tree.ByCode(category)?.Name.In(query.Culture) ?? category : null,
            product.Price.Amount,
            product.Price.Currency,
            [.. product.Images.OrderBy(image => image.SortOrder).Select(image => image.Id.Value)],
            // Every value, a defined one by its label and an undefined one by its
            // code: a review hides nothing the product carries.
            [.. product.Attributes.Select(value => ProductAttributes.Render(value, attributes.ByCode(value.Code), query.Culture))],
            [.. Cultures.Where(culture => !product.Name.Cultures.Contains(culture))],
            product.UpdatedAt);
    }

    /// <summary>The texts as stored, per culture, with no fallback: an absence is the finding.</summary>
    private static Dictionary<string, string> AsStored(LocalizedText text) =>
        text.Cultures.ToDictionary(culture => culture, culture => text.In(culture), StringComparer.Ordinal);
}
