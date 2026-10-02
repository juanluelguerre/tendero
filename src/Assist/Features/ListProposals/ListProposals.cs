using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Assist.Domain;
using ElGuerre.Tendero.Assist.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Assist.Features.ListProposals;

/// <summary>The Proposals list's chips: what became of a proposal, as a person
/// sorts their work, not the state machine's six states.</summary>
public enum ProposalFilter
{
    /// <summary>Waiting for a person — and an approval whose process died, which
    /// is still somebody's to finish.</summary>
    ToReview,

    Applied,
    Rejected,

    /// <summary>Did not change the shop: the command failed, or what it would
    /// change changed first (stale).</summary>
    Failed,

    All,
}

/// <param name="Ids">The proposals a turn returned, for its card and its review:
/// answered in this order whatever the filter, unpaged.</param>
public sealed record ListProposalsQuery(
    ProposalFilter Filter, IReadOnlyList<ProposalId>? Ids, int Page, int PageSize, string Culture) : IQuery<ProposalsPage>;

public sealed record ProposalFilterCount(ProposalFilter Filter, int Count);

/// <param name="Counts">Every filter, zero included, over all proposals whichever is shown.</param>
public sealed record ProposalsPage(
    IReadOnlyList<ProposalView> Items, int Total, int Page, int PageSize, IReadOnlyList<ProposalFilterCount> Counts);

/// <summary>
/// A proposal as a person reads it, in their language: the action (*Translate a
/// product*), the subject's title and code, its topic — the kind of change, in
/// words (*English name*) — each field now and proposed, the model's reason, what
/// became of it, and where it came from.
/// </summary>
public sealed record ProposalView(
    string Id,
    string Command,
    string Action,
    string Subject,
    string Title,
    string Topic,
    IReadOnlyList<ProposalChangeView> Changes,
    string Reason,
    ProposalStatus Status,
    DateTimeOffset ProposedAt,
    string? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? Result,
    ProposalProvenanceView Provenance);

public sealed record ProposalChangeView(string Field, string? Now, string? Proposed);

public sealed record ProposalProvenanceView(
    string? Profile, string? Model, string? PromptVersion, string? ConversationId, string? TraceId, string AskedBy);

public sealed class ListProposalsValidator : AbstractValidator<ListProposalsQuery>
{
    public ListProposalsValidator()
    {
        RuleFor(query => query.Filter).IsInEnum();
        RuleFor(query => query.Page).IsAPage();
        RuleFor(query => query.PageSize).IsAPageSize();
        RuleFor(query => query.Ids!.Count).LessThanOrEqualTo(ProposalLimits.MaxBatch).When(query => query.Ids is not null);
        RuleFor(query => query.Culture).Must(CultureNegotiation.Supports).WithMessage("Supported cultures: es, en.");
    }
}

public sealed class ListProposalsHandler(IProposalRepository proposals, ProposableCommands registry)
    : IQueryHandler<ListProposalsQuery, ProposalsPage>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Assist);

    /// <summary>Which states each chip shows; All is every state.</summary>
    private static readonly IReadOnlyDictionary<ProposalFilter, ProposalStatus[]> Statuses = new Dictionary<ProposalFilter, ProposalStatus[]>
    {
        [ProposalFilter.ToReview] = [ProposalStatus.Proposed, ProposalStatus.Approved],
        [ProposalFilter.Applied] = [ProposalStatus.Applied],
        [ProposalFilter.Rejected] = [ProposalStatus.Rejected],
        [ProposalFilter.Failed] = [ProposalStatus.Failed, ProposalStatus.Stale],
        [ProposalFilter.All] = Enum.GetValues<ProposalStatus>(),
    };

    public async Task<ProposalsPage> HandleAsync(ListProposalsQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("assist.proposals.list");
        activity?.SetTag("assist.filter", query.Filter.ToString());

        var byStatus = await proposals.CountByStatusAsync(cancellationToken);
        var counts = Enum.GetValues<ProposalFilter>()
            .Select(filter => new ProposalFilterCount(filter, Statuses[filter].Sum(status => byStatus.GetValueOrDefault(status))))
            .ToList();

        IReadOnlyList<ActionProposal> items;
        int total;
        if (query.Ids is { Count: > 0 } ids)
        {
            var found = (await proposals.FindManyAsync(ids, cancellationToken)).ToDictionary(proposal => proposal.Id);
            items = [.. ids.Distinct().Where(found.ContainsKey).Select(id => found[id])];
            total = items.Count;
        }
        else
        {
            var statuses = query.Filter == ProposalFilter.All ? null : Statuses[query.Filter];
            (items, total) = await proposals.PageAsync(statuses, query.Page, query.PageSize, cancellationToken);
        }

        activity?.SetTag("assist.rows", items.Count);
        return new ProposalsPage([.. items.Select(item => View(item, query.Culture))], total, query.Page, query.PageSize, counts);
    }

    private ProposalView View(ActionProposal proposal, string culture)
    {
        var action = registry.Find(proposal.Command)?.Label.In(culture) ?? proposal.Command;
        var display = ProposalDisplay.FromJson(proposal.Display);
        var provenance = proposal.Provenance;

        return new ProposalView(
            proposal.Id.ToString(),
            proposal.Command,
            action,
            proposal.Subject,
            display?.Title.In(culture) ?? proposal.Subject,
            display?.Kind.In(culture) ?? action,
            [.. (display?.Changes ?? []).Select(change => new ProposalChangeView(change.Field.In(culture), change.Now, change.Proposed))],
            proposal.Reason,
            proposal.Status,
            proposal.ProposedAt,
            proposal.DecidedBy,
            proposal.DecidedAt,
            proposal.Result,
            new ProposalProvenanceView(
                provenance.Profile, provenance.Model, provenance.PromptVersion, provenance.ConversationId, provenance.TraceId, provenance.AskedBy));
    }
}

public sealed class ListProposalsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/steward/proposals?filter=ToReview&page=1 — the list of work;
        // ?ids=…&ids=… — a turn's proposals, for its card and its review.
        app.MapGet("/api/steward/proposals",
                async Task<Ok<ProposalsPage>> (
                    WireEnum<ProposalFilter>? filter, Guid[]? ids, int? page, int? pageSize, string? culture, HttpContext http,
                    IQueryDispatcher dispatcher, CancellationToken ct) =>
                {
                    var language = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                    var answer = await dispatcher.SendAsync(new ListProposalsQuery(
                        filter?.Value ?? ProposalFilter.ToReview,
                        ids is { Length: > 0 } ? [.. ids.Select(id => new ProposalId(id))] : null,
                        page ?? Paging.FirstPage,
                        pageSize ?? Paging.DefaultPageSize,
                        language), ct);
                    http.Response.Headers.ContentLanguage = language;
                    http.Response.Headers.Vary = "Accept-Language";
                    return TypedResults.Ok(answer);
                })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Steward")
            .WithName("ListProposals");
    }
}
