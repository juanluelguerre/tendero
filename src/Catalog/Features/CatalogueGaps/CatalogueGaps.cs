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

namespace ElGuerre.Tendero.Catalog.Features.CatalogueGaps;

/// <summary>
/// What the catalogue lacks (phase 12, S-3a). "Missing a required attribute" is
/// not here: the domain has no such thing yet (the backlog's *Required attributes
/// per category*).
/// </summary>
public enum CatalogueGap
{
    /// <summary>The name has nothing in a culture.</summary>
    MissingName,

    MissingImage,

    /// <summary>A picture whose alternative text has nothing in a culture.</summary>
    MissingImageAlt,

    DraftWaiting,
}

/// <summary>
/// The products the catalogue lacks something for — Steward's
/// <c>catalogue_gaps</c>, and a screen's question alike. Draft and Active only:
/// an archived product is out of the catalogue.
/// </summary>
public sealed record CatalogueGapsQuery(CatalogueGap Kind, string? Culture, int Limit = ReadLimits.MaxRows)
    : IQuery<CatalogueGaps>;

/// <param name="Total">All the products with the gap, not only the rows shown.</param>
public sealed record CatalogueGaps(CatalogueGap Kind, string? Culture, int Total, IReadOnlyList<GapRow> Rows);

public sealed record GapRow(string ProductId, string Code, string Name, ProductStatus Status);

/// <summary>One rule per kind; the Products list's "missing" filter is the same
/// rule for a name, in SQL (S-3a).</summary>
public static class CatalogueGapRules
{
    public static bool NeedsCulture(CatalogueGap kind) =>
        kind is CatalogueGap.MissingName or CatalogueGap.MissingImageAlt;

    public static bool Has(Product product, CatalogueGap kind, string? culture) => kind switch
    {
        CatalogueGap.MissingName => !product.Name.Cultures.Contains(Culture.Normalize(culture!)),
        CatalogueGap.MissingImage => product.Images.Count == 0,
        CatalogueGap.MissingImageAlt => product.Images.Any(image =>
            image.Alt is null || !image.Alt.Cultures.Contains(Culture.Normalize(culture!))),
        CatalogueGap.DraftWaiting => product.Status == ProductStatus.Draft,
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
    }
}

public sealed class CatalogueGapsHandler(IProductCatalogReader products) : IQueryHandler<CatalogueGapsQuery, CatalogueGaps>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);

    public async Task<CatalogueGaps> HandleAsync(CatalogueGapsQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.gaps");
        activity?.SetTag("catalog.gap", query.Kind.ToString());

        // In memory over the live catalogue: a hundred products (the scale rule's
        // "deferred with its number"); the full catalogue makes it SQL.
        var gaps = (await products.LiveAsync(cancellationToken))
            .Where(product => CatalogueGapRules.Has(product, query.Kind, query.Culture))
            .OrderBy(product => product.Code, StringComparer.Ordinal)
            .ToList();

        activity?.SetTag("catalog.total", gaps.Count);
        var culture = query.Culture ?? CultureNegotiation.Default;

        return new CatalogueGaps(query.Kind, query.Culture, gaps.Count,
        [
            .. gaps.Take(query.Limit).Select(product =>
                new GapRow(product.Id.Value.ToString(), product.Code, product.Name.In(culture), product.Status)),
        ]);
    }
}

public sealed class CatalogueGapsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/catalog/gaps?kind=MissingName&culture=en&limit=50
        app.MapGet("/api/catalog/gaps",
                async Task<Ok<CatalogueGaps>> (WireEnum<CatalogueGap> kind, string? culture, int? limit,
                    IQueryDispatcher dispatcher, CancellationToken ct) =>
                    TypedResults.Ok(await dispatcher.SendAsync(
                        new CatalogueGapsQuery(kind.Value, culture, limit ?? ReadLimits.MaxRows), ct)))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Catalog")
            .WithName("ListCatalogueGaps");
    }
}
