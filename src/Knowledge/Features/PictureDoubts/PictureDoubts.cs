using System.Diagnostics.Metrics;
using Carter;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Knowledge.Features.PictureDoubts;

// ---------- The queue ----------

/// <summary>The doubts about pictures that wait for a person (phase 13a, I-4d), the oldest first.</summary>
/// <param name="Culture">The language the two colours are worded in: the reviewer's.</param>
public sealed record ListPictureDoubtsQuery(int Page = Paging.FirstPage, int PageSize = Paging.DefaultPageSize, string Culture = "es")
    : IQuery<PictureDoubtsPage>;

/// <summary>
/// One doubt, as a person meets it: which picture of which product, the colour
/// the model saw and how sure it was, and the colour the catalogue holds — for
/// the screen to put beside the picture.
/// </summary>
/// <param name="Seen">The option's code, and <paramref name="SeenLabel"/> the same as a person reads it.</param>
/// <param name="ReadBy">The model that read the picture: the doubt is its, not the shop's.</param>
/// <param name="SeenOnAnotherVariant">The colour seen is one another variant of the same product holds: the picture may be that variant's.</param>
public sealed record PictureDoubtView(
    string DoubtId,
    string ProductId,
    string ImageId,
    string AttributeCode,
    string Seen,
    string SeenLabel,
    string Held,
    string HeldLabel,
    double Confidence,
    string ReadBy,
    DateTimeOffset RaisedAt,
    bool SeenOnAnotherVariant);

/// <param name="Total">How many doubts are open, not how many are on the page.</param>
public sealed record PictureDoubtsPage(IReadOnlyList<PictureDoubtView> Items, int Total, int Page, int PageSize);

public sealed class ListPictureDoubtsValidator : AbstractValidator<ListPictureDoubtsQuery>
{
    public ListPictureDoubtsValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(Paging.FirstPage);
        RuleFor(query => query.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}

public sealed class ListPictureDoubtsHandler(IPictureDoubtRepository doubts) : IQueryHandler<ListPictureDoubtsQuery, PictureDoubtsPage>
{
    public async Task<PictureDoubtsPage> HandleAsync(ListPictureDoubtsQuery query, CancellationToken cancellationToken)
    {
        var page = await doubts.OpenDoubtsAsync(query.Page, query.PageSize, cancellationToken);

        return new PictureDoubtsPage(
            [
                .. page.Doubts.Select(doubt => new PictureDoubtView(
                    doubt.Id.ToString(),
                    doubt.ProductId.ToString(),
                    doubt.PictureId,
                    doubt.AttributeCode,
                    doubt.Seen,
                    doubt.SeenLabel.In(query.Culture),
                    doubt.Held,
                    doubt.HeldLabel.In(query.Culture),
                    doubt.Confidence,
                    doubt.ReadByModel,
                    doubt.RaisedAt,
                    doubt.SeenOnAnotherVariant)),
            ],
            page.Total, query.Page, query.PageSize);
    }
}

// ---------- The decision ----------

/// <summary>
/// A person's decision on a doubt — never a model's (invariant 12). It records
/// what they said of the picture and changes nothing else: replacing a picture,
/// or filing it under another variant, is done in the catalogue by whoever
/// decided, and a doubt blocks nothing while it waits.
/// </summary>
public sealed record DecidePictureDoubtCommand(PictureDoubtId DoubtId, PictureDoubtDecision Decision) : ICommand<DecidePictureDoubtResult>;

public enum DecidePictureDoubtOutcome
{
    Decided,
    NotFound,

    /// <summary>Already decided, or withdrawn because the picture was read again: there is nothing left to decide.</summary>
    NotWaiting,
}

public sealed record DecidePictureDoubtResult(DecidePictureDoubtOutcome Outcome, PictureDoubtId DoubtId) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        DecidePictureDoubtOutcome.Decided => AuditOutcome.Allowed,
        DecidePictureDoubtOutcome.NotFound or DecidePictureDoubtOutcome.NotWaiting => AuditOutcome.Denied,
    };

    string? IAuditedResult.AuditReason => Outcome == DecidePictureDoubtOutcome.Decided ? null : Outcome.ToString();
}

