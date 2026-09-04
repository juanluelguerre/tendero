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
public sealed record ListAuditEntriesQuery(string? Outcome, int Take)
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
    string Outcome,
    string? Reason,
    string? TraceId,
    DateTimeOffset At);

public sealed record ListAuditEntriesResult(IReadOnlyList<AuditEntryView> Entries, int Refused);

public sealed class ListAuditEntriesValidator : AbstractValidator<ListAuditEntriesQuery>
{
    /// <summary>A screen, not an export. An audit log that could be pulled a
    /// hundred thousand rows at a time through a browser is an exfiltration
    /// endpoint with a table in front of it.</summary>
    private const int MaximumTake = 200;

    public ListAuditEntriesValidator()
    {
        RuleFor(query => query.Outcome)
            .Must(outcome => outcome is null || Enum.TryParse<AuditOutcome>(outcome, ignoreCase: true, out _))
            .WithMessage("Outcome must be one of: allowed, denied, failed.");

        RuleFor(query => query.Take).InclusiveBetween(1, MaximumTake);
    }
}

public sealed class ListAuditEntriesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/audit?outcome=denied&take=50
        app.MapGet("/api/audit",
            async Task<Ok<ListAuditEntriesResult>> (
                   string? outcome, int? take,
                   IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(
                    new ListAuditEntriesQuery(outcome, take ?? 50), ct)))
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
        activity?.SetTag("audit.outcome", query.Outcome);

        var outcome = query.Outcome is null
            ? (AuditOutcome?)null
            : Enum.Parse<AuditOutcome>(query.Outcome, ignoreCase: true);

        var entries = await audit.RecentAsync(outcome, query.Take, cancellationToken);

        activity?.SetTag("audit.returned", entries.Count);

        return new ListAuditEntriesResult(
            [.. entries.Select(View)],
            // What the screen leads with. A count of refusals in the page is the
            // number a shopkeeper is actually looking for, and computing it here
            // saves the client from filtering a list it was given filtered.
            entries.Count(entry => entry.Outcome != AuditOutcome.Allowed));
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
            entry.Outcome.ToString().ToLowerInvariant(),
            entry.Reason,
            entry.TraceId,
            entry.At);
}
