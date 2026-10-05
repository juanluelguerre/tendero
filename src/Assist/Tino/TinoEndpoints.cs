using Carter;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.AI.Support;
using ElGuerre.Tendero.Assist.Turns;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Assist.Tino;

// ---------- The wire ----------

/// <param name="Context">What the shopper is looking at, when it is one thing: a product's page.</param>
/// <param name="ConversationId">The conversation to continue; omitted on its first turn.</param>
public sealed record TinoTurnRequest(IReadOnlyList<TurnMessage> Messages, TinoContext? Context, string? ConversationId);

/// <param name="Available">Whether Tino can be asked. False, the storefront says so and offers the search.</param>
public sealed record TinoAvailability(bool Available);

public sealed class TinoEndpoints : ICarterModule
{
    /// <summary>The rate limiter's policy: twelve turns a minute per client (the spec, *The models*).</summary>
    public const string TurnsPerClient = "tino-turns";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/assistant/turns?culture=en — the conversation so far and what the
        // shopper is looking at; the server keeps nothing between turns.
        //
        // Anonymous by explicit decision (invariant 10): a shopper asks before
        // signing in, as they search. What bounds it is not who asks but how much —
        // the rate limit per client, the turn's caps and the shop's day of tokens —
        // and what it can do is read what the storefront already shows.
        app.MapPost("/api/assistant/turns",
                async Task<Ok<TinoAnswer>> (TinoTurnRequest body, string? culture, HttpContext http,
                    ICommandDispatcher dispatcher, CancellationToken ct) =>
                {
                    var language = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                    var conversation = body.ConversationId is { Length: > 0 } id ? id : Guid.CreateVersion7().ToString();
                    var answer = await dispatcher.SendAsync(
                        new TinoTurnCommand(body.Messages ?? [], body.Context, conversation, language), ct);
                    http.Response.Headers.ContentLanguage = language;
                    http.Response.Headers.Vary = "Accept-Language";
                    return TypedResults.Ok(answer);
                })
            .AllowAnonymous()
            .RequireRateLimiting(TurnsPerClient)
            .WithTags("Assistant")
            .WithName("TakeTinoTurn");

        // GET /api/assistant/status — whether Tino can be asked, so the storefront's
        // entry says "not available" rather than failing on the first question.
        // Configured, not probed: no model is called to answer it (the plan's ruling 8).
        app.MapGet("/api/assistant/status",
                Ok<TinoAvailability> (IOptions<TinoOptions> options, ModelProfiles profiles, IEnumerable<ISupportScorer> scorers) =>
                {
                    var settings = options.Value;
                    string?[] names = [settings.Model, settings.Fallback];
                    return TypedResults.Ok(new TinoAvailability(
                        settings.Enabled && scorers.Any()
                        && names.Any(name => name is { Length: > 0 } && profiles.Find(name) is not null)));
                })
            .AllowAnonymous()
            .WithTags("Assistant")
            .WithName("GetTinoAvailability");
    }
}
