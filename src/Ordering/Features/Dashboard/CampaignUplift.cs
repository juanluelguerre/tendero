using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NodaTime;

namespace ElGuerre.Tendero.Ordering.Features.Dashboard;

/// <summary>
/// A day of the campaign: what it sold and the baseline it is measured against.
/// A day still to come is <paramref name="Future"/> — not a day that sold nothing —
/// and today is <paramref name="Partial"/>: half a day against a whole day's
/// baseline, drawn apart and counted in no total until it ends.
/// </summary>
public sealed record UpliftDay(DateTimeOffset Start, Money Sold, Money? Baseline, bool Future, bool Partial);

/// <summary>
/// What a campaign added over what the shop would have sold anyway (R-9, D-7):
/// each day against the average of the same weekday over the four weeks before it
/// opened. The totals are over the days that have ended. With fewer than four
/// weeks of history there is no baseline, and it says so rather than inventing
/// one. An estimate, not a proof: weather, a mention elsewhere and a campaign that
/// overlaps are not told apart. Sales are the lines' price times quantity —
/// before shipping and the order's discounts — and only the category's lines
/// when the campaign is a category's.
/// </summary>
/// <param name="TimeZone">The zone the days were counted in — the shop's (ADR 0046) — so no reader assumes one.</param>
public sealed record CampaignUplift(
    string TimeZone,
    bool HasBaseline,
    Money Sold,
    Money? Baseline,
    /// <summary>What was sold less the baseline: the one amount here that can be negative — a campaign that sold less than an ordinary week.</summary>
    Money? Uplift,
    IReadOnlyList<UpliftDay> Days);

/// <summary>Dates in, figures out: the backoffice reads the window from Pricing and asks with it, so Ordering never reads Pricing.</summary>
public sealed record GetCampaignUpliftQuery(DateTimeOffset From, DateTimeOffset To, string? Category) : IQuery<CampaignUplift>;

/// <summary>The spec's definition, in the shop's days (ADR 0046), with no database.</summary>
public static class CampaignBaseline
{
    public const int Weeks = 4;

    /// <summary>How far back the orders have to be read for a window opening at <paramref name="from"/>.</summary>
    public static DateTimeOffset HistoryFrom(DateTimeZone zone, DateTimeOffset from) =>
        zone.AtStartOfDay(LocalDateOf(zone, from).PlusWeeks(-Weeks)).ToDateTimeOffset();

    public static CampaignUplift Of(
        DateTimeZone zone, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now,
        IReadOnlyList<Order> orders, DateTimeOffset? firstOrderAt, string? category = null)
    {
        var sales = orders.Where(SalesRanking.IsSale).ToList();
        var opening = LocalDateOf(zone, from);
        var currency = DashboardCurrency.Of(sales);
        var hasBaseline = firstOrderAt is { } first && first <= zone.AtStartOfDay(opening.PlusWeeks(-Weeks)).ToDateTimeOffset();

        decimal SoldOn(LocalDate day)
        {
            var (start, end) = (zone.AtStartOfDay(day).ToDateTimeOffset(), zone.AtStartOfDay(day.PlusDays(1)).ToDateTimeOffset());
            return sales
                .Where(order => order.CreatedAt >= start && order.CreatedAt < end)
                .SelectMany(order => order.Lines)
                // A campaign on a department reaches its whole branch, as its promotions do.
                .Where(line => category is null || line.IsIn(category))
                .Sum(line => line.Total.Amount);
        }

        decimal? BaselineOf(LocalDate day)
        {
            if (!hasBaseline) return null;
            // The last same weekday before the campaign opened, and the three before it.
            var last = opening.PlusDays(-1);
            while (last.DayOfWeek != day.DayOfWeek) last = last.PlusDays(-1);
            var weeks = Enumerable.Range(0, Weeks).Select(week => SoldOn(last.PlusWeeks(-week))).ToArray();
            return MinorUnits.Round(weeks.Average(), currency);
        }

        var days = new List<UpliftDay>();
        for (var day = opening; zone.AtStartOfDay(day).ToDateTimeOffset() < to; day = day.PlusDays(1))
        {
            var start = zone.AtStartOfDay(day).ToDateTimeOffset();
            var future = start > now;
            var partial = !future && zone.AtStartOfDay(day.PlusDays(1)).ToDateTimeOffset() > now;
            days.Add(new UpliftDay(
                start, new Money(future ? 0m : SoldOn(day), currency),
                BaselineOf(day) is { } usual ? new Money(usual, currency) : null, future, partial));
        }

        var happened = days.Where(day => !day.Future && !day.Partial).ToList();
        var sold = happened.Sum(day => day.Sold.Amount);
        decimal? baseline = hasBaseline ? happened.Sum(day => day.Baseline?.Amount ?? 0m) : null;

        return new CampaignUplift(
            zone.Id,
            hasBaseline,
            new Money(sold, currency),
            baseline is { } usually ? new Money(usually, currency) : null,
            baseline is { } expected ? new Money(sold - expected, currency) : null,
            days);
    }

    private static LocalDate LocalDateOf(DateTimeZone zone, DateTimeOffset at) => Instant.FromDateTimeOffset(at).InZone(zone).Date;
}

public sealed class CampaignUpliftHandler(IShopHistory history, TimeProvider clock, IOptions<ShopOptions> shop)
    : IQueryHandler<GetCampaignUpliftQuery, CampaignUplift>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<CampaignUplift> HandleAsync(GetCampaignUpliftQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.dashboard.uplift");
        activity?.SetTag("campaign.category", query.Category);

        var zone = DateTimeZoneProviders.Tzdb[shop.Value.TimeZone];
        var orders = await history.OrdersPlacedAsync(CampaignBaseline.HistoryFrom(zone, query.From), query.To, cancellationToken);

        return CampaignBaseline.Of(zone, query.From, query.To, clock.GetUtcNow(), orders,
            await history.FirstLiveOrderAtAsync(cancellationToken), query.Category);
    }
}

public static class UpliftRefusals
{
    public static readonly Refusal WindowNotValid = new(
        "WindowNotValid", 400,
        "La ventana de una campaña abre antes de cerrar, y dura 120 días como mucho.",
        "A campaign's window opens before it closes, and lasts at most 120 days.");
}

public sealed class CampaignUpliftEndpoint : ICarterModule
{
    /// <summary>A campaign's window, at most a season: a longer one is a mistake in the question.</summary>
    private static readonly TimeSpan LongestWindow = TimeSpan.FromDays(120);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard/uplift",
            async Task<Results<Ok<CampaignUplift>, ProblemHttpResult>> (
                DateTimeOffset from, DateTimeOffset to, string? category, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                if (to <= from || to - from > LongestWindow)
                    return TypedResults.Problem(statusCode: UpliftRefusals.WindowNotValid.Status, extensions: UpliftRefusals.WindowNotValid.Said());

                return TypedResults.Ok(await dispatcher.SendAsync(new GetCampaignUpliftQuery(from, to, category), ct));
            })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Dashboard")
            .WithMetadata(new Refuses(UpliftRefusals.WindowNotValid))
            .WithName("GetCampaignUplift");
    }
}