/// <summary>
/// Decides one doubt. Who decided is whoever the request authenticated as —
/// taken from the principal, never from the request body, so the audit row and
/// the doubt name the same person.
/// </summary>
public sealed class DecidePictureDoubtHandler(
    IPictureDoubtRepository doubts, IPrincipalAccessor principals, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<DecidePictureDoubtCommand, DecidePictureDoubtResult>
{
    public async Task<DecidePictureDoubtResult> HandleAsync(DecidePictureDoubtCommand command, CancellationToken cancellationToken)
    {
        var doubt = await doubts.FindDoubtAsync(command.DoubtId, cancellationToken);
        if (doubt is null)
            return new DecidePictureDoubtResult(DecidePictureDoubtOutcome.NotFound, command.DoubtId);
        if (doubt.Status != PictureDoubtStatus.Open)
            return new DecidePictureDoubtResult(DecidePictureDoubtOutcome.NotWaiting, command.DoubtId);

        var principal = principals.Current;
        doubt.Decide(command.Decision, principal.DisplayName ?? principal.Subject ?? "unknown", clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        PictureDoubtsDecided.Record(command.Decision);
        return new DecidePictureDoubtResult(DecidePictureDoubtOutcome.Decided, doubt.Id);
    }
}

/// <summary>
/// `tendero.knowledge.picture_doubts.decided` (the tracking plan): what people
/// say of the pictures the model doubted. Mostly *the picture is fine* is the
/// model raising what nobody needed raised.
/// </summary>
internal static class PictureDoubtsDecided
{
    private static readonly Meter Meter = new(TelemetrySources.Knowledge);

    private static readonly Counter<long> Decided = Meter.CreateCounter<long>(
        "tendero.knowledge.picture_doubts.decided", unit: "{doubt}", description: "Doubts about a picture a person decided, by what they said of it.");

    public static void Record(PictureDoubtDecision decision) =>
        Decided.Add(1, new KeyValuePair<string, object?>("decision", MetricLabels.Of(decision)));
}

// ---------- The wire ----------

public sealed record DecidePictureDoubtRequest(PictureDoubtDecision Decision);

public sealed record DecidePictureDoubtResponse(string DoubtId, PictureDoubtDecision Decision);

public static class PictureDoubtRefusals
{
    public static readonly Refusal NotWaiting = new(
        "DoubtNotWaiting", 409, "La duda ya no espera una decisión.", "The doubt is not waiting for a decision.");
}

public sealed class PictureDoubtsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/knowledge/picture-doubts?page=1&pageSize=20 — the pictures the
        // model is sure show another colour than the catalogue holds, oldest first.
        app.MapGet("/api/knowledge/picture-doubts",
            async Task<Ok<PictureDoubtsPage>> (
                int? page, int? pageSize, string? culture, HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                // The reviewer's language (ADR 0013): the two colours are worded in it.
                var language = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                var queue = await dispatcher.SendAsync(
                    new ListPictureDoubtsQuery(page ?? Paging.FirstPage, pageSize ?? Paging.DefaultPageSize, language), ct);
                http.Response.Headers.ContentLanguage = language;
                http.Response.Headers.Vary = "Accept-Language";
                return TypedResults.Ok(queue);
            })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Knowledge")
            .WithName("ListPictureDoubts");

        // POST /api/knowledge/picture-doubts/{id}/decide  { "decision": "PictureIsFine" }
        app.MapPost("/api/knowledge/picture-doubts/{id:guid}/decide",
                async Task<Results<Ok<DecidePictureDoubtResponse>, NotFound, ProblemHttpResult>> (
                    Guid id, DecidePictureDoubtRequest body, ICommandDispatcher dispatcher, CancellationToken ct) =>
                {
                    var result = await dispatcher.SendAsync(new DecidePictureDoubtCommand(new PictureDoubtId(id), body.Decision), ct);

                    return result.Outcome switch
                    {
                        DecidePictureDoubtOutcome.NotFound => TypedResults.NotFound(),
                        DecidePictureDoubtOutcome.NotWaiting => TypedResults.Problem(
                            statusCode: PictureDoubtRefusals.NotWaiting.Status,
                            extensions: PictureDoubtRefusals.NotWaiting.Said(
                                es: "Ya se decidió, o la imagen se volvió a leer y ya no la plantea.",
                                en: "It was already decided, or the picture was read again and no longer raises it.")),
                        _ => TypedResults.Ok(new DecidePictureDoubtResponse(result.DoubtId.ToString(), body.Decision)),
                    };
                })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithMetadata(new Refuses(PictureDoubtRefusals.NotWaiting))
            .WithTags("Knowledge")
            .WithName("DecidePictureDoubt");
    }
}
