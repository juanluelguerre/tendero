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
                // Culture: explicit query parameter > Accept-Language > "es"
                // (ADR 0013). The chain lives in CultureNegotiation since the
                // third endpoint that needed it turned up.
                var resolved = CultureNegotiation.Resolve(
                    culture, http.Request.Headers.AcceptLanguage);

                var result = await dispatcher.SendAsync(
                    new SearchProductsQuery(q, resolved, page ?? 1, pageSize ?? 20), ct);

                // The other half of the negotiation: the client asks, the server
                // declares which language it answered in. Vary because the
                // response DEPENDS on Accept-Language when the parameter is
                // absent, and a shared cache without it serves Spanish to
                // whoever asked for English.
                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return TypedResults.Ok(result);
            })
            .AllowAnonymous()   // searching is what an agent does without identifying itself
            .WithTags("Search")
            .WithName("SearchProducts");
    }
}

public sealed class SearchProductsHandler(ILexicalProductSearch search)
    : IQueryHandler<SearchProductsQuery, SearchResultPage>
{
    // Today it delegates to the lexical one. When hybrid search exists, this
    // handler will choose between lexical and hybrid by feature flag, and the
    // endpoint does not change: the storefront's public contract has been stable
    // since day one.
    public Task<SearchResultPage> HandleAsync(SearchProductsQuery query, CancellationToken ct) =>
        search.SearchAsync(new ProductSearchQuery(query.Q, query.Culture, query.Page, query.PageSize), ct);
}
