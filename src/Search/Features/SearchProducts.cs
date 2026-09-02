using Carter;
using ElGuerre.Tendero.Search.Contracts;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Search.Features.SearchProducts;

public sealed record SearchProductsQuery(string Q, string Culture, int Page, int PageSize)
    : IQuery<SearchResultPage>;

public sealed class SearchProductsValidator : AbstractValidator<SearchProductsQuery>
{
    public SearchProductsValidator()
    {
        RuleFor(x => x.Q).NotEmpty().MinimumLength(2).MaximumLength(200);
        RuleFor(x => x.Culture).Must(c => c is "es" or "en")
            .WithMessage("Supported cultures: es, en.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}

public sealed class SearchProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/search?q=zapatillas%20running&culture=es&page=1&pageSize=20
        app.MapGet("/api/search",
            async Task<Ok<SearchResultPage>> (
                   string q, string? culture, int? page, int? pageSize,
                   HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                // Cultura: query param explícito > Accept-Language > "es".
                var resolved = culture
                    ?? http.Request.GetTypedHeaders().AcceptLanguage
                        .OrderByDescending(l => l.Quality ?? 1)
                        .Select(l => l.Value.Value?.Split('-')[0].ToLowerInvariant())
                        .FirstOrDefault(c => c is "es" or "en")
                    ?? "es";

                var result = await dispatcher.SendAsync(
                    new SearchProductsQuery(q, resolved, page ?? 1, pageSize ?? 20), ct);

                // La otra mitad de la negociacion: el cliente pide, el servidor
                // declara en que idioma respondio. Vary porque la respuesta
                // DEPENDE de Accept-Language cuando no viene el parametro, y una
                // cache compartida sin esto sirve espanol a quien pidio ingles.
                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return TypedResults.Ok(result);
            })
            .WithTags("Search")
            .WithName("SearchProducts");
    }
}

public sealed class SearchProductsHandler(ILexicalProductSearch search)
    : IQueryHandler<SearchProductsQuery, SearchResultPage>
{
    // Hoy delega en el léxico. Cuando exista la búsqueda híbrida, este handler
    // decidirá por feature flag entre léxico / híbrido, y el endpoint no cambia:
    // el contrato público del storefront queda estable desde el primer día.
    public Task<SearchResultPage> HandleAsync(SearchProductsQuery query, CancellationToken ct) =>
        search.SearchAsync(new ProductSearchQuery(query.Q, query.Culture, query.Page, query.PageSize), ct);
}
