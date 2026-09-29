using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests;

/// <summary>
/// A customer's trail (R-8) and the rules that make keeping it tolerable: each
/// thing once, most recent first, capped, forgotten after ninety days, removable
/// one by one and all at once, and — switched off — neither recorded nor kept.
/// </summary>
public sealed class BrowsingHistoryTests
{
    private static (BrowsingHistory History, TestClock Clock) Fresh()
    {
        var clock = new TestClock();
        return (BrowsingHistory.StartFor(CustomerId.New(), clock), clock);
    }

    private static string[] Views(BrowsingHistory history, TimeProvider clock) =>
        [.. history.Kept(BrowsingKind.View, clock.GetUtcNow()).Select(entry => entry.Key)];

    private static string[] Searches(BrowsingHistory history, TimeProvider clock) =>
        [.. history.Kept(BrowsingKind.Search, clock.GetUtcNow()).Select(entry => entry.Key)];

    [Fact]
    public void A_product_viewed_again_moves_to_the_front_and_is_kept_once()
    {
        var (history, clock) = Fresh();

        history.Record(clock, BrowsingKind.View, "B073WXYZ01");
        clock.Advance(TimeSpan.FromMinutes(1));
        history.Record(clock, BrowsingKind.View, "B07C0FFEE1");
        clock.Advance(TimeSpan.FromMinutes(1));
        history.Record(clock, BrowsingKind.View, "b073wxyz01");

        Assert.Equal(["B073WXYZ01", "B07C0FFEE1"], Views(history, clock));
    }

    [Fact]
    public void The_same_words_in_another_case_are_one_search_spelled_as_last_typed()
    {
        var (history, clock) = Fresh();

        history.Record(clock, BrowsingKind.Search, "zapatillas  running");
        clock.Advance(TimeSpan.FromMinutes(1));
        history.Record(clock, BrowsingKind.Search, " Zapatillas Running ");

        Assert.Equal(["Zapatillas Running"], Searches(history, clock));
    }

    [Fact]
    public void The_trail_keeps_the_last_fifty_products_and_twenty_searches()
    {
        var (history, clock) = Fresh();

        for (var i = 0; i < BrowsingHistory.MaxViews + 5; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            history.Record(clock, BrowsingKind.View, $"P{i:D4}");
            history.Record(clock, BrowsingKind.Search, $"query {i}");
        }

        var views = Views(history, clock);
        Assert.Equal(BrowsingHistory.MaxViews, views.Length);
        Assert.Equal("P0054", views[0]);
        Assert.DoesNotContain("P0004", views);
        Assert.Equal(BrowsingHistory.MaxSearches, Searches(history, clock).Length);
    }

    /// <summary>Forgotten after ninety days whether or not anything was written since — read, the old entry is gone.</summary>
    [Fact]
    public void An_entry_older_than_ninety_days_is_not_kept()
    {
        var (history, clock) = Fresh();

        history.Record(clock, BrowsingKind.View, "B073WXYZ01");
        clock.Advance(BrowsingHistory.Retention - TimeSpan.FromMinutes(1));
        history.Record(clock, BrowsingKind.View, "B07C0FFEE1");
        Assert.Equal(["B07C0FFEE1", "B073WXYZ01"], Views(history, clock));

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(["B07C0FFEE1"], Views(history, clock));
    }

    [Fact]
    public void One_entry_is_removed_and_the_rest_stays()
    {
        var (history, clock) = Fresh();
        history.Record(clock, BrowsingKind.View, "B073WXYZ01");
        history.Record(clock, BrowsingKind.Search, "cafetera");

        Assert.True(history.Forget(clock, BrowsingKind.Search, "CAFETERA"));
        Assert.False(history.Forget(clock, BrowsingKind.Search, "cafetera"));

        Assert.Empty(Searches(history, clock));
        Assert.Equal(["B073WXYZ01"], Views(history, clock));
    }

    [Fact]
    public void Clearing_removes_everything_and_leaves_the_trail_on()
    {
        var (history, clock) = Fresh();
        history.Record(clock, BrowsingKind.View, "B073WXYZ01");
        history.Record(clock, BrowsingKind.Search, "cafetera");

        history.Clear(clock);
        history.Record(clock, BrowsingKind.View, "B07C0FFEE1");

        Assert.Equal(["B07C0FFEE1"], Views(history, clock));
        Assert.Empty(Searches(history, clock));
    }

