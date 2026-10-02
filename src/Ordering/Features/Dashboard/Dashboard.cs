using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Ordering.Features.Dashboard;

// The shopkeeper's dashboard, Ordering's half of it (P11-7): what sold, what is in
// hand, how the checkout converts, and what agents bring. Each figure is its own
// typed query and endpoint, so each widget asks for exactly what it draws and a
// figure without a source is simply not built. Rehearsals are left out everywhere
// (ADR 0038) — they are counted once, in the agents' figures, as rehearsals.

// ---------- Sales ----------

public sealed record SalesFigures(
    decimal Revenue,
    int Orders,
    int Units,
    decimal AverageOrderValue,
    /// <summary>Units over orders (R-9, D-7): how full a basket is, beside how dear.</summary>
    decimal UnitsPerOrder);

public sealed record BestSeller(string Sku, string Name, int Units, decimal Revenue);

public sealed record SalesSummary(DashboardPeriod Period, string Currency, SalesFigures Current, SalesFigures Previous, IReadOnlyList<BestSeller> BestSellers);

public sealed record GetSalesSummaryQuery(DashboardPeriod Period) : IQuery<SalesSummary>;

// ---------- Operations ----------

public sealed record ReasonCount(ReturnReason Reason, int Count);

public sealed record OperationsSummary(
    DashboardPeriod Period,
    string Currency,
    /// <summary>Confirmed and paid, waiting for a parcel.</summary>
    int ToShip,
    /// <summary>To ship and confirmed more than <see cref="DashboardFigures.LateAfter"/> ago.</summary>
    int LateToShip,
    /// <summary>Placed and not yet confirmed: payment authorised, stock being committed.</summary>
    int InProgress,
    int PaymentFailed,
    int Cancelled,
    /// <summary>Cancelled among the orders placed in the period, 0 to 1.</summary>
    decimal CancellationRate,
    /// <summary>The rate the shop holds itself to (the redesign's R-4.5), 0 to 1.</summary>
    decimal CancellationTarget,
    /// <summary><c>within</c>, <c>near</c> or <c>outside</c> the target: decided here, so every screen says the same.</summary>
    string CancellationStanding,
    int OpenReturns,
    IReadOnlyList<ReasonCount> OpenReturnsByReason,
    decimal Refunded,
    /// <summary>Returns opened in the period against orders placed in it, 0 to 1.</summary>
    decimal ReturnRate,
    /// <summary>
    /// Orders of the period the shopper was let down by (R-9, D-7): a return for
    /// damaged or not as described, or cancelled by the shop.
    /// </summary>
    int Defects,
    /// <summary>Defects over the live orders of the period, 0 to 1; null when there were none to divide by.</summary>
    decimal? DefectRate,
    decimal DefectTarget,
    string? DefectStanding,
    /// <summary>Shipped in the period more than <see cref="DashboardFigures.LateAfter"/> after being confirmed.</summary>
    int LateShipments,
    /// <summary>Shipped in the period with both moments recorded: what the late rate divides by.</summary>
    int ShippedCounted,
    /// <summary>Shipped before the shop recorded when: counted apart, never as on time.</summary>
    int ShippedBeforeMoments,
    decimal? LateShipmentRate,
    decimal LateShipmentTarget,
    string? LateShipmentStanding);

public sealed record GetOperationsSummaryQuery(DashboardPeriod Period) : IQuery<OperationsSummary>;

// ---------- Checkout funnel ----------

public sealed record CheckoutFunnel(
    DashboardPeriod Period,
    int CartsStarted,
    int CheckedOut,
    int Abandoned,
    int StillOpen,
    /// <summary>Checked out among the carts started, 0 to 1.</summary>
    decimal Conversion,
    int NewCustomers,
    int ReturningCustomers);

public sealed record GetCheckoutFunnelQuery(DashboardPeriod Period) : IQuery<CheckoutFunnel>;

// ---------- Agents ----------

public sealed record AgentSalesRow(string AgentId, int Orders, decimal Revenue, int Mandated);

