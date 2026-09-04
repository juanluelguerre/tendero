using Carter;
using ElGuerre.Tendero.Search.Contracts;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Search.Features.SearchProducts;

public sealed record SearchProductsQuery(
    string Q, string Culture, int Page, int PageSize, string? Category = null, string? Sort = null)
    : IQuery<SearchResultPage>;

public sealed class SearchProductsValidator : AbstractValidator<SearchProductsQuery>
{
    public SearchProductsValidator()
    {
        // **Empty is allowed; one character is not.** Nothing typed is a
        // browse — a category page, a home page row — and it is the question
        // every entry point that is not a search box asks. A single character
        // is still a query, and still one that matches so much the answer is
        // noise, which is the rule the storefront has enforced on its own side
        // since the first slice.
        RuleFor(x => x.Q).MinimumLength(2).MaximumLength(200)
            .When(x => !string.IsNullOrEmpty(x.Q));

        RuleFor(x => x.Category).MaximumLength(60);

        // A closed set, because a sort the index cannot honour is a promise
        // broken at run time rather than at the door.
        RuleFor(x => x.Sort).Must(sort => sort is null or "newest")
            .WithMessage("Supported sorts: newest.");
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
        // GET /api/search?category=KITCHEN            — browse a whole branch
        // GET /api/search                             — browse everything
        app.MapGet("/api/search",
            async Task<Ok<SearchResultPage>> (
                   string? q, string? culture, int? page, int? pageSize, string? category, string? sort,
                   HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                // Culture: explicit query parameter > Accept-Language > "es"
                // (ADR 0013). The chain lives in CultureNegotiation since the
                // third endpoint that needed it turned up.
                var resolved = CultureNegotiation.Resolve(
                    culture, http.Request.Headers.AcceptLanguage);

                var result = await dispatcher.SendAsync(
                    new SearchProductsQuery(
                        q ?? string.Empty, resolved, page ?? 1, pageSize ?? 20, category, sort), ct);

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
        search.SearchAsync(
            new ProductSearchQuery(
                query.Q, query.Culture, query.Page, query.PageSize, query.Category, query.Sort), ct);
}