    /// <summary>Off is off: what was kept is cleared, and nothing is recorded — nor merged — until it is switched on.</summary>
    [Fact]
    public void Switched_off_it_forgets_what_it_kept_and_records_nothing()
    {
        var (history, clock) = Fresh();
        history.Record(clock, BrowsingKind.View, "B073WXYZ01");

        history.SwitchOff(clock);

        Assert.False(history.IsEnabled);
        Assert.Empty(Views(history, clock));
        Assert.False(history.Record(clock, BrowsingKind.View, "B07C0FFEE1"));
        Assert.Equal(0, history.Merge(clock, BrowsingKind.Search, [new TrailEntry("cafetera", clock.GetUtcNow())]));
        Assert.Empty(Views(history, clock));
        Assert.Empty(Searches(history, clock));

        history.SwitchOn(clock);
        Assert.True(history.Record(clock, BrowsingKind.View, "B07C0FFEE1"));
        Assert.Equal(["B07C0FFEE1"], Views(history, clock));
    }

    /// <summary>
    /// The guest's trail at sign-in: folded in by time, each key once at its latest,
    /// a time from the browser's future counted as now, and one past the retention dropped.
    /// </summary>
    [Fact]
    public void A_guest_trail_merges_by_time_and_the_latest_time_of_each_product_wins()
    {
        var (history, clock) = Fresh();
        var now = clock.GetUtcNow();
        history.Record(clock, BrowsingKind.View, "B073WXYZ01");

        var merged = history.Merge(clock, BrowsingKind.View,
        [
            new TrailEntry("B07C0FFEE1", now.AddHours(-2)),
            new TrailEntry("b073wxyz01", now.AddHours(-5)),
            new TrailEntry("B0FUTURE01", now.AddDays(3)),
            new TrailEntry("B0ANCIENT1", now - BrowsingHistory.Retention - TimeSpan.FromDays(1)),
        ]);

        Assert.Equal(2, merged);
        var kept = history.Kept(BrowsingKind.View, now);
        Assert.Equal(["B073WXYZ01", "B07C0FFEE1", "B0FUTURE01"], kept.Select(entry => entry.Key).Order(StringComparer.Ordinal));
        Assert.Equal(now, kept.Single(entry => entry.Key == "B0FUTURE01").At);
        Assert.Equal(now, kept.Single(entry => entry.Key == "B073WXYZ01").At);
    }

    /// <summary>Exactly ninety days old is past the retention, arriving from a browser or already kept.</summary>
    [Fact]
    public void A_merge_neither_takes_nor_keeps_an_entry_exactly_ninety_days_old()
    {
        var (history, clock) = Fresh();
        history.Record(clock, BrowsingKind.View, "B073WXYZ01");
        clock.Advance(BrowsingHistory.Retention);
        var now = clock.GetUtcNow();

        var merged = history.Merge(clock, BrowsingKind.View,
            [new TrailEntry("B0EDGE0001", now - BrowsingHistory.Retention), new TrailEntry("B07C0FFEE1", now)]);

        Assert.Equal(1, merged);
        // Read a minute earlier, the old entry would still be inside the window: it
        // is absent because the merge dropped it, not because the read filtered it.
        Assert.Equal(["B07C0FFEE1"], history.Kept(BrowsingKind.View, now.AddMinutes(-1)).Select(entry => entry.Key));
    }

    /// <summary>Two products viewed at the same instant come back in one order, the code's.</summary>
    [Fact]
    public void Entries_at_the_same_instant_are_kept_in_the_order_of_their_keys()
    {
        var (history, clock) = Fresh();
        var now = clock.GetUtcNow();

        history.Merge(clock, BrowsingKind.View, [new TrailEntry("B0BBBBBBB2", now), new TrailEntry("B0AAAAAAA1", now)]);

        Assert.Equal(["B0AAAAAAA1", "B0BBBBBBB2"], Views(history, clock));
    }

    [Fact]
    public void A_blank_key_records_nothing()
    {
        var (history, clock) = Fresh();

        Assert.False(history.Record(clock, BrowsingKind.Search, "   "));
        Assert.Empty(Searches(history, clock));
    }
}
