using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Images;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
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
/// <param name="Words">What the shopper typed after the picture, to narrow it (the camera search, CS-4): matched by the lexical search, never embedded.</param>
/// <param name="Category">The category the answer stays within, branch and all: the object's kind, when a photograph was split into its objects.</param>
public sealed record SearchByImageQuery(ReadOnlyMemory<byte> Picture, string Culture, int Limit, string? Words = null, string? Category = null) : IQuery<VisualSearchResult>;

public sealed class SearchByImageValidator : AbstractValidator<SearchByImageQuery>
{
    public SearchByImageValidator()
    {
        this.RuleFor(query => query.Culture).Must(culture => culture is "es" or "en").WithMessage("Supported cultures: es, en.");
        this.RuleFor(query => query.Limit).InclusiveBetween(1, VisualSearch.MaxLimit).OverridePropertyName("limit");
        this.RuleFor(query => query.Words).MaximumLength(200).OverridePropertyName("q");
        this.RuleFor(query => query.Category).MaximumLength(64).OverridePropertyName("category");
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
        // POST /api/search/by-image?culture=es&limit=12&category=SHIRT&q=negra — multipart, one file: picture
        app.MapPost("/api/search/by-image",
                async Task<Results<Ok<VisualSearchResult>, ProblemHttpResult>> (
                    IFormFile picture, string? culture, int? limit, string? q, string? category,
                    HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
                {
                    // Before a byte is buffered: the declared length is the caller's
                    // word, and the request's own limit below is the server's.
                    if (picture.Length > UploadedPicture.MaxBytes)
                        return PictureRefusals.NotTaken(UploadedPicture.TooHeavy);

                    using var buffer = new MemoryStream(capacity: (int)picture.Length);
                    await using (var upload = picture.OpenReadStream())
                        await upload.CopyToAsync(buffer, ct);

                    // What it is and how large, by the bytes — answered at the door,
                    // before a model is asked anything, with the code its sibling
                    // answers for the same picture (ADR 0059).
                    if (UploadedPicture.Check(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)).Refusal is { } refusal)
                        return PictureRefusals.NotTaken(refusal);

                    var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                    try
                    {
                        var result = await dispatcher.SendAsync(
                            new SearchByImageQuery(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), resolved, limit ?? VisualSearch.DefaultLimit, q, category), ct);

                        http.Response.Headers.ContentLanguage = resolved;
                        http.Response.Headers.Vary = "Accept-Language";

                        return TypedResults.Ok(result);
                    }
                    catch (UnreadablePictureException)
                    {
                        return PictureRefusals.NotTaken(PictureRefusals.Unreadable);
                    }
                    catch (Exception exception) when (exception is SearchStageNotConfiguredException or SearchUnavailableException)
                    {
                        return PictureRefusals.NotAnsweringFor(exception);
                    }
                })
            // By explicit decision: what search shows anybody, asked with a picture.
            .AllowAnonymous()
            .RequireRateLimiting(VisualSearch.PerClient)
            // The picture and a little for the form around it; more is refused
            // while it is read, not after it is all in memory.
            .WithMetadata(new RequestSizeLimitAttribute(UploadedPicture.MaxBytes + (64 * 1024)))
            .WithMetadata(new Refuses(PictureRefusals.PictureNotTaken, PictureRefusals.NotAvailable, PictureRefusals.NotAnswering))
            // Nothing here rides on a cookie: the endpoint reads no session and
            // changes nothing, which is what antiforgery protects.
            .DisableAntiforgery()
            .WithTags("Search")
            .WithName("SearchByImage");

        // GET /api/search/by-image/status — whether a picture can be searched with
        // here. Configured, not probed: no model is run to answer it.
        app.MapGet("/api/search/by-image/status",
                Ok<VisualSearchAvailability> (IImageEmbeddingGenerator model, IPictureVectorIndexer pictures, IObjectDetector detector) =>
                    TypedResults.Ok(new VisualSearchAvailability(model.IsConfigured && pictures.IsConfigured, detector.IsConfigured)))
            .AllowAnonymous()
            .WithTags("Search")
            .WithName("GetVisualSearchAvailability");
    }
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
        catch (ArgumentException exception)
        {
            // A header that reads as a picture over a body that does not decode.
            throw new UnreadablePictureException(exception);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SearchStageNotConfiguredException or SearchUnavailableException))
        {
            throw new SearchUnavailableException("embedding a picture", $"{exception.GetType().Name}: {exception.Message}");
        }

        // The words and the category narrow what the picture is answered among; they never move its order.
        var among = await search.AmongAsync(query.Words, query.Category, query.Culture, ct);
        activity?.SetTag("search.by_image.narrowed", among is not null);

        var result = await search.NearestAsync(vector, options.Value.MinPictureSimilarity, query.Culture, query.Limit, among, ct);

        activity?.SetTag("search.by_image.found", result.Items.Count);
        if (result.Items is [var nearest, ..])
            activity?.SetTag("search.by_image.nearest", nearest.Similarity);

        return result;
    }
}
