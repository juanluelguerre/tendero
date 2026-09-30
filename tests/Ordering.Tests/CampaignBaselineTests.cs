using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Dashboard;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using NodaTime;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// What a campaign added over what the shop would have sold anyway (R-9, D-7; the
/// spec's *Definitions*): each day against the average of the same weekday over
/// the four weeks before it opened, counted in the shop's days (ADR 0046). An
/// estimate, not a proof; with fewer than four weeks of history there is none.
/// </summary>
public sealed class CampaignBaselineTests
{
    private static readonly DateTimeZone Madrid = DateTimeZoneProviders.Tzdb["Europe/Madrid"];

    /// <summary>Tendero Days: Thursday 1 October 2026 to Wednesday 7 October, in Madrid.</summary>
    private static readonly DateTimeOffset Opens = new(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset Closes = new(2026, 10, 8, 0, 0, 0, TimeSpan.FromHours(2));

    private static readonly DateTimeOffset LongAgo = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Order Sold(DateTimeOffset at, decimal amount, string? category = null, string? path = null) =>
        OrderBuilder.Default()
            .WithLines(new OrderLine(ProductId.New(), VariantId.New(), "SKU", "Product", null, null, new Money(amount, "EUR"), 1, category, path))
            .At(new TestClock(at))
            .InStatus(OrderStatus.Confirmed);

    /// <summary>Noon on a day in Madrid.</summary>
    private static DateTimeOffset Noon(int month, int day) => new(2026, month, day, 12, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void A_saturday_that_sold_330_against_240_270_250_and_280_has_a_baseline_of_260_and_an_uplift_of_70()
    {
        List<Order> orders = [Sold(Noon(10, 3), 330m), Sold(Noon(9, 26), 240m), Sold(Noon(9, 19), 270m), Sold(Noon(9, 12), 250m), Sold(Noon(9, 5), 280m)];

        var uplift = CampaignBaseline.Of(Madrid, Opens, Closes, now: Noon(10, 3).AddHours(6), orders, firstOrderAt: LongAgo);

        var saturday = uplift.Days[2];
        Assert.Equal((330m, 260m, 70m), (saturday.Sold, saturday.Baseline, saturday.Sold - saturday.Baseline));
        Assert.True(uplift.HasBaseline);
    }

    [Fact]
    public void With_fewer_than_four_weeks_of_history_there_is_no_baseline()
    {
        var uplift = CampaignBaseline.Of(Madrid, Opens, Closes, now: Noon(10, 3), [Sold(Noon(10, 2), 100m)], firstOrderAt: Noon(9, 15));

        Assert.Equal((false, null, null, 100m), (uplift.HasBaseline, uplift.Baseline, uplift.Uplift, uplift.Sold));
        Assert.All(uplift.Days, day => Assert.Null(day.Baseline));
    }

    /// <summary>A day still to come has sold nothing yet — which is not the same as having sold nothing.</summary>
    [Fact]
    public void A_day_of_the_campaign_still_to_come_is_future_not_zero()
    {
        var uplift = CampaignBaseline.Of(Madrid, Opens, Closes, now: Noon(10, 3), [Sold(Noon(9, 5), 280m)], firstOrderAt: LongAgo);

        Assert.Equal([false, false, false, true, true, true, true], uplift.Days.Select(day => day.Future));
        // The totals are over the days that have ended, Thursday and Friday: Saturday
        // is under way (its baseline would be 280 over its four Saturdays), and the
        // days to come add nothing.
        Assert.Equal(0m, uplift.Baseline);
    }

    /// <summary>
    /// Today is under way: half a day's sales against a whole day's baseline would read
    /// as a campaign failing every morning. It is drawn as partial and counted in no
    /// total until it ends (the branch's review, 2026-09-30).
    /// </summary>
    [Fact]
    public void Today_is_partial_and_counts_in_no_total_until_it_ends()
    {
        List<Order> orders = [Sold(Noon(10, 2), 100m), Sold(Noon(10, 3), 40m), Sold(Noon(9, 26), 400m)];

        var uplift = CampaignBaseline.Of(Madrid, Opens, Closes, now: Noon(10, 3), orders, firstOrderAt: LongAgo);

        Assert.Equal([false, false, true, false, false, false, false], uplift.Days.Select(day => day.Partial));
        // The zone the days were counted in travels with them, as the sales series' does (ADR 0046).
        Assert.Equal("Europe/Madrid", uplift.TimeZone);
        Assert.Equal((100m, 0m), (uplift.Sold, uplift.Baseline));
    }

    /// <summary>Sunday 25 October 2026 lasts 25 hours in Madrid: still one Sunday, matched to the four before.</summary>
    [Fact]
    public void A_campaign_across_the_october_clock_change_keeps_its_weekdays()
    {
        var opens = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.FromHours(2));
        var closes = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.FromHours(1));
        var lateOnSunday = new DateTimeOffset(2026, 10, 25, 23, 30, 0, TimeSpan.FromHours(1));
        List<Order> orders = [Sold(lateOnSunday, 100m), Sold(Noon(10, 18), 40m), Sold(Noon(10, 11), 40m), Sold(Noon(10, 4), 40m), Sold(Noon(9, 27), 40m)];

        var uplift = CampaignBaseline.Of(Madrid, opens, closes, now: closes.AddDays(1), orders, firstOrderAt: LongAgo);

        var sunday = uplift.Days.Single(day => day.Sold > 0);
        Assert.Equal((new LocalDate(2026, 10, 25), 40m), (Instant.FromDateTimeOffset(sunday.Start).InZone(Madrid).Date, sunday.Baseline));
    }

    [Fact]
    public void A_category_campaign_counts_only_its_category()
    {
        List<Order> orders = [Sold(Noon(10, 2), 50m, "COOKWARE"), Sold(Noon(10, 2), 80m, "MUGS")];

        var uplift = CampaignBaseline.Of(Madrid, Opens, Closes, now: Noon(10, 3), orders, firstOrderAt: LongAgo, category: "COOKWARE");

        Assert.Equal(50m, uplift.Sold);
    }

    /// <summary>
    /// A campaign on a department reaches everything under it, as its promotions do
    /// (Pricing's PathContains): a pan filed under COOKWARE sells in HOME's campaign.
    /// Found by the branch's review, 2026-09-30.
    /// </summary>
    [Fact]
    public void A_category_campaign_counts_everything_under_its_category()
    {
        List<Order> orders = [Sold(Noon(10, 2), 50m, "COOKWARE", "HOME/KITCHEN/COOKWARE"), Sold(Noon(10, 2), 80m, "SHIRT", "APPAREL/SPORTSWEAR/SHIRT")];

        var uplift = CampaignBaseline.Of(Madrid, Opens, Closes, now: Noon(10, 3), orders, firstOrderAt: LongAgo, category: "HOME");

        Assert.Equal(50m, uplift.Sold);
    }
}
