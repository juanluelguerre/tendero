using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Accounts.Features.ListAuditEntries;

/// <summary>
/// Who did what, and what the shop refused.
///
/// **The slice lives in Accounts and the table belongs to nobody**, which is
/// worth saying out loud rather than leaving as an oddity. `audit` is a
/// cross-cutting schema: every context writes to it through the dispatcher and
/// none owns it, exactly as none owns the outbox. The read had to live
/// somewhere, and Accounts is where it belongs because the screen is built
/// around WHO — the column a reader scans, and the only column any context
/// actually owns.
///
/// It reads through `IAuditReader`, so this slice knows no more about the table
/// than any other consumer does. If a later phase gives audit a behaviour of its
/// own — a retention policy, an export — that is the moment it earns a context,
/// and moving one file is the whole cost of having been wrong.
/// </summary>
public sealed record ListAuditEntriesQuery(
    AuditOutcome? Outcome, int Page = Paging.FirstPage, int PageSize = Paging.DefaultPageSize)
    : IQuery<ListAuditEntriesResult>;

public sealed record AuditEntryView(
    string Id,
    string CommandType,
    string Payload,
    string? CustomerId,
    string? AgentId,
    /// <summary>The stable identifier, opaque with a real issuer.</summary>
    string? Subject,
    /// <summary>What that subject was called when this happened.</summary>
    string? ActorName,
    AuditOutcome Outcome,
    string? Reason,
    string? TraceId,
    DateTimeOffset At);

/// <summary>How many entries of the log have one outcome.</summary>
public sealed record AuditOutcomeCount(AuditOutcome Outcome, int Count);

/// <param name="Total">How many entries the outcome filter matched: the log's, not the page's (R-7.2).</param>
/// <param name="Counts">Every outcome, zero included, whichever one is shown:
/// a chip keeps its number and its width when another is chosen.</param>
/// <remarks>There used to be a `Refused` number beside these. Nothing read it
/// once the heading stopped counting refusals (R-4.4), and it is the counts'
/// Denied and Failed added up: one number in two fields is two numbers that can
/// disagree, so it went with R-7.2.</remarks>
public sealed record ListAuditEntriesResult(
    IReadOnlyList<AuditEntryView> Entries, int Total, int Page, int PageSize, IReadOnlyList<AuditOutcomeCount> Counts);

public sealed class ListAuditEntriesValidator : AbstractValidator<ListAuditEntriesQuery>
{
    public ListAuditEntriesValidator()
    {
        // The binding already refused a name the set does not have (ADR 0043);
        // this is for a caller that builds the query in code, where a cast
        // number is still an AuditOutcome the log has never written.
        RuleFor(query => query.Outcome).IsInEnum();

        // A screen, not an export: the shared ceiling is the one this log had
        // reason to want. An audit log that could be pulled a hundred thousand
        // rows at a time through a browser is an exfiltration endpoint with a
        // table in front of it.
        RuleFor(query => query.Page).IsAPage();
        RuleFor(query => query.PageSize).IsAPageSize();
    }
}

public sealed class ListAuditEntriesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/audit?outcome=denied&page=1&pageSize=20
        app.MapGet("/api/audit",
            async Task<Ok<ListAuditEntriesResult>> (
                   WireEnum<AuditOutcome>? outcome, int? page, int? pageSize,
                   IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(
                    new ListAuditEntriesQuery(outcome?.Value, page ?? Paging.FirstPage, pageSize ?? Paging.DefaultPageSize), ct)))
            // The audit log names customers, agents and subjects, and quotes the
            // payload of every command. It is the single most sensitive read in
            // the system and it is behind the shopkeeper policy for that reason
            // rather than by default.
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Accounts")
            .WithName("ListAuditEntries");
    }
}

public sealed class ListAuditEntriesHandler(IAuditReader audit)
    : IQueryHandler<ListAuditEntriesQuery, ListAuditEntriesResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Accounts);

    public async Task<ListAuditEntriesResult> HandleAsync(
        ListAuditEntriesQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("accounts.list_audit");
        activity?.SetTag("audit.outcome", query.Outcome?.ToString());

        var page = await audit.PageAsync(query.Outcome, query.Page, query.PageSize, cancellationToken);

        activity?.SetTag("audit.returned", page.Entries.Count);
        activity?.SetTag("audit.total", page.Total);

        var counts = Enum.GetValues<AuditOutcome>()
            .Select(each => new AuditOutcomeCount(each, page.CountByOutcome.GetValueOrDefault(each)))
            .ToList();

        return new ListAuditEntriesResult(
            [.. page.Entries.Select(View)],
            page.Total,
            query.Page,
            query.PageSize,
            counts);
    }

    private static AuditEntryView View(AuditEntry entry) =>
        // Flat strings, never the record structs: CustomerId serialises as
        // {"value":"…"} and the wire wants an id (CLAUDE.md).
        new(entry.Id.ToString(),
            entry.CommandType,
            entry.Payload,
            entry.Customer?.Value.ToString(),
            entry.Agent?.Value,
            entry.Subject,
            entry.ActorName,
            entry.Outcome,
            entry.Reason,
            entry.TraceId,
            entry.At);
}
