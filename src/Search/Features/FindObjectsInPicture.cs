using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Images;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Search.Features.FindObjectsInPicture;

/// <param name="Picture">The bytes as they arrived, held for this query and by nothing else (CS-7).</param>
public sealed record FindObjectsInPictureQuery(ReadOnlyMemory<byte> Picture, UploadedPicture Facts, string Culture) : IQuery<PictureObjectsView>;

/// <param name="Width">The picture's width as it is seen, after its orientation: the coordinates every box is in.</param>
/// <param name="Height">Its height, the same way.</param>
/// <param name="Objects">In reading order — top to bottom, left to right — numbered from one. Empty when nothing of any kind asked for was found.</param>
public sealed record PictureObjectsView(int Width, int Height, IReadOnlyList<PictureObjectView> Objects);

/// <param name="Number">Its place in the reading order: what the mark on the picture and its block are called by (CS-1.5).</param>
/// <param name="Kind">The shop's category code when it sells the kind (<c>SHIRT</c>), or the name of a common object it does not (<c>book</c>).</param>
/// <param name="Sold">Whether the shop sells this kind: a sold object is searched, one not sold goes in the line under the results (CS-1.6).</param>
/// <param name="Name">The kind in the language asked: its category's name, or the object's.</param>
/// <param name="Score">The detector's confidence, from 0 to 1. On the wire so the floor can be chosen from what shoppers saw.</param>
public sealed record PictureObjectView(int Number, string Kind, bool Sold, string Name, PictureBox Box, double Score);

/// <summary>The ways the shop says no to a photograph's objects, each with its code (ADR 0059).</summary>
public static class ObjectsRefusals
{
    public static readonly Refusal PictureNotTaken = new(
        "PictureNotTaken", 400, "La foto no se puede usar.", "The picture cannot be used.");

    public static readonly Refusal NotAvailable = new(
        "ObjectsNotAvailable", 503, "Aquí no se buscan los objetos de una foto.", "Finding the objects in a picture is not available here.");

    public static readonly Refusal NotAnswering = new(
        "ObjectsNotAnswering", 503, "La búsqueda de objetos no responde ahora.", "Finding the objects in a picture is not answering right now.");

    public static ProblemHttpResult Problem(Refusal refusal, string es, string en) =>
        TypedResults.Problem(statusCode: refusal.Status, extensions: refusal.Said(es: es, en: en));
}

/// <summary>
/// The objects in a photograph (the camera search, CS-2): what the shopper sent,
/// as boxes with a kind each, for the browser to cut out and search one by one
/// with <c>POST /api/search/by-image</c> (CS-3). The server keeps nothing between
/// the two: the picture is in the browser, and this answers where, never what is
/// like it.
///
/// **Anyone may ask**, as anyone may search with a picture; what bounds it is a
/// detector's run on this process's CPU, two to four seconds, twelve photographs a
/// minute on a budget of their own. **Without a detector it says so** (invariant
/// 8): the status says the objects cannot be found, and the browser then searches
/// the photograph whole, as one object (CS-9).
/// </summary>
public sealed class FindObjectsInPictureEndpoint : ICarterModule
{
    /// <summary>The rate limit's name: photographs a minute per client, registered by the host.</summary>
    public const string PerClient = "objects-in-picture-per-client";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/search/by-image/objects?culture=es — multipart, one file: picture
        app.MapPost("/api/search/by-image/objects",
                async Task<Results<Ok<PictureObjectsView>, ProblemHttpResult>> (
                    IFormFile picture, string? culture, HttpContext http, IObjectDetector detector, IQueryDispatcher dispatcher, CancellationToken ct) =>
                {
                    if (!detector.IsConfigured)
                        return ObjectsRefusals.Problem(ObjectsRefusals.NotAvailable,
                            "Esta tienda se ha arrancado sin detector de objetos. La foto se puede buscar entera.",
                            "This shop was started without an object detector. The picture can be searched whole.");

                    if (picture.Length > UploadedPicture.MaxBytes)
                        return NotTaken(UploadedPicture.TooHeavy);

                    using var buffer = new MemoryStream(capacity: (int)picture.Length);
                    await using (var upload = picture.OpenReadStream())
                        await upload.CopyToAsync(buffer, ct);
                    var bytes = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);

                    // What it is and how large, by the bytes, before the detector is asked anything.
                    var (facts, refusal) = UploadedPicture.Check(bytes.Span);
                    if (refusal is not null)
                        return NotTaken(refusal);

                    var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                    try
                    {
                        var found = await dispatcher.SendAsync(new FindObjectsInPictureQuery(bytes, facts!, resolved), ct);

                        http.Response.Headers.ContentLanguage = resolved;
                        http.Response.Headers.Vary = "Accept-Language";
                        return TypedResults.Ok(found);
                    }
                    catch (ArgumentException)
                    {
                        // A header that reads as a picture over a body that does not decode.
                        return NotTaken(LocalizedText.Both("The picture could not be read. Try another, or a JPEG of it.", "No se ha podido leer la foto. Prueba con otra, o con un JPEG de ella."));
                    }
                    catch (ObjectDetectionFailedException)
                    {
                        // The failure's detail is on the exception, logged whole by the handler;
                        // it names the export's files, and says nothing to a shopper.
                        return ObjectsRefusals.Problem(ObjectsRefusals.NotAnswering,
                            "La foto se puede buscar entera. Prueba de nuevo dentro de un momento.",
                            "The picture can be searched whole. Try again in a moment.");
                    }
                })
            // By explicit decision: what search shows anybody, asked with a photograph.
            .AllowAnonymous()
            .RequireRateLimiting(PerClient)
            .WithMetadata(new RequestSizeLimitAttribute(UploadedPicture.MaxBytes + (64 * 1024)))
            .WithMetadata(new Refuses(ObjectsRefusals.PictureNotTaken, ObjectsRefusals.NotAvailable, ObjectsRefusals.NotAnswering))
            // Nothing here rides on a cookie: it reads no session and changes nothing.
            .DisableAntiforgery()
            .WithTags("Search")
            .WithName("FindObjectsInPicture");
    }

    private static ProblemHttpResult NotTaken(LocalizedText why) =>
        ObjectsRefusals.Problem(ObjectsRefusals.PictureNotTaken, why.In("es"), why.In("en"));
}

