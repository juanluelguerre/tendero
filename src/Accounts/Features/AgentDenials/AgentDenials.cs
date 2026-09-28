using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Accounts.Features.AgentDenials;

public sealed record DenialCount(string Reason, int Count);

public sealed record AgentDenialCount(string AgentId, int Count);

public sealed record RecentDenial(string AgentId, string? ActorName, string Command, string Reason, string? Detail, DateTimeOffset At);

/// <summary>
/// What the shop refused agents (P11-7), the dashboard's half of Know Your Agent:
/// how many, why — by the rule or the protocol code that said no — which agents,
/// and the latest few. Read from the audit table, where every denial already is
/// (P11-6); a denial is the most interesting row in the system.
/// </summary>
public sealed record AgentDenialsSummary(
    DashboardPeriod Period,
    int Total,
    IReadOnlyList<DenialCount> ByReason,
    IReadOnlyList<AgentDenialCount> ByAgent,
    IReadOnlyList<RecentDenial> Recent);

public sealed record GetAgentDenialsQuery(DashboardPeriod Period) : IQuery<AgentDenialsSummary>;

public static class AgentDenialFigures
{
    private const int TopRows = 5;

    /// <summary>
    /// The reason a row is counted under: the shop's rule when one of its rules said
    /// no (<c>agent_refused: orders_per_hour: …</c> counts as <c>orders_per_hour</c>),
    /// otherwise the code that opens the audit reason, and <c>denied</c> when there
    /// is none.
    /// </summary>
    public static (string Reason, string? Detail) ReasonOf(string? audited)
    {
        if (string.IsNullOrWhiteSpace(audited))
            return ("denied", null);

        var parts = audited.Split(':', 3, StringSplitOptions.TrimEntries);
        return parts[0] == "agent_refused" && parts.Length > 1
            ? (parts[1], parts.Length > 2 ? parts[2] : null)
            : (parts[0], parts.Length > 1 ? string.Join(": ", parts[1..]) : null);
    }

    public static AgentDenialsSummary Summarise(DashboardPeriod period, IReadOnlyList<AuditEntry> denials) => new(
        period,
        denials.Count,
        [
            .. denials.GroupBy(entry => ReasonOf(entry.Reason).Reason, StringComparer.Ordinal)
                .Select(group => new DenialCount(group.Key, group.Count()))
                .OrderByDescending(reason => reason.Count)
                .ThenBy(reason => reason.Reason, StringComparer.Ordinal)
        ],
        [
            .. denials.Where(entry => entry.Agent is not null)
                .GroupBy(entry => entry.Agent!.Value.Value, StringComparer.Ordinal)
                .Select(group => new AgentDenialCount(group.Key, group.Count()))
                .OrderByDescending(agent => agent.Count)
                .ThenBy(agent => agent.AgentId, StringComparer.Ordinal)
                .Take(TopRows)
        ],
        [
            .. denials.OrderByDescending(entry => entry.At).Take(TopRows).Select(entry =>
            {
                var (reason, detail) = ReasonOf(entry.Reason);
                return new RecentDenial(entry.Agent?.Value ?? "", entry.ActorName, ShortName(entry.CommandType), reason, detail, entry.At);
            })
        ]);

    /// <summary><c>ElGuerre.Tendero.Ordering.Features.CheckoutSessions.CompleteCheckoutSessionCommand</c> reads as <c>CompleteCheckoutSession</c>.</summary>
    private static string ShortName(string commandType)
    {
        var name = commandType[(commandType.LastIndexOf('.') + 1)..];
        return name.EndsWith("Command", StringComparison.Ordinal) ? name[..^"Command".Length] : name;
    }
}

public sealed class GetAgentDenialsHandler(IAgentDenialHistory audit, TimeProvider clock) : IQueryHandler<GetAgentDenialsQuery, AgentDenialsSummary>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Accounts);

    public async Task<AgentDenialsSummary> HandleAsync(GetAgentDenialsQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("accounts.dashboard.agent_denials");
        activity?.SetTag("dashboard.period", MetricLabels.Of(query.Period));

        var window = DashboardWindow.Ending(clock.GetUtcNow(), query.Period);
        return AgentDenialFigures.Summarise(query.Period, await audit.AgentDenialsSinceAsync(window.From, cancellationToken));
    }
}

public sealed class AgentDenialsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/api/dashboard/agent-denials",
                async Task<Results<Ok<AgentDenialsSummary>, BadRequest<string>>> (string? period, IQueryDispatcher dispatcher, CancellationToken ct) =>
                    DashboardPeriods.Parse(period) is { } parsed
                        ? TypedResults.Ok(await dispatcher.SendAsync(new GetAgentDenialsQuery(parsed), ct))
                        : TypedResults.BadRequest(DashboardPeriods.Error))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Dashboard")
            .WithName("GetAgentDenials");
}
