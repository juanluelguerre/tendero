using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Catalog.Features.ListCategories;

/// <summary>
/// The taxonomy, for anything that wants to browse it.
///
/// **Phase 2 built categories and gave them exactly one consumer: the search
/// index.** They carry a localized name, a parent and a materialised path, and
/// the only thing that ever read them turned that into analysed text. So "Hogar
/// › Cocina › Menaje" existed as data, and as a breadcrumb on a product page,
/// and never as a way IN — which is most of why the storefront was a search box
/// with a grid under it.
///
/// It is a flat list rather than a nested tree, and that is deliberate: the
/// shape is already in `parent`, every consumer nests it differently, and a
/// nested payload forces a recursive type on a client that may only want the
/// top level.
/// </summary>
public sealed record CategoryView(
    string Code,
    string? Parent,
    string Name,

    /// <summary>
    /// The branch's names in the reader's culture, root first. It is what a
    /// breadcrumb needs and what stops a client having to walk parents itself.
    /// </summary>
    IReadOnlyList<string> Path,

    /// <summary>How deep it sits. Zero is a root, which is what a home page shows.</summary>
    int Depth);

public sealed record ListCategoriesResult(IReadOnlyList<CategoryView> Items);

public sealed record ListCategoriesQuery(string Culture) : IQuery<ListCategoriesResult>;

public sealed class ListCategoriesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/catalog/categories?culture=es
        app.MapGet("/api/catalog/categories",
            async Task<Ok<ListCategoriesResult>> (
                   string? culture, HttpContext http,
                   IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(
                    culture, http.Request.Headers.AcceptLanguage);

                var result = await dispatcher.SendAsync(new ListCategoriesQuery(resolved), ct);

                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return TypedResults.Ok(result);
            })
            // Anonymous by the same explicit decision the catalogue reads are:
            // what a shop puts in its window is public, and an agent browsing a
            // public taxonomy needs no identity (ADR 0013, and the MCP read
            // tools in phase 9 will want exactly this).
            .AllowAnonymous()
            .WithTags("Catalog")
            .WithName("ListCategories");
    }
}

public sealed class ListCategoriesHandler(ICategoryReader categories)
    : IQueryHandler<ListCategoriesQuery, ListCategoriesResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);

    public async Task<ListCategoriesResult> HandleAsync(
        ListCategoriesQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.list_categories");
        activity?.SetTag("catalog.culture", query.Culture);

        var tree = await categories.AllAsync(cancellationToken);

        var items = tree.All
            .Select(category => new CategoryView(
                category.Code,
                // The root's parent is itself in the materialised path, so a
                // one-segment ancestry means no parent at all.
                category.Ancestry.Count > 1 ? category.Ancestry[^2] : null,
                category.Name.In(query.Culture),
                [
                    .. category.Ancestry
                        .Select(code => tree.ByCode(code)?.Name.In(query.Culture))
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Select(name => name!)
                ],
                category.Ancestry.Count - 1))
            .OrderBy(view => view.Depth)
            .ThenBy(view => view.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        activity?.SetTag("catalog.category_count", items.Length);

        return new ListCategoriesResult(items);
    }
}
