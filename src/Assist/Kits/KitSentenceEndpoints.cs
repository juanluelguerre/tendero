using Carter;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Assist.Tino;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Assist.Kits;

/// <param name="Sentence">What the shopper wrote about the kit they want.</param>
public sealed record UnderstandKitSentenceRequest(string Sentence);

/// <param name="Available">Whether a sentence can be understood. False, the page draws no box and the form is the form.</param>
public sealed record KitSentenceAvailability(bool Available);

public sealed class KitSentenceEndpoints : ICarterModule
{
    /// <summary>The rate limiter's policy: as many a minute per client as kits are built, since each is a model's call.</summary>
    public const string PerClient = "kit-sentences";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/kits/understand?culture=es — a sentence in, a kit's form out.
        // Anonymous by explicit decision (invariant 10), as building a kit is: a
        // shopper asks before signing in. Nothing is built and nothing is kept.
        app.MapPost("/api/kits/understand",
                async Task<Ok<KitSentenceUnderstood>> (UnderstandKitSentenceRequest body, string? culture, HttpContext http,
                    IQueryDispatcher dispatcher, CancellationToken ct) =>
                {
                    var language = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                    var understood = await dispatcher.SendAsync(new UnderstandKitSentenceQuery(body.Sentence ?? "", language), ct);
                    http.Response.Headers.ContentLanguage = language;
                    http.Response.Headers.Vary = "Accept-Language";
                    return TypedResults.Ok(understood);
                })
            // An empty sentence, or one longer than a box takes: the validator's
            // problem, with its field (CommonRefusals.Invalid).
            .ProducesValidationProblem()
            .AllowAnonymous()
            .RequireRateLimiting(PerClient)
            .WithTags("Kits")
            .WithName("UnderstandKitSentence");

        // GET /api/kits/understand/status — whether to draw the box at all.
        // Configured, not probed: no model is called to answer it, as Tino's.
        app.MapGet("/api/kits/understand/status",
                Ok<KitSentenceAvailability> (IOptions<TinoOptions> options, ModelProfiles profiles) =>
                    TypedResults.Ok(new KitSentenceAvailability(options.Value.HasModel(profiles))))
            .AllowAnonymous()
            .WithTags("Kits")
            .WithName("GetKitSentenceAvailability");
    }
}
