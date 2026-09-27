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

public sealed record SalesFigures(decimal Revenue, int Orders, int Units, decimal AverageOrderValue);

public sealed record BestSeller(string Sku, string Name, int Units, decimal Revenue);

public sealed record SalesSummary(string Period, string Currency, SalesFigures Current, SalesFigures Previous, IReadOnlyList<BestSeller> BestSellers);

public sealed record GetSalesSummaryQuery(DashboardPeriod Period) : IQuery<SalesSummary>;

// ---------- Operations ----------

public sealed record ReasonCount(string Reason, int Count);

public sealed record OperationsSummary(
    string Period,
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
    int OpenReturns,
    IReadOnlyList<ReasonCount> OpenReturnsByReason,
    decimal Refunded,
    /// <summary>Returns opened in the period against orders placed in it, 0 to 1.</summary>
    decimal ReturnRate);

public sealed record GetOperationsSummaryQuery(DashboardPeriod Period) : IQuery<OperationsSummary>;

// ---------- Checkout funnel ----------

public sealed record CheckoutFunnel(
    string Period,
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
    string Period,
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

    private const int TopRows = 5;

    private const string DefaultCurrency = "EUR";

    /// <summary>
    /// A sale: a live order whose money was authorised and that was not cancelled —
    /// read off the state machine (invariant 5), which is where "paid" is decided.
    /// </summary>
    public static bool IsSale(Order order) =>
        order.Mode == OrderMode.Live
        && order.Status is OrderStatus.PaymentAuthorized or OrderStatus.Confirmed or OrderStatus.Shipped or OrderStatus.Delivered;

    public static SalesSummary Sales(DashboardPeriod period, IReadOnlyList<Order> current, IReadOnlyList<Order> previous)
    {
        var sales = current.Where(IsSale).ToList();

        return new SalesSummary(
            MetricLabels.Of(period),
            CurrencyOf(current.Concat(previous)),
            Figures(sales),
            Figures([.. previous.Where(IsSale)]),
            [
                .. sales.SelectMany(order => order.Lines)
                    .GroupBy(line => line.Sku, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new BestSeller(group.Key, group.First().ProductName, group.Sum(line => line.Quantity), group.Sum(line => line.Total.Amount)))
                    .OrderByDescending(seller => seller.Units)
                    .ThenByDescending(seller => seller.Revenue)
                    .ThenBy(seller => seller.Sku, StringComparer.Ordinal)
                    .Take(TopRows)
            ]);
    }

    public static OperationsSummary Operations(
        DashboardPeriod period, DashboardWindow window, IReadOnlyList<Order> placed, IReadOnlyList<Order> inHand,
        IReadOnlyList<ReturnRequest> returns)
    {
        var live = placed.Where(order => order.Mode == OrderMode.Live).ToList();
        var working = inHand.Where(order => order.Mode == OrderMode.Live).ToList();
        var toShip = working.Where(order => order.Status == OrderStatus.Confirmed).ToList();
        var open = returns.Where(request => request.Status is ReturnStatus.Requested or ReturnStatus.Approved or ReturnStatus.Received).ToList();
        var cancelled = live.Count(order => order.Status == OrderStatus.Cancelled);
        var opened = returns.Count(request => request.CreatedAt >= window.From && request.CreatedAt < window.To);

        return new OperationsSummary(
            MetricLabels.Of(period),
            CurrencyOf(placed),
            toShip.Count,
            toShip.Count(order => order.UpdatedAt <= window.To - LateAfter),
            working.Count(order => order.Status is OrderStatus.Pending or OrderStatus.PaymentAuthorized),
            working.Count(order => order.Status == OrderStatus.PaymentFailed),
            cancelled,
            Rate(cancelled, live.Count),
            open.Count,
            [
                .. open.SelectMany(request => request.Lines)
                    .GroupBy(line => line.Reason)
                    .Select(group => new ReasonCount(MetricLabels.Of(group.Key), group.Sum(line => line.Quantity)))
                    .OrderByDescending(reason => reason.Count)
                    .ThenBy(reason => reason.Reason, StringComparer.Ordinal)
            ],
            returns
                .Where(request => request.Status == ReturnStatus.Refunded && request.UpdatedAt >= window.From && request.UpdatedAt < window.To)
                .Sum(request => request.RefundAmount?.Amount ?? 0m),
            Rate(opened, live.Count));
    }

    public static CheckoutFunnel Funnel(
        DashboardPeriod period, DateTimeOffset now, IReadOnlyList<Cart> carts, IReadOnlyList<Order> placed, IReadOnlySet<CustomerId> returning)
    {
        var checkedOut = carts.Count(cart => cart.Status == CartStatus.CheckedOut);
        var abandoned = carts.Count(cart => cart.Status == CartStatus.Abandoned || cart.HasExpiredAt(now));
        var buyers = placed.Where(IsSale).Select(order => order.CustomerId).Distinct().ToList();

        return new CheckoutFunnel(
            MetricLabels.Of(period),
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
        var sales = placed.Where(IsSale).ToList();
        var byAgents = sales.Where(order => order.PlacedBy is not null).ToList();

        return new AgentSales(
            MetricLabels.Of(period),
            CurrencyOf(placed),
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
            sales.Count == 0 ? 0m : Math.Round(revenue / sales.Count, Money.Decimals, MidpointRounding.AwayFromZero));
    }

    /// <summary>A share, 0 to 1, to four places; nothing over nothing is zero, not a division error.</summary>
    private static decimal Rate(int part, int whole) =>
        whole == 0 ? 0m : Math.Round((decimal)part / whole, 4, MidpointRounding.AwayFromZero);

    private static string CurrencyOf(IEnumerable<Order> orders) =>
        orders.Select(order => order.Currency).FirstOrDefault() ?? DefaultCurrency;
}

public sealed class DashboardHandlers(IShopHistory history, TimeProvider clock) :
    IQueryHandler<GetSalesSummaryQuery, SalesSummary>,
    IQueryHandler<GetOperationsSummaryQuery, OperationsSummary>,
    IQueryHandler<GetCheckoutFunnelQuery, CheckoutFunnel>,
    IQueryHandler<GetAgentSalesQuery, AgentSales>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

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
            await history.ReturnsOpenOrChangedSinceAsync(window.From, cancellationToken));
    }

    public async Task<CheckoutFunnel> HandleAsync(GetCheckoutFunnelQuery query, CancellationToken cancellationToken)
    {
        using var activity = Start("ordering.dashboard.funnel", query.Period);
        var now = clock.GetUtcNow();
        var window = DashboardWindow.Ending(now, query.Period);

        var placed = await history.OrdersPlacedAsync(window.From, window.To, cancellationToken);
        var buyers = placed.Where(DashboardFigures.IsSale).Select(order => order.CustomerId).Distinct().ToList();

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
            async Task<Results<Ok<SalesSummary>, BadRequest<string>>> (string? period, IQueryDispatcher dispatcher, CancellationToken ct) =>
                DashboardPeriods.Parse(period) is { } parsed
                    ? TypedResults.Ok(await dispatcher.SendAsync(new GetSalesSummaryQuery(parsed), ct))
                    : TypedResults.BadRequest(DashboardPeriods.Error))
            .WithName("GetSalesSummary");

        dashboard.MapGet("/operations",
            async Task<Results<Ok<OperationsSummary>, BadRequest<string>>> (string? period, IQueryDispatcher dispatcher, CancellationToken ct) =>
                DashboardPeriods.Parse(period) is { } parsed
                    ? TypedResults.Ok(await dispatcher.SendAsync(new GetOperationsSummaryQuery(parsed), ct))
                    : TypedResults.BadRequest(DashboardPeriods.Error))
            .WithName("GetOperationsSummary");

        dashboard.MapGet("/funnel",
            async Task<Results<Ok<CheckoutFunnel>, BadRequest<string>>> (string? period, IQueryDispatcher dispatcher, CancellationToken ct) =>
                DashboardPeriods.Parse(period) is { } parsed
                    ? TypedResults.Ok(await dispatcher.SendAsync(new GetCheckoutFunnelQuery(parsed), ct))
                    : TypedResults.BadRequest(DashboardPeriods.Error))
            .WithName("GetCheckoutFunnel");

        dashboard.MapGet("/agent-sales",
            async Task<Results<Ok<AgentSales>, BadRequest<string>>> (string? period, IQueryDispatcher dispatcher, CancellationToken ct) =>
                DashboardPeriods.Parse(period) is { } parsed
                    ? TypedResults.Ok(await dispatcher.SendAsync(new GetAgentSalesQuery(parsed), ct))
                    : TypedResults.BadRequest(DashboardPeriods.Error))
            .WithName("GetAgentSales");
    }

}