public sealed record AgentSales(
    DashboardPeriod Period,
    string Currency,
    int Orders,
    decimal Revenue,
    int AgentOrders,
    decimal AgentRevenue,
    /// <summary>Agent orders placed under a verified mandate (P11-5).</summary>
    int Mandated,
    /// <summary>Test orders agents placed: rehearsals, never counted as sales.</summary>
    int Rehearsals,
    IReadOnlyList<AgentSalesRow> ByAgent);

public sealed record GetAgentSalesQuery(DashboardPeriod Period) : IQuery<AgentSales>;

/// <summary>
/// The arithmetic, as pure functions over what <see cref="IShopHistory"/> read — so
/// every figure is checked by a test with no database, and the handlers only fetch.
/// </summary>
public static class DashboardFigures
{
    /// <summary>How long a confirmed order may wait for its parcel before it is late — Seller Central's measure, at a laboratory's patience.</summary>
    public static readonly TimeSpan LateAfter = TimeSpan.FromHours(48);

    /// <summary>
    /// The cancellation rate a shop holds itself to: Seller Central's published
    /// account-health target, borrowed until the shop has one of its own (the
    /// redesign's R-3, P10).
    /// </summary>
    public const decimal CancellationTarget = 0.025m;

    /// <summary>The order defect rate a shop holds itself to: below 1 %, Seller Central's account health (R-9, D-7).</summary>
    public const decimal DefectTarget = 0.01m;

    /// <summary>
    /// The late shipment rate a shop holds itself to: below 4 % (R-9, D-7). The
    /// target R-3 named and left waiting for the moment each order shipped,
    /// which the order records now.
    /// </summary>
    public const decimal LateShipmentTarget = 0.04m;

    /// <summary>A return for these is the shop's fault, not the shopper's change of mind.</summary>
    private static readonly ReturnReason[] DefectReasons = [ReturnReason.Damaged, ReturnReason.NotAsDescribed];

    /// <summary>Near is the last fifth of the way to the target: time to look, before it is a problem.</summary>
    private const decimal NearFrom = 0.8m;

    /// <summary>Where a rate stands against its target. At the target is near it; only above it is outside.</summary>
    public static string Standing(decimal rate, decimal target) =>
        rate > target ? "outside" : rate > target * NearFrom ? "near" : "within";

    private const int TopRows = 5;

    public static SalesSummary Sales(DashboardPeriod period, IReadOnlyList<Order> current, IReadOnlyList<Order> previous)
    {
        var sales = current.Where(SalesRanking.IsSale).ToList();

        return new SalesSummary(
            period,
            DashboardCurrency.Of(current.Concat(previous)),
            Figures(sales),
            Figures([.. previous.Where(SalesRanking.IsSale)]),
            // The ranking is Ordering's one rule (SalesRanking), which the shop's
            // public rows read too; the dashboard ranks variants, by SKU.
            [
                .. SalesRanking.Rank(sales, SalesRanking.BySku)
                    .Take(TopRows)
                    .Select(sold => new BestSeller(sold.Key, sold.Name, sold.Units, sold.Revenue))
            ]);
    }

