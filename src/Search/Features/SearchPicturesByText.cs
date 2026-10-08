using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Search.Features.SearchPicturesByText;

public sealed record SearchPicturesByTextQuery(string Q, string Culture, int Limit) : IQuery<VisualSearchResult>;

public sealed class SearchPicturesByTextValidator : AbstractValidator<SearchPicturesByTextQuery>
{
    public SearchPicturesByTextValidator()
    {
        // The same door as the search box: one character matches so much that the
        // answer is noise. Unlike it, nothing typed is not a browse — there is no
        // "every picture" to show.
        RuleFor(query => query.Q).NotEmpty().MinimumLength(2).MaximumLength(200).OverridePropertyName("q");
        RuleFor(query => query.Culture).Must(culture => culture is "es" or "en").WithMessage("Supported cultures: es, en.");
        RuleFor(query => query.Limit).InclusiveBetween(1, VisualSearch.MaxLimit).OverridePropertyName("limit");
    }
}

/// <summary>
/// Words against the catalogue's pictures (phase 13a, I-6): what the products
/// LOOK like, asked in words — "a round wooden table" finds the table whose
/// description never said round.
///
/// **An endpoint of its own, and not a leg of the search.** The model that
/// embeds both sides places a text near the pictures of what it names, which is
/// a third opinion beside the words and their meaning — and nobody yet knows
/// what it does to the ranking the shop is measured on. Until its effect on
/// NDCG@10 is a number, `/api/search` does not know this exists.
///
/// The culture decides the cards, never the match: one model reads both
/// languages, and a picture has none.
/// </summary>
public sealed class SearchPicturesByTextEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/search/pictures?q=mesa redonda de madera&culture=es&limit=12
        app.MapGet("/api/search/pictures",
                async Task<Results<Ok<VisualSearchResult>, ProblemHttpResult>> (
                    string? q, string? culture, int? limit,
                    HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
                {
                    var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                    try
                    {
                        var result = await dispatcher.SendAsync(
                            new SearchPicturesByTextQuery(q ?? "", resolved, limit ?? VisualSearch.DefaultLimit), ct);

                        http.Response.Headers.ContentLanguage = resolved;
                        http.Response.Headers.Vary = "Accept-Language";

                        return TypedResults.Ok(result);
                    }
                    catch (Exception exception) when (exception is SearchStageNotConfiguredException or SearchUnavailableException)
                    {
                        return TypedResults.Problem(
                            title: "Searching the pictures is not available right now.",
                            detail: "Searching by words works as always.",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                })
            // By explicit decision, as the search it sits beside.
            .AllowAnonymous()
            .RequireRateLimiting(VisualSearch.PerClient)
            .WithTags("Search")
            .WithName("SearchPicturesByText");
    }
}

public sealed class SearchPicturesByTextHandler(
    IImageEmbeddingGenerator model,
    VisualSearch search,
    IOptions<VisualSearchOptions> options)
    : IQueryHandler<SearchPicturesByTextQuery, VisualSearchResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Search);

    public async Task<VisualSearchResult> HandleAsync(SearchPicturesByTextQuery query, CancellationToken ct)
    {
        using var activity = Telemetry.StartActivity("search.pictures_by_text");
        activity?.SetTag("search.culture", query.Culture);
        activity?.SetTag("search.query_length", query.Q.Length);

        ReadOnlyMemory<float> vector;
        try
        {
            vector = await model.EmbedTextAsync(query.Q, ct);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SearchStageNotConfiguredException or SearchUnavailableException))
        {
            throw new SearchUnavailableException("embedding a text for the pictures", $"{exception.GetType().Name}: {exception.Message}");
        }

        var result = await search.NearestAsync(vector, options.Value.MinTextSimilarity, query.Culture, query.Limit, ct);

        activity?.SetTag("search.pictures_by_text.found", result.Items.Count);
        if (result.Items is [var nearest, ..])
            activity?.SetTag("search.pictures_by_text.nearest", nearest.Similarity);

        return result;
    }
}
