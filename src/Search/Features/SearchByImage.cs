using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Search.Features.SearchByImage;

/// <param name="Picture">
/// The bytes as they arrived, held for the length of this query and by nothing
/// else: never written to the image store, a log or a trace (the images spec,
/// *The uploaded picture*; <c>docs/privacy.md</c>).
/// </param>
public sealed record SearchByImageQuery(ReadOnlyMemory<byte> Picture, string Culture, int Limit) : IQuery<VisualSearchResult>;

public sealed class SearchByImageValidator : AbstractValidator<SearchByImageQuery>
{
    public SearchByImageValidator()
    {
        // What it is and how large, by the bytes (UploadedPicture) — answered at
        // the door, before a model is asked anything.
        RuleFor(query => query.Picture)
            .Custom((picture, context) =>
            {
                if (UploadedPicture.Read(picture.Span).Refusal is { } refusal)
                    context.AddFailure("picture", refusal);
            });

        RuleFor(query => query.Culture).Must(culture => culture is "es" or "en").WithMessage("Supported cultures: es, en.");
        RuleFor(query => query.Limit).InclusiveBetween(1, VisualSearch.MaxLimit).OverridePropertyName("limit");
    }
}

/// <summary>
/// Searching the shop with a picture (phase 13a, I-6): a photograph in, the
/// products whose pictures are nearest out, as the shop's own cards.
///
/// **Anyone may ask**, as anyone may search by words: a guest with a photograph
/// is a shopper. What bounds it is how much is asked — a model runs on this
/// process's CPU for each picture — and never who asks.
///
/// **Without the model or the store it says so, and nothing else changes**
/// (invariant 8): a 503 that names the stage, and a status the storefront reads
/// first so its control says "not available" instead of failing on the first
/// picture. Searching by words never passes through here.
/// </summary>
public sealed class SearchByImageEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/search/by-image?culture=es&limit=12 — multipart, one file: picture
        app.MapPost("/api/search/by-image",
                async Task<Results<Ok<VisualSearchResult>, ValidationProblem, ProblemHttpResult>> (
                    IFormFile picture, string? culture, int? limit,
                    HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
                {
                    // Before a byte is buffered: the declared length is the caller's
                    // word, and the request's own limit below is the server's.
                    if (picture.Length > UploadedPicture.MaxBytes)
                        return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["picture"] = [$"A picture to search with is at most {UploadedPicture.MaxBytes / (1024 * 1024)} MB."],
                        });

                    using var buffer = new MemoryStream(capacity: (int)picture.Length);
                    await using (var upload = picture.OpenReadStream())
                        await upload.CopyToAsync(buffer, ct);

                    var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                    try
                    {
                        var result = await dispatcher.SendAsync(
                            new SearchByImageQuery(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), resolved, limit ?? VisualSearch.DefaultLimit), ct);

                        http.Response.Headers.ContentLanguage = resolved;
                        http.Response.Headers.Vary = "Accept-Language";

                        return TypedResults.Ok(result);
                    }
                    catch (Exception exception) when (exception is SearchStageNotConfiguredException or SearchUnavailableException)
                    {
                        return VisualSearchProblems.NotAnswering(exception);
                    }
                })
            // By explicit decision: what search shows anybody, asked with a picture.
            .AllowAnonymous()
            .RequireRateLimiting(VisualSearch.PerClient)
            // The picture and a little for the form around it; more is refused
            // while it is read, not after it is all in memory.
            .WithMetadata(new RequestSizeLimitAttribute(UploadedPicture.MaxBytes + (64 * 1024)))
            // Nothing here rides on a cookie: the endpoint reads no session and
            // changes nothing, which is what antiforgery protects.
            .DisableAntiforgery()
            .WithTags("Search")
            .WithName("SearchByImage");

        // GET /api/search/by-image/status — whether a picture can be searched with
        // here. Configured, not probed: no model is run to answer it.
        app.MapGet("/api/search/by-image/status",
                Ok<VisualSearchAvailability> (IImageEmbeddingGenerator model, IPictureVectorIndexer pictures) =>
                    TypedResults.Ok(new VisualSearchAvailability(model.IsConfigured && pictures.IsConfigured)))
            .AllowAnonymous()
            .WithTags("Search")
            .WithName("GetVisualSearchAvailability");
    }
}

/// <summary>What a visual question answers when it cannot: which of the two reasons, in the API's voice.</summary>
internal static class VisualSearchProblems
{
    public static ProblemHttpResult NotAnswering(Exception exception) =>
        exception is SearchStageNotConfiguredException
            ? TypedResults.Problem(
                title: "Searching by picture is not available here.",
                detail: "This shop was started without visual search. Searching by words works as always.",
                statusCode: StatusCodes.Status503ServiceUnavailable)
            // The detail of the failure stays on the exception, which is logged
            // whole: it names a store and a port, and says nothing to a shopper.
            : TypedResults.Problem(
                title: "Searching by picture is not answering right now.",
                detail: "Searching by words works as always. Try the picture again in a moment.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
}

public sealed class SearchByImageHandler(
    IImageEmbeddingGenerator model,
    VisualSearch search,
    IOptions<VisualSearchOptions> options)
    : IQueryHandler<SearchByImageQuery, VisualSearchResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Search);

    public async Task<VisualSearchResult> HandleAsync(SearchByImageQuery query, CancellationToken ct)
    {
        using var activity = Telemetry.StartActivity("search.by_image");
        activity?.SetTag("search.culture", query.Culture);

        // Three facts about the picture, and nothing of the picture itself.
        if (UploadedPicture.Read(query.Picture.Span).Picture is { } picture)
        {
            activity?.SetTag("search.picture.width", picture.Width);
            activity?.SetTag("search.picture.height", picture.Height);
            activity?.SetTag("search.picture.format", picture.Format);
        }

        ReadOnlyMemory<float> vector;
        try
        {
            vector = await model.EmbedPictureAsync(query.Picture, ct);
        }
        catch (ArgumentException)
        {
            // A header that reads as a picture over a body that does not decode.
            throw new ValidationException([new ValidationFailure("picture", "The picture could not be read. Try another, or a JPEG of it.")]);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SearchStageNotConfiguredException or SearchUnavailableException))
        {
            throw new SearchUnavailableException("embedding a picture", $"{exception.GetType().Name}: {exception.Message}");
        }

        var result = await search.NearestAsync(vector, options.Value.MinPictureSimilarity, query.Culture, query.Limit, ct);

        activity?.SetTag("search.by_image.found", result.Items.Count);
        if (result.Items is [var nearest, ..])
            activity?.SetTag("search.by_image.nearest", nearest.Similarity);

        return result;
    }
}
