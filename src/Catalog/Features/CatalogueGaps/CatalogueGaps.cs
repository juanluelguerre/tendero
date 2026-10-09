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

namespace ElGuerre.Tendero.Catalog.Features.CatalogueGaps;

/// <summary>
/// What the catalogue lacks (phase 12, S-3a). *Missing a required attribute*
/// joined in phase 13a, once a category could say what a product must have
/// (ADR 0056, amended).
/// </summary>
public enum CatalogueGap
{
    /// <summary>The name has nothing in a culture.</summary>
    MissingName,

    MissingImage,

    /// <summary>A picture whose alternative text has nothing in a culture.</summary>
    MissingImageAlt,

    DraftWaiting,

    /// <summary>An attribute its category requires and it has no value for: an incomplete product.</summary>
    MissingRequiredAttribute,
}

/// <summary>
/// The products the catalogue lacks something for — Steward's
/// <c>catalogue_gaps</c>, and a screen's question alike. Draft and Active only:
/// an archived product is out of the catalogue.
/// </summary>
/// <param name="Culture">The gap's language, for the kinds that are in one.</param>
/// <param name="DisplayCulture">The reader's: what the names come back in.</param>
public sealed record CatalogueGapsQuery(
    CatalogueGap Kind, string? Culture, int Limit = ReadLimits.MaxRows, string DisplayCulture = CultureNegotiation.Default)
    : IQuery<CatalogueGaps>;

/// <param name="Total">All the products with the gap, not only the rows shown.</param>
public sealed record CatalogueGaps(CatalogueGap Kind, string? Culture, int Total, IReadOnlyList<GapRow> Rows);

/// <param name="Lacks">For <see cref="CatalogueGap.MissingRequiredAttribute"/>: the attributes it lacks, by
/// their labels in the reader's culture. Empty for the other kinds — the kind says what is missing.</param>
public sealed record GapRow(string ProductId, string Code, string Name, ProductStatus Status, IReadOnlyList<string> Lacks);

/// <summary>One rule per kind; the Products list's "missing" filter is the same
/// rule for a name, in SQL (S-3a).</summary>
public static class CatalogueGapRules
{
    public static bool NeedsCulture(CatalogueGap kind) =>
        kind is CatalogueGap.MissingName or CatalogueGap.MissingImageAlt;

    /// <param name="tree">What each category requires; only <see cref="CatalogueGap.MissingRequiredAttribute"/>
    /// reads it, and with none nothing is required.</param>
    public static bool Has(Product product, CatalogueGap kind, string? culture, CategoryTree? tree = null) => kind switch
    {
        CatalogueGap.MissingName => !product.Name.Cultures.Contains(Culture.Normalize(culture!)),
        CatalogueGap.MissingImage => product.Images.Count == 0,
        CatalogueGap.MissingImageAlt => product.Images.Any(image =>
            image.Alt is null || !image.Alt.Cultures.Contains(Culture.Normalize(culture!))),
        CatalogueGap.DraftWaiting => product.Status == ProductStatus.Draft,
        CatalogueGap.MissingRequiredAttribute => product.MissingRequired(tree ?? CategoryTree.Empty).Count > 0,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No rule for this gap."),
    };
}

public sealed class CatalogueGapsValidator : AbstractValidator<CatalogueGapsQuery>
{
    public CatalogueGapsValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();

        RuleFor(x => x.Culture)
            .Must(CultureNegotiation.Supports)
            .When(x => CatalogueGapRules.NeedsCulture(x.Kind))
            .WithMessage("This gap is in a language: es or en.");

        RuleFor(x => x.Culture)
            .Null()
            .When(x => !CatalogueGapRules.NeedsCulture(x.Kind))
            .WithMessage("This gap is not in a language.");

        RuleFor(x => x.Limit).InclusiveBetween(1, ReadLimits.MaxRows);

        RuleFor(x => x.DisplayCulture).Must(CultureNegotiation.Supports);
    }
}

public sealed class CatalogueGapsHandler(
    IProductCatalogReader products, ICategoryReader categories, IAttributeDefinitionReader definitions)
    : IQueryHandler<CatalogueGapsQuery, CatalogueGaps>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);

    public async Task<CatalogueGaps> HandleAsync(CatalogueGapsQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.gaps");
        activity?.SetTag("catalog.gap", query.Kind.ToString());
        activity?.SetTag("catalog.culture", query.Culture);

        // Only the one kind reads what categories require, and names what is lacking.
        var byCategory = query.Kind == CatalogueGap.MissingRequiredAttribute;
        var tree = byCategory ? await categories.AllAsync(cancellationToken) : CategoryTree.Empty;
        var labels = byCategory ? await definitions.AllAsync(cancellationToken) : AttributeDefinitions.Empty;

        // In memory over the live catalogue: a hundred products (the scale rule's
        // "deferred with its number"); the full catalogue makes it SQL.
        var gaps = (await products.LiveAsync(cancellationToken))
            .Where(product => CatalogueGapRules.Has(product, query.Kind, query.Culture, tree))
            .OrderBy(product => product.Code, StringComparer.Ordinal)
            .ToList();

        activity?.SetTag("catalog.total", gaps.Count);

        return new CatalogueGaps(query.Kind, query.Culture is { } asked ? Culture.Normalize(asked) : null, gaps.Count,
        [
            .. gaps.Take(query.Limit).Select(product =>
                new GapRow(
                    product.Id.Value.ToString(), product.Code, product.Name.In(query.DisplayCulture), product.Status,
                    [.. ProductAttributes.Missing(product, tree, labels, query.DisplayCulture).Select(missing => missing.Label)])),
        ]);
    }
}

public sealed class CatalogueGapsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/catalog/gaps?kind=MissingName&culture=en&limit=50 — `culture` is the
        // gap's language; the names come back in the reader's, from Accept-Language
        // (invariant 6), declared as Content-Language.
        app.MapGet("/api/catalog/gaps",
                async Task<Ok<CatalogueGaps>> (WireEnum<CatalogueGap> kind, string? culture, int? limit,
                    HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
                {
                    var reader = CultureNegotiation.Resolve(null, http.Request.Headers.AcceptLanguage);
                    var gaps = await dispatcher.SendAsync(
                        new CatalogueGapsQuery(kind.Value, culture, limit ?? ReadLimits.MaxRows, reader), ct);
                    http.Response.Headers.ContentLanguage = reader;
                    http.Response.Headers.Vary = "Accept-Language";
                    return TypedResults.Ok(gaps);
                })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Catalog")
            .WithName("ListCatalogueGaps");
    }
}