    public static OperationsSummary Operations(
        DashboardPeriod period, DashboardWindow window, IReadOnlyList<Order> placed, IReadOnlyList<Order> inHand,
        IReadOnlyList<ReturnRequest> returns, IReadOnlyList<Order>? shipped = null)
    {
        var live = placed.Where(order => order.Mode == OrderMode.Live).ToList();
        var defects = Defects(live, returns);
        var shipping = Shipping(window, shipped ?? []);
        var working = inHand.Where(order => order.Mode == OrderMode.Live).ToList();
        var toShip = working.Where(order => order.Status == OrderStatus.Confirmed).ToList();
        var open = returns.Where(request => request.Status is ReturnStatus.Requested or ReturnStatus.Approved or ReturnStatus.Received).ToList();
        var cancelled = live.Count(order => order.Status == OrderStatus.Cancelled);
        var opened = returns.Count(request => request.CreatedAt >= window.From && request.CreatedAt < window.To);

        return new OperationsSummary(
            period,
            DashboardCurrency.Of(placed),
            toShip.Count,
            toShip.Count(order => order.UpdatedAt <= window.To - LateAfter),
            working.Count(order => order.Status is OrderStatus.Pending or OrderStatus.PaymentAuthorized),
            working.Count(order => order.Status == OrderStatus.PaymentFailed),
            cancelled,
            Rate(cancelled, live.Count),
            CancellationTarget,
            Standing(Rate(cancelled, live.Count), CancellationTarget),
            open.Count,
            [
                .. open.SelectMany(request => request.Lines)
                    .GroupBy(line => line.Reason)
                    .Select(group => new ReasonCount(group.Key, group.Sum(line => line.Quantity)))
                    .OrderByDescending(reason => reason.Count)
                    // Ties by name, not by the order the members were declared in.
                    .ThenBy(reason => reason.Reason.ToString(), StringComparer.Ordinal)
            ],
            returns
                .Where(request => request.Status == ReturnStatus.Refunded && request.UpdatedAt >= window.From && request.UpdatedAt < window.To)
                .Sum(request => request.RefundAmount?.Amount ?? 0m),
            Rate(opened, live.Count),
            defects,
            RateOrNone(defects, live.Count),
            DefectTarget,
            StandingOrNone(RateOrNone(defects, live.Count), DefectTarget),
            shipping.Late,
            shipping.Counted,
            shipping.BeforeMoments,
            RateOrNone(shipping.Late, shipping.Counted),
            LateShipmentTarget,
            StandingOrNone(RateOrNone(shipping.Late, shipping.Counted), LateShipmentTarget));
    }

    /// <summary>
    /// The live orders of the period with a defect: stopped by the shop, or with a
    /// return for a defect reason that stands — one the shop rejected or the
    /// shopper withdrew said nothing was wrong.
    /// </summary>
    private static int Defects(IReadOnlyList<Order> live, IReadOnlyList<ReturnRequest> returns)
    {
        var returnedForADefect = returns
            .Where(request => request.Status is not (ReturnStatus.Rejected or ReturnStatus.Cancelled))
            .Where(request => request.Lines.Any(line => DefectReasons.Contains(line.Reason)))
            .Select(request => request.OrderId)
            .ToHashSet();

        return live.Count(order => order.Stop?.Code == OrderStop.ShopCancelled || returnedForADefect.Contains(order.Id));
    }

    /// <summary>
    /// The live orders shipped in the window, late or not; and those that shipped
    /// before the moments were recorded — windowed by their last change, the only
    /// time they have — which are counted apart.
    /// </summary>
    private static (int Late, int Counted, int BeforeMoments) Shipping(DashboardWindow window, IReadOnlyList<Order> shipped)
    {
        var live = shipped.Where(order => order.Mode == OrderMode.Live).ToList();
        var recorded = live
            .Where(order => order.ShippedAt is { } at && order.ConfirmedAt is not null && at >= window.From && at < window.To)
            .ToList();
        var before = live.Count(order =>
            (order.ShippedAt is null || order.ConfirmedAt is null) && order.UpdatedAt >= window.From && order.UpdatedAt < window.To);

        return (recorded.Count(order => order.ShippedAt - order.ConfirmedAt > LateAfter), recorded.Count, before);
    }

    public static CheckoutFunnel Funnel(
        DashboardPeriod period, DateTimeOffset now, IReadOnlyList<Cart> carts, IReadOnlyList<Order> placed, IReadOnlySet<CustomerId> returning)
    {
        var checkedOut = carts.Count(cart => cart.Status == CartStatus.CheckedOut);
        var abandoned = carts.Count(cart => cart.Status == CartStatus.Abandoned || cart.HasExpiredAt(now));
        var buyers = placed.Where(SalesRanking.IsSale).Select(order => order.CustomerId).Distinct().ToList();

        return new CheckoutFunnel(
            period,
            carts.Count,
            checkedOut,
            abandoned,
            carts.Count - checkedOut - abandoned,
            Rate(checkedOut, carts.Count),
            buyers.Count(customer => !returning.Contains(customer)),
            buyers.Count(returning.Contains));
    }

