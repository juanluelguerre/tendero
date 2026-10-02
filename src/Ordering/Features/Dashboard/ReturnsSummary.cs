using System.Diagnostics;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;

namespace ElGuerre.Tendero.Ordering.Features.Dashboard;

/// <summary>
/// Returns opened in a period, whatever became of them since: units by reason and
/// by SKU. Steward's <c>returns_summary</c> (phase 12, S-3a). Aggregates and
/// products only — no order, no customer and no comment, which is the shopper's
/// own words: a hosted model may read this.
/// </summary>
public sealed record GetReturnsSummaryQuery(DashboardPeriod Period, int Limit = 10) : IQuery<ReturnsSummary>;

/// <param name="Returns">Returns opened in the period.</param>
/// <param name="Units">Units on their lines.</param>
/// <param name="ByReason">Every reason, zero included, in the enum's order.</param>
/// <param name="SkuTotal">Every SKU returned, not only the rows shown.</param>
public sealed record ReturnsSummary(
    DashboardPeriod Period, int Returns, int Units, IReadOnlyList<ReasonUnits> ByReason, int SkuTotal, IReadOnlyList<SkuReturns> BySku);

public sealed record ReasonUnits(ReturnReason Reason, int Units);

public sealed record SkuReturns(string Sku, int Units, IReadOnlyList<ReasonUnits> ByReason);

public static class ReturnsFigures
{
    public static ReturnsSummary Of(DashboardPeriod period, int limit, DashboardWindow window, IReadOnlyList<ReturnRequest> returns)
    {
        // Opened in the window, however it ended. The window ends now, and nothing
        // was opened after now, so its end is inclusive.
        var opened = returns.Where(request => window.From <= request.CreatedAt && request.CreatedAt <= window.To).ToList();
        var lines = opened.SelectMany(request => request.Lines).ToList();

        var bySku = lines
            .GroupBy(line => line.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SkuReturns(group.Key, group.Sum(line => line.Quantity), Reasons(group)))
            .OrderByDescending(row => row.Units)
            .ThenBy(row => row.Sku, StringComparer.Ordinal)
            .ToList();

        return new ReturnsSummary(period, opened.Count, lines.Sum(line => line.Quantity), Reasons(lines), bySku.Count, [.. bySku.Take(limit)]);
    }

    private static IReadOnlyList<ReasonUnits> Reasons(IEnumerable<ReturnLine> lines)
    {
        var units = lines.GroupBy(line => line.Reason).ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));
        return [.. Enum.GetValues<ReturnReason>().Select(reason => new ReasonUnits(reason, units.GetValueOrDefault(reason)))];
    }
}

public sealed class ReturnsSummaryValidator : AbstractValidator<GetReturnsSummaryQuery>
{
    public ReturnsSummaryValidator()
    {
        RuleFor(x => x.Period).IsInEnum();
        RuleFor(x => x.Limit).InclusiveBetween(1, ReadLimits.MaxRows);
    }
}

public sealed class ReturnsSummaryHandler(IShopHistory history, TimeProvider clock) : IQueryHandler<GetReturnsSummaryQuery, ReturnsSummary>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<ReturnsSummary> HandleAsync(GetReturnsSummaryQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.dashboard.returns");
        activity?.SetTag("dashboard.period", MetricLabels.Of(query.Period));
        var window = DashboardWindow.Ending(clock.GetUtcNow(), query.Period);

        // Everything opened since the window began is open or changed since then.
        return ReturnsFigures.Of(query.Period, query.Limit, window,
            await history.ReturnsOpenOrChangedSinceAsync(window.From, cancellationToken));
    }
}
