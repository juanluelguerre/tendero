using System.Diagnostics;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;
using NodaTime;

namespace ElGuerre.Tendero.Ordering.Features.Dashboard;

/// <summary>An hour for a day's series; a calendar day for a week's and a month's.</summary>
public enum SeriesStep { Hour, Day }

/// <summary>What sold in one bucket, which starts at <paramref name="Start"/> and ends where the next one starts.</summary>
public sealed record SalesPoint(DateTimeOffset Start, decimal Revenue, int Orders, int Units);

/// <summary>
/// Sales by hour or by day (R-9), for the period and the one before it, bucketed in
/// <paramref name="TimeZone"/> — the shop's (ADR 0046), so everyone reading it sees
/// the same Monday, as Shopify's and Seller Central's reports do. Test orders and
/// unpaid ones are left out, as in every sales figure.
/// </summary>
/// <param name="Current">Oldest first; the last bucket holds now.</param>
/// <param name="Previous">The same number of buckets, immediately before.</param>
public sealed record SalesSeries(
    DashboardPeriod Period,
    string Currency,
    string TimeZone,
    SeriesStep Step,
    IReadOnlyList<SalesPoint> Current,
    IReadOnlyList<SalesPoint> Previous);

/// <summary>Where each bucket starts, and where the last one ends.</summary>
public sealed record SeriesPlan(SeriesStep Step, IReadOnlyList<DateTimeOffset> Current, IReadOnlyList<DateTimeOffset> Previous, DateTimeOffset End)
{
    /// <summary>The first instant the series reads: the start of the period before.</summary>
    public DateTimeOffset From => Previous[0];
}

public static class SeriesPlans
{
    /// <summary>
    /// The buckets for <paramref name="period"/>, ending with the one that holds
    /// <paramref name="now"/>. Hours are absolute — sixty minutes each, labelled in
    /// the shop's zone — and days are calendar days in <paramref name="zone"/>, so
    /// the day the clocks change is 23 or 25 hours long and the next one still
    /// starts at midnight.
    /// </summary>
    public static SeriesPlan For(DashboardPeriod period, DateTimeOffset now, DateTimeZone zone)
    {
        var (step, count) = period switch
        {
            DashboardPeriod.Day => (SeriesStep.Hour, 24),
            DashboardPeriod.Week => (SeriesStep.Day, 7),
            _ => (SeriesStep.Day, 30),
        };

        var starts = step == SeriesStep.Hour ? Hours(now, count * 2 + 1) : Days(now, zone, count * 2 + 1);

        return new SeriesPlan(step, [.. starts.Skip(count).Take(count)], [.. starts.Take(count)], starts[^1]);
    }

    /// <summary>Hour starts, oldest first; the one before last holds now, the last is where it ends.</summary>
    private static DateTimeOffset[] Hours(DateTimeOffset now, int count)
    {
        var utc = now.ToUniversalTime();
        var thisHour = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);

        return [.. Enumerable.Range(0, count).Select(index => thisHour.AddHours(index - (count - 2)))];
    }

    /// <summary>
    /// The starts of the shop's days, oldest first; the one before last is today's,
    /// the last is tomorrow's. <c>AtStartOfDay</c> rather than midnight: in a zone
    /// whose clocks skip midnight, the day starts at the first instant it has.
    /// </summary>
    private static DateTimeOffset[] Days(DateTimeOffset now, DateTimeZone zone, int count)
    {
        var today = Instant.FromDateTimeOffset(now).InZone(zone).Date;

        return [.. Enumerable.Range(0, count).Select(index => zone.AtStartOfDay(today.PlusDays(index - (count - 2))).ToDateTimeOffset())];
    }
}

public static class SalesSeriesFigures
{
    private const string DefaultCurrency = "EUR";

    /// <summary>The sales in each of <paramref name="plan"/>'s buckets; an empty bucket is a zero.</summary>
    public static SalesSeries Of(DashboardPeriod period, string timeZone, SeriesPlan plan, IReadOnlyList<Order> orders)
    {
        var sales = orders.Where(SalesRanking.IsSale).ToArray();
        var currency = sales.Select(order => order.Currency).FirstOrDefault() ?? DefaultCurrency;

        return new SalesSeries(period, currency, timeZone, plan.Step,
            Points(plan.Current, plan.End, sales),
            Points(plan.Previous, plan.Current[0], sales));
    }

    private static SalesPoint[] Points(IReadOnlyList<DateTimeOffset> starts, DateTimeOffset end, IReadOnlyList<Order> sales) =>
    [
        .. starts.Select((start, index) =>
        {
            var until = index + 1 < starts.Count ? starts[index + 1] : end;
            var inside = sales.Where(order => order.CreatedAt >= start && order.CreatedAt < until).ToArray();

            return new SalesPoint(
                start,
                inside.Sum(order => order.Total.Amount),
                inside.Length,
                inside.Sum(order => order.Lines.Sum(line => line.Quantity)));
        }),
    ];
}

/// <summary>The shop's sales by hour or by day; the zone is the shop's, from configuration (ADR 0046).</summary>
public sealed record GetSalesSeriesQuery(DashboardPeriod Period) : IQuery<SalesSeries>;

public sealed class SalesSeriesHandler(IShopHistory history, TimeProvider clock, IOptions<ShopOptions> shop)
    : IQueryHandler<GetSalesSeriesQuery, SalesSeries>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<SalesSeries> HandleAsync(GetSalesSeriesQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.dashboard.sales_series");
        activity?.SetTag("dashboard.period", MetricLabels.Of(query.Period));

        var zone = DateTimeZoneProviders.Tzdb[shop.Value.TimeZone];
        var plan = SeriesPlans.For(query.Period, clock.GetUtcNow(), zone);
        var orders = await history.OrdersPlacedAsync(plan.From, plan.End, cancellationToken);

        activity?.SetTag("dashboard.series.buckets", plan.Current.Count);
        return SalesSeriesFigures.Of(query.Period, zone.Id, plan, orders);
    }
}