    public static AgentSales Agents(DashboardPeriod period, IReadOnlyList<Order> placed)
    {
        var sales = placed.Where(SalesRanking.IsSale).ToList();
        var byAgents = sales.Where(order => order.PlacedBy is not null).ToList();

        return new AgentSales(
            period,
            DashboardCurrency.Of(placed),
            sales.Count,
            sales.Sum(order => order.Total.Amount),
            byAgents.Count,
            byAgents.Sum(order => order.Total.Amount),
            byAgents.Count(order => order.Mandate is not null),
            placed.Count(order => order.PlacedBy is not null && order.Mode == OrderMode.Test),
            [
                .. byAgents.GroupBy(order => order.PlacedBy!.Value)
                    .Select(group => new AgentSalesRow(group.Key.Value, group.Count(), group.Sum(order => order.Total.Amount), group.Count(order => order.Mandate is not null)))
                    .OrderByDescending(row => row.Revenue)
                    .ThenBy(row => row.AgentId, StringComparer.Ordinal)
                    .Take(TopRows)
            ]);
    }

    private static SalesFigures Figures(IReadOnlyList<Order> sales)
    {
        var revenue = sales.Sum(order => order.Total.Amount);
        return new SalesFigures(
            revenue,
            sales.Count,
            sales.Sum(order => order.Lines.Sum(line => line.Quantity)),
            sales.Count == 0 ? 0m : Math.Round(revenue / sales.Count, Money.Decimals, MidpointRounding.AwayFromZero),
            sales.Count == 0 ? 0m : Math.Round((decimal)sales.Sum(order => order.Lines.Sum(line => line.Quantity)) / sales.Count, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>A share, 0 to 1, to four places; nothing over nothing is zero, not a division error.</summary>
    private static decimal Rate(int part, int whole) =>
        whole == 0 ? 0m : Math.Round((decimal)part / whole, 4, MidpointRounding.AwayFromZero);

    /// <summary>A rate with nothing to divide by is no rate — *no data*, not 0 %.</summary>
    private static decimal? RateOrNone(int part, int whole) => whole == 0 ? null : Rate(part, whole);

    private static string? StandingOrNone(decimal? rate, decimal target) => rate is { } value ? Standing(value, target) : null;
}

public sealed class DashboardHandlers(IShopHistory history, TimeProvider clock) :
    IQueryHandler<GetSalesSummaryQuery, SalesSummary>,
    IQueryHandler<GetOperationsSummaryQuery, OperationsSummary>,
    IQueryHandler<GetCheckoutFunnelQuery, CheckoutFunnel>,
    IQueryHandler<GetAgentSalesQuery, AgentSales>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    /// <summary>Orders that have left: what the late shipment rate is counted over.</summary>
    private static readonly OrderStatus[] Shipped = [OrderStatus.Shipped, OrderStatus.Delivered];

    private static readonly OrderStatus[] InHand =
        [OrderStatus.Pending, OrderStatus.PaymentAuthorized, OrderStatus.PaymentFailed, OrderStatus.Confirmed];

    public async Task<SalesSummary> HandleAsync(GetSalesSummaryQuery query, CancellationToken cancellationToken)
    {
        using var activity = Start("ordering.dashboard.sales", query.Period);
        var window = DashboardWindow.Ending(clock.GetUtcNow(), query.Period);

        return DashboardFigures.Sales(query.Period,
            await history.OrdersPlacedAsync(window.From, window.To, cancellationToken),
            await history.OrdersPlacedAsync(window.PreviousFrom, window.From, cancellationToken));
    }

    public async Task<OperationsSummary> HandleAsync(GetOperationsSummaryQuery query, CancellationToken cancellationToken)
    {
        using var activity = Start("ordering.dashboard.operations", query.Period);
        var window = DashboardWindow.Ending(clock.GetUtcNow(), query.Period);

        return DashboardFigures.Operations(query.Period, window,
            await history.OrdersPlacedAsync(window.From, window.To, cancellationToken),
            await history.OrdersInAsync(InHand, cancellationToken),
            await history.ReturnsOpenOrChangedSinceAsync(window.From, cancellationToken),
            await history.OrdersInAsync(Shipped, cancellationToken));
    }

    public async Task<CheckoutFunnel> HandleAsync(GetCheckoutFunnelQuery query, CancellationToken cancellationToken)
    {
        using var activity = Start("ordering.dashboard.funnel", query.Period);
        var now = clock.GetUtcNow();
        var window = DashboardWindow.Ending(now, query.Period);

        var placed = await history.OrdersPlacedAsync(window.From, window.To, cancellationToken);
        var buyers = placed.Where(SalesRanking.IsSale).Select(order => order.CustomerId).Distinct().ToList();

        return DashboardFigures.Funnel(query.Period, now,
            await history.CartsStartedAsync(window.From, window.To, cancellationToken),
            placed,
            await history.CustomersWhoOrderedBeforeAsync(buyers, window.From, cancellationToken));
    }

    public async Task<AgentSales> HandleAsync(GetAgentSalesQuery query, CancellationToken cancellationToken)
    {
        using var activity = Start("ordering.dashboard.agents", query.Period);
        var window = DashboardWindow.Ending(clock.GetUtcNow(), query.Period);

        return DashboardFigures.Agents(query.Period, await history.OrdersPlacedAsync(window.From, window.To, cancellationToken));
    }

    private static Activity? Start(string name, DashboardPeriod period)
    {
        var activity = Telemetry.StartActivity(name);
        activity?.SetTag("dashboard.period", MetricLabels.Of(period));
        return activity;
    }
}

public sealed class DashboardEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var dashboard = app.MapGroup("/api/dashboard")
            // What a shop sold and how it converts is commercial information.
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Dashboard");

        dashboard.MapGet("/sales",
            async Task<Ok<SalesSummary>> (WireEnum<DashboardPeriod>? period, IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new GetSalesSummaryQuery(period?.Value ?? DashboardPeriods.Default), ct)))
            .WithName("GetSalesSummary");