/// <summary>The detector failed for a reason of its own — its export, its runtime — and not because of the picture.</summary>
public sealed class ObjectDetectionFailedException(string detector, Exception inner)
    : Exception($"The object detector '{detector}' failed: {inner.GetType().Name}: {inner.Message}", inner);

public sealed class FindObjectsInPictureHandler(
    IObjectDetector detector,
    ICategoryReader categories,
    IOptions<ObjectDetectionOptions> options,
    ILogger<FindObjectsInPictureHandler> logger)
    : IQueryHandler<FindObjectsInPictureQuery, PictureObjectsView>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Search);

    public async Task<PictureObjectsView> HandleAsync(FindObjectsInPictureQuery query, CancellationToken ct)
    {
        using var activity = Telemetry.StartActivity("search.objects_in_picture");
        // What the picture is like, and nothing of the picture itself (CS-7).
        activity?.SetTag("search.culture", query.Culture);
        activity?.SetTag("search.picture.width", query.Facts.UprightWidth);
        activity?.SetTag("search.picture.height", query.Facts.UprightHeight);
        activity?.SetTag("search.picture.format", query.Facts.Format);
        activity?.SetTag("search.objects.detector", detector.Name);

        IReadOnlyList<DetectedObject> detected;
        try
        {
            detected = await detector.DetectAsync(query.Picture, ObjectKinds.Shop.All, ct);
        }
        // A plain ArgumentException is the picture's: it did not decode. Its null and
        // out-of-range kinds are an adapter's bug, and are a failure like any other.
        catch (Exception exception) when (exception is not OperationCanceledException
            && (exception is not ArgumentException || exception is ArgumentNullException or ArgumentOutOfRangeException))
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.AddException(exception);
            logger.LogError(exception, "The object detector {Detector} failed", detector.Name);
            throw new ObjectDetectionFailedException(detector.Name, exception);
        }

        var chosen = ObjectsInPicture.Choose(detected, options.Value.MinScore);
        var tree = await categories.AllAsync(ct);
        var (width, height) = (query.Facts.UprightWidth, query.Facts.UprightHeight);

        var objects = ObjectsInPicture.InReadingOrder(chosen, height)
            .Select((found, at) => new PictureObjectView(
                at + 1,
                found.Kind.Code,
                found.Kind.Sold,
                (found.Kind.Sold ? tree.ByCode(found.Kind.Code)?.Name.In(query.Culture) : ObjectKinds.Shop.NameOf(found.Kind.Code, query.Culture)) ?? found.Kind.Code,
                found.Box,
                Math.Round(found.Score, 3)))
            .ToList();

        activity?.SetTag("search.objects.found", objects.Count);
        activity?.SetTag("search.objects.not_sold", objects.Count(found => !found.Sold));
        return new PictureObjectsView(width, height, objects);
    }
}