        // Sales by hour or by day on the shop's clock (R-9, ADR 0046): the zone is the
        // shop's configuration, not a parameter, so every reader sees the same Monday.
        dashboard.MapGet("/sales/series",
            async Task<Ok<SalesSeries>> (WireEnum<DashboardPeriod>? period, IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new GetSalesSeriesQuery(period?.Value ?? DashboardPeriods.Default), ct)))
            .WithName("GetSalesSeries");

        dashboard.MapGet("/operations",
            async Task<Ok<OperationsSummary>> (WireEnum<DashboardPeriod>? period, IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new GetOperationsSummaryQuery(period?.Value ?? DashboardPeriods.Default), ct)))
            .WithName("GetOperationsSummary");

        dashboard.MapGet("/funnel",
            async Task<Ok<CheckoutFunnel>> (WireEnum<DashboardPeriod>? period, IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new GetCheckoutFunnelQuery(period?.Value ?? DashboardPeriods.Default), ct)))
            .WithName("GetCheckoutFunnel");

        dashboard.MapGet("/agent-sales",
            async Task<Ok<AgentSales>> (WireEnum<DashboardPeriod>? period, IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new GetAgentSalesQuery(period?.Value ?? DashboardPeriods.Default), ct)))
            .WithName("GetAgentSales");

        dashboard.MapGet("/mix",
            async Task<Ok<SalesMix>> (WireEnum<DashboardPeriod>? period, IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new GetSalesMixQuery(period?.Value ?? DashboardPeriods.Default), ct)))
            .WithName("GetSalesMix");

        // The products that sell most or least (phase 12, S-3a), under a category
        // and everything below it when one is given.
        dashboard.MapGet("/products",
            async Task<Ok<ProductPerformance>> (WireEnum<DashboardPeriod>? period, WireEnum<PerformanceRanking>? ranking,
                string? category, int? limit, IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new GetProductPerformanceQuery(
                    period?.Value ?? DashboardPeriods.Default, ranking?.Value ?? PerformanceRanking.Best, category, limit ?? 10), ct)))
            .WithName("GetProductPerformance");

        // Returns opened in a period, by reason and by SKU — nobody's words in it (S-3a).
        dashboard.MapGet("/returns",
            async Task<Ok<ReturnsSummary>> (WireEnum<DashboardPeriod>? period, int? limit, IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new GetReturnsSummaryQuery(period?.Value ?? DashboardPeriods.Default, limit ?? 10), ct)))
            .WithName("GetReturnsSummary");
    }

}
