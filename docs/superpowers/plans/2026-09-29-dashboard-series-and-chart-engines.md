# R-9 · Plan 1 — the daily sales series, and two chart engines compared

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** give the dashboard its first time series (sales per hour or per day, against the period before) and draw it as *Sales by day* with **both** chart engines behind one component contract, measured side by side so the author can keep one.

**Architecture:** Ordering computes the series as a pure function over the orders `IShopHistory` already returns, bucketed in the reader's time zone, and serves it at `GET /api/dashboard/sales/series`. In the backoffice, a `BarChart` component is the contract — data, format, caption in; a drawing and a table of its figures out — and an injection token picks the engine behind it: our own SVG, or Apache ECharts in a deferred chunk. A lab page shows both on the same data and times their redraws; a small script measures what each adds to the bundle.

**Tech Stack:** .NET 11, Carter, custom CQRS, FluentValidation, xUnit; Angular 22 (signals, `rxResource`, `@defer`), Transloco, Vitest 4; `echarts` 6.1.0 (Apache-2.0) for the comparison only.

**Spec:** `docs/specs/backoffice-dashboard.md` (approved 2026-09-29). This is Plan 1 of four: Plan 2 builds the widgets, the tabs, full screen and the phone on the engine that wins; Plan 3 the new data; Plan 4 the close.

## Global Constraints

- Branch `feature/backoffice-dashboard`; commits in Conventional Commits, English, **no attribution trailers** (CLAUDE.md).
- `dotnet build -warnaserror` clean; `dotnet test` green, architecture tests included.
- Every figure is computed on the server by the context that owns it; test orders (`OrderMode.Test`) are left out of every sales figure (`SalesRanking.IsSale`).
- A closed set is an enum on the wire, bound by name (ADR 0043).
- After changing a response: `UPDATE_OPENAPI=1 dotnet test tests/Api.Tests`, then `npm run generate:api-types` from `frontend/`. Types come from `@tendero/shared-api`, never hand-written.
- UI colours from tokens only — no hex in any `.css`, no palette step (`--clay-600`…) in `frontend/**/*.css`.
- An Angular component is three files (`.ts`, `.html`, `.css`); user-facing strings in `es` and `en` via Transloco.
- `features/**` never uses `HttpClient`; HTTP lives in `data-access/`.
- New npm dependency: `echarts@6.1.0` only, approved by the author for this comparison, recorded as tier 1 in an ADR with its removal condition; **no `ngx-echarts`**.
- A test written for code that already exists earns its place with a hand mutation, run and restored, named in the commit.

## Review Focus

1. **An order near midnight** — 23:30 UTC on a Saturday is Sunday 01:30 in Madrid: it must land in Sunday's bar for a Madrid reader. (Task 1, `A_late_order_lands_on_the_readers_day`.)
2. **The day the clocks go back** — 25 October 2026 in Madrid has 25 hours; its bucket must hold all of them and the next one must start at local midnight. (Task 1, `A_day_is_a_calendar_day_across_a_clock_change`.)
3. **A time zone nobody has** — `?timeZone=Mars/Olympus` must be a 400 with a sentence, never a 500. (Task 2.)
4. **A period with nothing sold** — every bucket present with zeros, so the chart draws an empty week rather than no week. (Task 1, `Empty_buckets_are_zero_not_missing`.)
5. **A screen reader** — the figures must be readable without the drawing, with either engine: the same table under both. (Task 5, `the table carries every figure whatever draws it`.)

---

## File structure

**Backend (Ordering, one slice):**
- Create `src/Ordering/Features/Dashboard/SalesSeries.cs` — the records, the bucket plan, the pure series function, the query, its validator and its handler. One file: it is one figure, and `Dashboard.cs` is already 328 lines.
- Modify `src/Ordering/Features/Dashboard/Dashboard.cs` — one route in `DashboardEndpoints`.
- Create `tests/Ordering.Tests/SalesSeriesTests.cs`.
- Modify `tests/Api.Tests/DashboardPeriodBindingTests.cs` — the new route's binding and its 400.
- Regenerated: `docs/openapi/tendero.json`, `frontend/libs/shared/api/src/lib/generated/schema.ts`.
- Modify `frontend/libs/shared/api/src/lib/ordering-contracts.ts` — three aliases.

**Design tokens:**
- Modify `design/tokens.css` — `--chart-data`, `--chart-agent`, `--chart-previous`, light and dark.
- Modify `frontend/libs/shared/i18n/src/lib/design-contrast.spec.ts` — marks at 3:1 on every surface.
- Modify `design/unpublished/mockups/proposals.json` — the three proposals leave it once the tokens own them.

**Backoffice charts (`frontend/apps/backoffice/src/app/charts/`, app-local per ADR 0010 — only the backoffice draws charts):**
- `chart-contract.ts` — the types and the engine token.
- `scale.ts` + `scale.spec.ts` — round maxima and ticks, pure.
- `bar-chart.{ts,html,css}` + `bar-chart.spec.ts` — the contract component: caption, table, engine switch.
- `svg-bar-chart.{ts,html,css}` + `svg-bar-chart.spec.ts` — engine 1.
- `echarts-bar-options.ts` + `echarts-bar-options.spec.ts` — engine 2's options, pure.
- `echarts.directive.ts` — engine 2's binding (~40 lines).
- `echarts-bar-chart.{ts,html,css}` — engine 2.
- `chart-colours.ts` — the tokens read for engine 2.

**Backoffice dashboard:**
- Modify `frontend/apps/backoffice/src/app/data-access/ordering.service.ts` — `salesSeries()`.
- Create `frontend/apps/backoffice/src/app/features/dashboard/sales-series.ts` + `.spec.ts` — the series as chart data, pure.
- Create `frontend/apps/backoffice/src/app/features/dashboard/sales-by-day-widget.{ts,html,css}`.
- Modify `dashboard-page.html` — the widget in the grid.
- Create `frontend/apps/backoffice/src/app/features/dashboard/lab/chart-engines-page.{ts,html,css}` — both engines side by side, with timings; route `dashboard/lab/charts`.
- Modify `frontend/apps/backoffice/src/app/app.routes.ts`, `frontend/apps/backoffice/public/i18n/{en,es}.json`.

**Measure and decide:**
- Create `tools/chart-engines/measure.mjs` — bytes per engine from the build's stats, lines of code per engine.
- Create `docs/adr/0045-echarts-is-an-experiment-with-a-removal-condition.md`; modify `docs/adr/README.md`.
- Modify `docs/specs/backoffice-dashboard.md` (*As measured*), `docs/blog/notebook.md`, `docs/delivery-plan.md`.

---

### Task 1: The sales series, as a pure function

**Files:**
- Create: `src/Ordering/Features/Dashboard/SalesSeries.cs`
- Test: `tests/Ordering.Tests/SalesSeriesTests.cs`

**Interfaces:**
- Consumes: `Order` (`CreatedAt`, `Total`, `Lines`, `Currency`), `SalesRanking.IsSale`, `DashboardPeriod`, `Money.Decimals`.
- Produces:
  - `public enum SeriesStep { Hour, Day }`
  - `public sealed record SalesPoint(DateTimeOffset Start, decimal Revenue, int Orders, int Units)`
  - `public sealed record SalesSeries(DashboardPeriod Period, string Currency, string TimeZone, SeriesStep Step, IReadOnlyList<SalesPoint> Current, IReadOnlyList<SalesPoint> Previous)`
  - `public sealed record SeriesPlan(SeriesStep Step, IReadOnlyList<DateTimeOffset> Current, IReadOnlyList<DateTimeOffset> Previous, DateTimeOffset End)` with `From => Previous[0]`
  - `public static class SeriesPlans { public static SeriesPlan For(DashboardPeriod period, DateTimeOffset now, TimeZoneInfo zone) }`
  - `public static class SalesSeriesFigures { public static SalesSeries Of(DashboardPeriod period, string timeZone, SeriesPlan plan, IReadOnlyList<Order> orders) }`

- [ ] **Step 1: Write the failing tests**

```csharp
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Dashboard;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// Sales by hour or by day, against the period before (R-9): the dashboard's first
/// series. A day is the reader's calendar day, not UTC's — an order at 23:30 UTC on
/// a Saturday is Sunday in Madrid — and a bucket with nothing in it is a zero, so
/// an empty week still draws as a week.
/// </summary>
public sealed class SalesSeriesTests
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    private static OrderLine Line(decimal price, int quantity) =>
        new(ProductId.New(), VariantId.New(), "SKU", "Product", null, null, new Money(price, "EUR"), quantity);

    private static Order SaleAt(DateTimeOffset at, decimal price = 10m, int quantity = 1) =>
        OrderBuilder.Default().WithLines(Line(price, quantity)).At(new TestClock(at)).InStatus(OrderStatus.Confirmed);

    [Fact]
    public void A_week_is_seven_calendar_days_ending_today_and_the_seven_before()
    {
        // Wednesday 14 October 2026, 10:00 in Madrid (08:00 UTC, summer time).
        var now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

        var plan = SeriesPlans.For(DashboardPeriod.Week, now, Madrid);

        Assert.Equal(SeriesStep.Day, plan.Step);
        Assert.Equal(7, plan.Current.Count);
        Assert.Equal(7, plan.Previous.Count);
        // Thursday 8 October at local midnight (22:00 UTC the day before) opens the week.
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.FromHours(2)), plan.Current[0]);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.FromHours(2)), plan.Current[^1]);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.FromHours(2)), plan.End);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(2)), plan.From);
    }

    [Fact]
    public void A_day_is_twenty_four_hours_ending_with_this_one()
    {
        var now = new DateTimeOffset(2026, 10, 14, 8, 40, 0, TimeSpan.Zero);

        var plan = SeriesPlans.For(DashboardPeriod.Day, now, Madrid);

        Assert.Equal((SeriesStep.Hour, 24, 24), (plan.Step, plan.Current.Count, plan.Previous.Count));
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero), plan.Current[^1]);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero), plan.End);
    }

    [Fact]
    public void A_month_is_thirty_days()
    {
        var plan = SeriesPlans.For(DashboardPeriod.Month, new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero), Madrid);

        Assert.Equal((SeriesStep.Day, 30, 30), (plan.Step, plan.Current.Count, plan.Previous.Count));
    }

    [Fact]
    public void A_late_order_lands_on_the_readers_day()
    {
        // Saturday 10 October, 23:30 UTC = Sunday 11 October, 01:30 in Madrid.
        var now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
        var plan = SeriesPlans.For(DashboardPeriod.Week, now, Madrid);
        var late = SaleAt(new DateTimeOffset(2026, 10, 10, 23, 30, 0, TimeSpan.Zero));

        var series = SalesSeriesFigures.Of(DashboardPeriod.Week, "Europe/Madrid", plan, [late]);

        var sunday = series.Current.Single(point => point.Start == new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.FromHours(2)));
        Assert.Equal(1, sunday.Orders);
        Assert.Equal(1, series.Current.Sum(point => point.Orders));
    }

    [Fact]
    public void A_day_is_a_calendar_day_across_a_clock_change()
    {
        // Sunday 25 October 2026: Madrid goes from +02:00 to +01:00 at 03:00, a 25-hour day.
        var now = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

        var plan = SeriesPlans.For(DashboardPeriod.Week, now, Madrid);

        var sunday = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(2));
        var monday = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.FromHours(1));
        var at = plan.Current.ToList().IndexOf(sunday);
        Assert.True(at >= 0);
        Assert.Equal(monday, plan.Current[at + 1]);
        Assert.Equal(TimeSpan.FromHours(25), plan.Current[at + 1] - plan.Current[at]);
    }

    [Fact]
    public void Each_bucket_sums_its_sales_and_the_period_before_is_its_own()
    {
        var now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
        var plan = SeriesPlans.For(DashboardPeriod.Week, now, Madrid);
        List<Order> orders =
        [
            SaleAt(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero), 10m, 2),   // this Tuesday
            SaleAt(new DateTimeOffset(2026, 10, 13, 18, 0, 0, TimeSpan.Zero), 30m, 1),   // this Tuesday
            SaleAt(new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero), 10m, 1),    // Tuesday the week before
        ];

        var series = SalesSeriesFigures.Of(DashboardPeriod.Week, "Europe/Madrid", plan, orders);

        // 2 × 10 + 4.95 shipping, and 30 + 4.95.
        Assert.Equal(new SalesPoint(plan.Current[5], 59.90m, 2, 3), series.Current[5]);
        Assert.Equal(new SalesPoint(plan.Previous[5], 14.95m, 1, 1), series.Previous[5]);
        Assert.Equal(("EUR", "Europe/Madrid", SeriesStep.Day), (series.Currency, series.TimeZone, series.Step));
    }

    [Fact]
    public void Test_orders_and_unpaid_ones_are_not_sales()
    {
        var now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
        var plan = SeriesPlans.For(DashboardPeriod.Week, now, Madrid);
        var at = new TestClock(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero));
        List<Order> orders =
        [
            OrderBuilder.Default().AsTest().At(at).InStatus(OrderStatus.Confirmed),
            OrderBuilder.Default().At(at).InStatus(OrderStatus.Pending),
            OrderBuilder.Default().At(at).InStatus(OrderStatus.Cancelled),
        ];

        var series = SalesSeriesFigures.Of(DashboardPeriod.Week, "Europe/Madrid", plan, orders);

        Assert.All(series.Current, point => Assert.Equal(0, point.Orders));
    }

    [Fact]
    public void Empty_buckets_are_zero_not_missing()
    {
        var plan = SeriesPlans.For(DashboardPeriod.Week, new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero), Madrid);

        var series = SalesSeriesFigures.Of(DashboardPeriod.Week, "Europe/Madrid", plan, []);

        Assert.Equal(plan.Current, series.Current.Select(point => point.Start));
        Assert.All(series.Current.Concat(series.Previous), point => Assert.Equal(new SalesPoint(point.Start, 0m, 0, 0), point));
        Assert.Equal("EUR", series.Currency);
    }

    [Fact]
    public void An_order_on_the_boundary_belongs_to_the_bucket_it_opens()
    {
        var now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
        var plan = SeriesPlans.For(DashboardPeriod.Week, now, Madrid);

        var series = SalesSeriesFigures.Of(DashboardPeriod.Week, "Europe/Madrid", plan, [SaleAt(plan.Current[3])]);

        Assert.Equal(1, series.Current[3].Orders);
        Assert.Equal(0, series.Current[2].Orders);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test --project tests/Ordering.Tests -- --filter-class "*SalesSeriesTests"`
Expected: build error — `SeriesPlans`, `SalesSeriesFigures`, `SalesPoint` do not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Features.Dashboard;

/// <summary>An hour for a day's series; a calendar day for a week's and a month's.</summary>
public enum SeriesStep { Hour, Day }

/// <summary>What sold in one bucket, which starts at <paramref name="Start"/> and ends where the next one starts.</summary>
public sealed record SalesPoint(DateTimeOffset Start, decimal Revenue, int Orders, int Units);

/// <summary>
/// Sales by hour or by day (R-9), for the period and the one before it, bucketed in
/// <paramref name="TimeZone"/> — the reader's, so a day is the day they lived. Test
/// orders and unpaid ones are left out, as in every sales figure.
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
    /// the reader's zone — and days are calendar days in <paramref name="zone"/>, so
    /// the day the clocks change is 23 or 25 hours long and the next one still
    /// starts at midnight.
    /// </summary>
    public static SeriesPlan For(DashboardPeriod period, DateTimeOffset now, TimeZoneInfo zone)
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

    /// <summary>Local midnights, oldest first; the one before last is today's, the last is tomorrow's.</summary>
    private static DateTimeOffset[] Days(DateTimeOffset now, TimeZoneInfo zone, int count)
    {
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;

        return [.. Enumerable.Range(0, count).Select(index => AtMidnight(today.AddDays(index - (count - 2)), zone))];
    }

    private static DateTimeOffset AtMidnight(DateTime localDate, TimeZoneInfo zone) =>
        new(localDate, zone.GetUtcOffset(localDate));
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
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test --project tests/Ordering.Tests -- --filter-class "*SalesSeriesTests"`
Expected: PASS, 9 tests. If `A_day_is_a_calendar_day_across_a_clock_change` fails with a missing zone on Windows, check `TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid")` resolves (ICU is on by default in .NET 5+); do not replace the IANA id with a Windows id.

- [ ] **Step 5: Hand mutation, then commit**

Change `order.CreatedAt >= start` to `order.CreatedAt > start` in `Points`; run the filter; expect `An_order_on_the_boundary_belongs_to_the_bucket_it_opens` red; restore; run again green.

```bash
git add src/Ordering/Features/Dashboard/SalesSeries.cs tests/Ordering.Tests/SalesSeriesTests.cs
git commit -m "feat(ordering): sales by hour or by day, in the reader's time zone (R-9)" -m "The dashboard's first series: a pure function over the orders the shop history already returns, bucketed as absolute hours for a day and calendar days for a week or a month, with the period before beside it. Hand mutation: an order exactly on a bucket's start counted in the bucket before (>= to >); the boundary test went red."
```

---

### Task 2: The series on the wire

**Files:**
- Modify: `src/Ordering/Features/Dashboard/SalesSeries.cs` (append the query, validator, handler)
- Modify: `src/Ordering/Features/Dashboard/Dashboard.cs:298-328` (one route)
- Modify: `tests/Api.Tests/DashboardPeriodBindingTests.cs`
- Modify: `frontend/libs/shared/api/src/lib/ordering-contracts.ts:110-117`
- Regenerated: `docs/openapi/tendero.json`, `frontend/libs/shared/api/src/lib/generated/schema.ts`

**Interfaces:**
- Consumes: Task 1's `SeriesPlans.For`, `SalesSeriesFigures.Of`; `IShopHistory.OrdersPlacedAsync(from, to, ct)`; `TimeProvider`.
- Produces: `GET /api/dashboard/sales/series?period=&timeZone=` → `SalesSeries`, operation id `GetSalesSeries`; frontend types `SalesSeries`, `SalesPoint`, `SeriesStep` from `@tendero/shared-api`.

- [ ] **Step 1: Write the failing binding tests**

Append to `DashboardPeriodBindingTests` (same class, its `LetEveryoneThrough` reused):

```csharp
    private sealed class RecordingSeriesDispatcher : IQueryDispatcher
    {
        public GetSalesSeriesQuery? Asked { get; private set; }

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            Asked = query as GetSalesSeriesQuery;
            var plan = SeriesPlans.For(Asked!.Period, DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
            return Task.FromResult((TResult)(object)SalesSeriesFigures.Of(Asked.Period, Asked.TimeZone, plan, []));
        }
    }

    [Theory]
    [InlineData(null, null, DashboardPeriod.Week, "UTC")]
    [InlineData("day", "Europe/Madrid", DashboardPeriod.Day, "Europe/Madrid")]
    public async Task The_series_reads_its_period_and_the_readers_time_zone(string? period, string? timeZone, DashboardPeriod expected, string zone)
    {
        var (status, asked) = await Series(period, timeZone);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((expected, zone), (asked!.Period, asked.TimeZone));
    }

    [Fact]
    public async Task A_time_zone_nobody_has_is_refused_with_a_sentence()
    {
        using var root = new TenderoApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IPolicyEvaluator, LetEveryoneThrough>()));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/dashboard/sales/series?timeZone=Mars%2FOlympus", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("IANA time zone", body, StringComparison.Ordinal);
    }

    private static async Task<(HttpStatusCode Status, GetSalesSeriesQuery? Asked)> Series(string? period, string? timeZone)
    {
        var dispatcher = new RecordingSeriesDispatcher();
        using var root = new TenderoApiFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IQueryDispatcher>(dispatcher);
            services.AddSingleton<IPolicyEvaluator, LetEveryoneThrough>();
        }));
        using var client = factory.CreateClient();

        var query = string.Join('&', new[] { period is null ? null : $"period={period}", timeZone is null ? null : $"timeZone={Uri.EscapeDataString(timeZone)}" }.OfType<string>());
        var response = await client.GetAsync($"/api/dashboard/sales/series{(query.Length > 0 ? "?" + query : "")}", TestContext.Current.CancellationToken);

        return (response.StatusCode, dispatcher.Asked);
    }
```

The 400 comes from the validator in the real pipeline, so that test keeps the real dispatcher and only lets authorization through; the two others record what the query carried.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project tests/Api.Tests -- --filter-class "*DashboardPeriodBindingTests"`
Expected: build error — `GetSalesSeriesQuery` does not exist.

- [ ] **Step 3: Write the query, validator, handler and route**

Append to `SalesSeries.cs`:

```csharp
/// <param name="TimeZone">An IANA zone (<c>Europe/Madrid</c>); the browser sends its own, UTC when none.</param>
public sealed record GetSalesSeriesQuery(DashboardPeriod Period, string TimeZone) : IQuery<SalesSeries>;

public sealed class GetSalesSeriesQueryValidator : AbstractValidator<GetSalesSeriesQuery>
{
    public GetSalesSeriesQueryValidator()
    {
        RuleFor(query => query.TimeZone)
            .Must(zone => TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _))
            .WithMessage("timeZone is an IANA time zone, such as Europe/Madrid.");
    }
}

public sealed class SalesSeriesHandler(IShopHistory history, TimeProvider clock) : IQueryHandler<GetSalesSeriesQuery, SalesSeries>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<SalesSeries> HandleAsync(GetSalesSeriesQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.dashboard.sales_series");
        activity?.SetTag("dashboard.period", MetricLabels.Of(query.Period));

        var zone = TimeZoneInfo.FindSystemTimeZoneById(query.TimeZone);
        var plan = SeriesPlans.For(query.Period, clock.GetUtcNow(), zone);
        var orders = await history.OrdersPlacedAsync(plan.From, plan.End, cancellationToken);

        activity?.SetTag("dashboard.series.buckets", plan.Current.Count);
        return SalesSeriesFigures.Of(query.Period, query.TimeZone, plan, orders);
    }
}
```

Add at the top of `SalesSeries.cs`: `using System.Diagnostics;`, `using ElGuerre.Tendero.Ordering.Ports;`, `using FluentValidation;`. Before relying on the validator, confirm validators are registered by assembly scan: `grep -rn "AddValidatorsFromAssembly" src/Ordering src/Api src/SharedKernel` must show the Ordering assembly scanned. If it is not, register `GetSalesSeriesQueryValidator` beside the other Ordering validators, in the same file that registers them.

In `Dashboard.cs`, `DashboardEndpoints.AddRoutes`, after the `/sales` route:

```csharp
        // The browser sends its own zone, so "a day" is the day the reader lived; UTC when none.
        dashboard.MapGet("/sales/series",
            async Task<Ok<SalesSeries>> (WireEnum<DashboardPeriod>? period, string? timeZone, IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new GetSalesSeriesQuery(period?.Value ?? DashboardPeriods.Default, timeZone ?? "UTC"), ct)))
            .WithName("GetSalesSeries");
```

- [ ] **Step 4: Run the binding tests, then the whole API suite**

Run: `dotnet test --project tests/Api.Tests -- --filter-class "*DashboardPeriodBindingTests"` → PASS.
Run: `dotnet test --project tests/Api.Tests` → the OpenAPI test fails: the committed document is stale. Expected.

- [ ] **Step 5: Regenerate the contract and the frontend types**

```bash
UPDATE_OPENAPI=1 dotnet test --project tests/Api.Tests -- --filter-class "*OpenApiDocumentTests"
dotnet test --project tests/Api.Tests
cd frontend && npm run generate:api-types && cd ..
node tools/api-map.mjs --schema SalesSeries
```

Expected: `SalesSeries { period: DashboardPeriod; currency; timeZone; step: SeriesStep; current: SalesPoint[]; previous: SalesPoint[] }`, and `SeriesStep: "Hour" | "Day"`. The endpoint-authorization test passes with no edit: the dashboard group requires the shopkeeper.

In `frontend/libs/shared/api/src/lib/ordering-contracts.ts`, after `export type SalesSummary = …`:

```ts
/** Sales by hour or by day, and the period before (R-9): the dashboard's first series. */
export type SalesSeries = Schemas['SalesSeries'];
export type SalesPoint = Schemas['SalesPoint'];
export type SeriesStep = Schemas['SeriesStep'];
```

- [ ] **Step 6: Commit**

```bash
git add src/Ordering/Features/Dashboard tests/Api.Tests/DashboardPeriodBindingTests.cs docs/openapi/tendero.json frontend/libs/shared/api/src/lib
git commit -m "feat(ordering): GET /api/dashboard/sales/series, in the reader's time zone (R-9)" -m "The browser sends its IANA zone, as it sends its culture; UTC when none, and a zone nobody has is a 400 with a sentence rather than a 500. The contract and the frontend types regenerated."
```

---

### Task 3: The chart colours become tokens

**Files:**
- Modify: `design/tokens.css` (the `:root` semantic block after `--focus-ring`, and the `[data-theme='dark']` block)
- Modify: `frontend/libs/shared/i18n/src/lib/design-contrast.spec.ts`
- Modify: `design/unpublished/mockups/proposals.json`

**Interfaces:**
- Produces: CSS custom properties `--chart-data`, `--chart-agent`, `--chart-previous`, defined in both themes.

- [ ] **Step 1: Write the failing contrast test**

In `design-contrast.spec.ts`, after the focus-ring test:

```ts
  /**
   * A chart's marks (R-9) against every surface a widget sits on, at the 3:1 WCAG
   * asks of the graphics that carry meaning. The previous period is drawn in the
   * muted text colour, so it passes on its own rather than by the table beside it.
   */
  const MARKS = ['--chart-data', '--chart-agent', '--chart-previous'];

  it.each(themeNames)('draws chart marks a reader can tell from every surface in the %s theme', (theme) => {
    const pairs = MARKS.flatMap((mark) => SURFACES.map((bg) => [theme, mark, bg] as const));

    expect(failures(INDICATOR_BAR, pairs)).toEqual([]);
  });
```

- [ ] **Step 2: Run it to see it fail**

Run: `cd frontend && npx nx test shared-i18n --skip-nx-cache`
Expected: FAIL — the three tokens do not resolve.

- [ ] **Step 3: Add the tokens**

In `design/tokens.css`, after `--focus-ring: var(--accent);` in `:root`:

```css
  /* A chart's marks (R-9), validated with the dataviz method's checks in both
     themes: a series in blue, what an agent did in clay (DESIGN.md: agents are
     clay), the period before in the muted text colour. Statuses keep theirs and
     are never a series. */
  --chart-data:     #2A69B0;
  --chart-agent:    var(--clay-600);
  --chart-previous: var(--text-muted);
```

In `[data-theme='dark']`, after `--warning`:

```css
  --chart-data:    #5E97CF;
  --chart-agent:   #E0633A;   /* clay's dark step for a mark: the dark accent is too light for one */
```

`--chart-previous` needs no dark line: it follows `--text-muted`, which the dark block already redefines.

- [ ] **Step 4: The proposals leave `proposals.json`**

Remove the entries `#2a69b0`, `#5e97cf` and `#e0633a` from `design/unpublished/mockups/proposals.json`: `tokens.css` owns them now.

Run: `cd frontend && npx nx test shared-i18n --skip-nx-cache`
Expected: PASS. This includes `design-mockups.spec.ts`, which now finds the three hexes in `tokens.css`, and `design-tokens.spec.ts`, which sees no hex in component CSS.

- [ ] **Step 5: Commit**

```bash
git add design/tokens.css design/unpublished/mockups/proposals.json frontend/libs/shared/i18n/src/lib/design-contrast.spec.ts
git commit -m "feat(design): the charts' colours become tokens, held to 3:1 on every surface (R-9)" -m "--chart-data, --chart-agent and --chart-previous in both themes; the proposals leave proposals.json now that tokens.css owns them. The previous period uses the muted text colour rather than the mock-up's lighter grey, so it passes the marks' 3:1 on its own."
```

---

### Task 4: The chart contract and its scale

**Files:**
- Create: `frontend/apps/backoffice/src/app/charts/chart-contract.ts`
- Create: `frontend/apps/backoffice/src/app/charts/scale.ts`, `scale.spec.ts`

**Interfaces:**
- Produces:

```ts
export type SeriesRole = 'data' | 'previous' | 'agent';
export interface BarSeries { readonly key: string; readonly label: string; readonly role: SeriesRole; readonly values: readonly number[] }
export interface BarChartData { readonly categories: readonly string[]; readonly series: readonly BarSeries[] }
export type ValueFormat = (value: number) => string;
export type ChartEngine = 'svg' | 'echarts';
export const CHART_ENGINE: InjectionToken<ChartEngine>;       // default 'svg'
export function roleColourToken(role: SeriesRole): string;      // '--chart-data' | '--chart-previous' | '--chart-agent'
export function niceMax(max: number): number;
export function ticks(max: number, count?: number): number[];
```

- [ ] **Step 1: Write the failing tests**

`scale.spec.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { niceMax, ticks } from './scale';

describe('a chart scale a person reads', () => {
  it('rounds the tallest bar up to a number with one significant step', () => {
    expect(niceMax(930)).toBe(1000);
    expect(niceMax(612)).toBe(1000);
    expect(niceMax(480)).toBe(500);
    expect(niceMax(210)).toBe(250);
    expect(niceMax(12)).toBe(20);
    expect(niceMax(1)).toBe(1);
  });

  it('keeps an empty chart drawable', () => {
    expect(niceMax(0)).toBe(1);
    expect(niceMax(-5)).toBe(1);
  });

  it('marks the axis in equal steps from zero', () => {
    expect(ticks(1000)).toEqual([0, 250, 500, 750, 1000]);
    expect(ticks(20, 2)).toEqual([0, 10, 20]);
  });
});
```

- [ ] **Step 2: Run it to see it fail**

Run: `cd frontend && npx nx test backoffice -- --include=src/app/charts/scale.spec.ts`. Expected: FAIL, module not found. If the Angular unit-test builder rejects `--include`, run `npx nx test backoffice` and read the one failing file.

- [ ] **Step 3: Implement**

`chart-contract.ts`:

```ts
import { InjectionToken } from '@angular/core';

/**
 * What every chart in the backoffice takes (R-9): the data, how to say a value,
 * and a caption. The engine behind it — our own SVG or Apache ECharts — is a
 * detail the contract hides, so the two can be compared on the same chart and
 * one of them removed without touching a widget.
 */
export type SeriesRole = 'data' | 'previous' | 'agent';

export interface BarSeries {
  readonly key: string;
  readonly label: string;
  readonly role: SeriesRole;
  readonly values: readonly number[];
}

export interface BarChartData {
  readonly categories: readonly string[];
  readonly series: readonly BarSeries[];
}

export type ValueFormat = (value: number) => string;

export type ChartEngine = 'svg' | 'echarts';

/** Which engine draws: SVG unless the comparison asks for the other. */
export const CHART_ENGINE = new InjectionToken<ChartEngine>('CHART_ENGINE', { factory: () => 'svg' });

/** A series' colour is its role's token, never a hex: `tokens.css` owns them. */
export function roleColourToken(role: SeriesRole): string {
  return role === 'data' ? '--chart-data' : role === 'agent' ? '--chart-agent' : '--chart-previous';
}
```

`scale.ts`:

```ts
/** The steps a person says a round number in: 1, 2, 2.5, 5, and on by tens. */
const STEPS = [1, 2, 2.5, 5, 10];

/** The smallest round number at or above `max`; 1 for a chart with nothing on it. */
export function niceMax(max: number): number {
  if (!(max > 0)) return 1;
  const magnitude = 10 ** Math.floor(Math.log10(max));
  const step = STEPS.find((candidate) => candidate * magnitude >= max) ?? 10;
  return step * magnitude;
}

/** `count` equal steps from zero to `max`, both ends included. */
export function ticks(max: number, count = 4): number[] {
  return Array.from({ length: count + 1 }, (_, index) => (max * index) / count);
}
```

- [ ] **Step 4: Run it to see it pass**

Same command as Step 2. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/apps/backoffice/src/app/charts
git commit -m "feat(backoffice): the chart contract, and a scale that rounds as a person would (R-9)"
```

---

### Task 5: Engine 1 — our own SVG — and the contract component

**Files:**
- Create: `frontend/apps/backoffice/src/app/charts/svg-bar-chart.{ts,html,css}`, `svg-bar-chart.spec.ts`
- Create: `frontend/apps/backoffice/src/app/charts/bar-chart.{ts,html,css}`, `bar-chart.spec.ts`

**Interfaces:**
- Consumes: Task 4's contract, `niceMax`, `ticks`, `roleColourToken`.
- Produces: `<backoffice-bar-chart [data] [format] [caption] />` (selector `backoffice-bar-chart`, class `BarChart`); `<backoffice-svg-bar-chart [data] [format] />` (class `SvgBarChart`).

- [ ] **Step 1: Write the failing tests**

`svg-bar-chart.spec.ts`:

```ts
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import type { BarChartData } from './chart-contract';
import { SvgBarChart } from './svg-bar-chart';

const WEEK: BarChartData = {
  categories: ['Mon', 'Tue', 'Wed'],
  series: [
    { key: 'current', label: 'This week', role: 'data', values: [520, 930, 0] },
    { key: 'previous', label: 'Week before', role: 'previous', values: [470, 720, 560] },
  ],
};

function draw(data: BarChartData) {
  const fixture = TestBed.createComponent(SvgBarChart);
  fixture.componentRef.setInput('data', data);
  fixture.componentRef.setInput('format', (value: number) => `€${value}`);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('our own SVG bar chart', () => {
  it('draws one group per category and one bar per series, proportional to the round maximum', () => {
    const bars = [...draw(WEEK).querySelectorAll('rect[data-series]')];

    expect(bars).toHaveLength(6);
    // 930 of a round 1000: 93 % of the plot's height.
    const tallest = bars.find((bar) => bar.getAttribute('data-value') === '930');
    expect(tallest?.getAttribute('height')).toBe('93');
  });

  it('colours a bar by its role, through a token', () => {
    const current = draw(WEEK).querySelector('rect[data-series="current"]') as SVGRectElement;

    expect(current.getAttribute('style')).toContain('var(--chart-data)');
  });

  it('names each bar for a pointer and a keyboard', () => {
    const titles = [...draw(WEEK).querySelectorAll('rect[data-series] title')].map((title) => title.textContent);

    expect(titles).toContain('Tue · This week · €930');
  });
});
```

`bar-chart.spec.ts`:

```ts
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { BarChart } from './bar-chart';
import { CHART_ENGINE, type BarChartData } from './chart-contract';

const DATA: BarChartData = {
  categories: ['Mon', 'Tue'],
  series: [
    { key: 'current', label: 'This week', role: 'data', values: [520, 930] },
    { key: 'previous', label: 'Week before', role: 'previous', values: [470, 720] },
  ],
};

function render() {
  TestBed.configureTestingModule({ providers: [{ provide: CHART_ENGINE, useValue: 'svg' }] });
  const fixture = TestBed.createComponent(BarChart);
  fixture.componentRef.setInput('data', DATA);
  fixture.componentRef.setInput('format', (value: number) => `€${value}`);
  fixture.componentRef.setInput('caption', 'Sales by day');
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('the chart contract', () => {
  it('the table carries every figure whatever draws it', () => {
    const cells = [...render().querySelectorAll('table td')].map((cell) => cell.textContent?.trim());

    expect(cells).toEqual(['€520', '€470', '€930', '€720']);
  });

  it('names the chart by its caption and gives each series a legend', () => {
    const element = render();

    expect(element.querySelector('figure figcaption')?.textContent).toContain('Sales by day');
    expect([...element.querySelectorAll('.legend li')].map((item) => item.textContent?.trim())).toEqual(['This week', 'Week before']);
  });

  it('draws with the engine it is given', () => {
    expect(render().querySelector('backoffice-svg-bar-chart')).not.toBeNull();
  });
});
```

- [ ] **Step 2: Run them to see them fail**

Run: `cd frontend && npx nx test backoffice`. Expected: FAIL, modules not found.

- [ ] **Step 3: Implement the SVG engine**

`svg-bar-chart.ts`:

```ts
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { type BarChartData, roleColourToken, type ValueFormat } from './chart-contract';
import { niceMax, ticks } from './scale';

/**
 * Engine 1 (R-9): bars as SVG rectangles in a 100 × 100 box that stretches with
 * its widget — proportional by construction, never pixels — and the labels in
 * HTML under it, so no text is stretched. No dependency.
 */
@Component({
  selector: 'backoffice-svg-bar-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './svg-bar-chart.html',
  styleUrl: './svg-bar-chart.css',
})
export class SvgBarChart {
  readonly data = input.required<BarChartData>();
  readonly format = input.required<ValueFormat>();

  protected readonly max = computed(() => niceMax(Math.max(0, ...this.data().series.flatMap((series) => series.values))));
  protected readonly gridlines = computed(() => ticks(this.max()).slice(1).map((tick) => 100 - (tick / this.max()) * 100));

  protected readonly bars = computed(() => {
    const { categories, series } = this.data();
    const groupWidth = 100 / categories.length;
    const barWidth = (groupWidth * 0.7) / series.length;
    return categories.flatMap((category, index) =>
      series.map((one, position) => {
        const value = one.values[index] ?? 0;
        const height = Math.round((value / this.max()) * 100);
        return {
          key: one.key,
          value,
          x: index * groupWidth + groupWidth * 0.15 + position * barWidth,
          y: 100 - height,
          width: barWidth * 0.92,
          height,
          colour: `var(${roleColourToken(one.role)})`,
          title: `${category} · ${one.label} · ${this.format()(value)}`,
        };
      }),
    );
  });
}
```

`svg-bar-chart.html`:

```html
<svg class="plot" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true" focusable="false">
  @for (y of gridlines(); track y) {
    <line class="gridline" x1="0" x2="100" [attr.y1]="y" [attr.y2]="y" vector-effect="non-scaling-stroke" />
  }
  @for (bar of bars(); track bar.key + bar.x) {
    <rect [attr.data-series]="bar.key" [attr.data-value]="bar.value"
          [attr.x]="bar.x" [attr.y]="bar.y" [attr.width]="bar.width" [attr.height]="bar.height"
          [attr.style]="'fill: ' + bar.colour">
      <title>{{ bar.title }}</title>
    </rect>
  }
</svg>
<ol class="categories" aria-hidden="true">
  @for (category of data().categories; track $index) {
    <li>{{ category }}</li>
  }
</ol>
```

`svg-bar-chart.css`:

```css
:host {
  display: grid;
  gap: var(--space-2);
}

.plot {
  inline-size: 100%;
  block-size: 11rem;
  border-block-end: 1px solid var(--border);
}

.gridline {
  stroke: var(--border);
  stroke-width: 1;
}

.categories {
  display: grid;
  grid-auto-columns: 1fr;
  grid-auto-flow: column;
  margin: 0;
  padding: 0;
  list-style: none;
  color: var(--text-muted);
  font-size: var(--text-2xs);
  text-align: center;
}
```

The pointer gets each bar's `<title>`. The keyboard and the screen reader get the contract's table, which holds the same figures; the drawing is `aria-hidden` so it is not read twice. This follows the spec's rule that a chart's figures are never only in the drawing.

- [ ] **Step 4: Implement the contract component**

`bar-chart.ts`:

```ts
import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { CHART_ENGINE, type BarChartData, roleColourToken, type ValueFormat } from './chart-contract';
import { EchartsBarChart } from './echarts-bar-chart';
import { SvgBarChart } from './svg-bar-chart';

/**
 * A bar chart as the backoffice's widgets use it (R-9): a caption, a legend, the
 * drawing — by whichever engine `CHART_ENGINE` names — and a table of every
 * figure, which is what a screen reader reads and what the drawing only shows.
 * ECharts is loaded only when it is the engine, in its own chunk.
 */
@Component({
  selector: 'backoffice-bar-chart',
  imports: [SvgBarChart, EchartsBarChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './bar-chart.html',
  styleUrl: './bar-chart.css',
})
export class BarChart {
  protected readonly engine = inject(CHART_ENGINE);

  readonly data = input.required<BarChartData>();
  readonly format = input.required<ValueFormat>();
  readonly caption = input.required<string>();

  protected colour(role: BarChartData['series'][number]['role']): string {
    return `var(${roleColourToken(role)})`;
  }
}
```

`bar-chart.html`:

```html
<figure class="chart">
  <figcaption class="sr-only">{{ caption() }}</figcaption>
  <ul class="legend">
    @for (series of data().series; track series.key) {
      <li><span class="swatch" [style.background]="colour(series.role)"></span>{{ series.label }}</li>
    }
  </ul>
  @switch (engine) {
    @case ('echarts') {
      @defer (on viewport) {
        <backoffice-echarts-bar-chart [data]="data()" [format]="format()" />
      } @placeholder {
        <div class="placeholder"></div>
      }
    }
    @default {
      <backoffice-svg-bar-chart [data]="data()" [format]="format()" />
    }
  }
  <table class="sr-only">
    <caption>{{ caption() }}</caption>
    <thead>
      <tr><th scope="col"></th>
        @for (series of data().series; track series.key) { <th scope="col">{{ series.label }}</th> }
      </tr>
    </thead>
    <tbody>
      @for (category of data().categories; track $index; let row = $index) {
        <tr><th scope="row">{{ category }}</th>
          @for (series of data().series; track series.key) { <td>{{ format()(series.values[row] ?? 0) }}</td> }
        </tr>
      }
    </tbody>
  </table>
</figure>
```

`bar-chart.css`:

```css
.chart {
  display: grid;
  gap: var(--space-3);
  margin: 0;
}

.legend {
  display: flex;
  gap: var(--space-4);
  margin: 0;
  padding: 0;
  list-style: none;
  color: var(--text-muted);
  font-size: var(--text-2xs);
}

.legend li {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
}

.swatch {
  inline-size: 0.625rem;
  block-size: 0.625rem;
  border-radius: 2px;
}

.placeholder {
  block-size: 11rem;
}
```

`sr-only` is the global utility the backoffice already uses. Confirm with `grep -n "sr-only" frontend/apps/backoffice/src/styles.css`; if it is missing there, it is in `libs/shared/tokens/src/theme.css`.

`BarChart` imports `EchartsBarChart`, which Task 6 creates. Write Task 6's three files before running Step 5, or temporarily drop the `echarts` case — and restore it in Task 6.

- [ ] **Step 5: Run the tests**

Run: `cd frontend && npx nx test backoffice`. Expected: the five new tests PASS (after Task 6's files exist).

- [ ] **Step 6: Hand mutation, then commit**

In `svg-bar-chart.ts` change `(value / this.max()) * 100` to `(value / this.max()) * 90`; run; expect the proportional test red; restore.

```bash
git add frontend/apps/backoffice/src/app/charts
git commit -m "feat(backoffice): engine 1, our own SVG bars, behind the chart contract (R-9)" -m "Bars in a 100 × 100 box that stretches with its widget — proportional, never pixels — labels in HTML, a title on each bar, and the contract's table of every figure for a screen reader. Hand mutation: bars at 90 % of their share; the proportional test went red."
```

---

### Task 6: Engine 2 — Apache ECharts — in its own chunk

**Files:**
- Create: `frontend/apps/backoffice/src/app/charts/chart-colours.ts`
- Create: `frontend/apps/backoffice/src/app/charts/echarts-bar-options.ts`, `echarts-bar-options.spec.ts`
- Create: `frontend/apps/backoffice/src/app/charts/echarts.directive.ts`
- Create: `frontend/apps/backoffice/src/app/charts/echarts-bar-chart.{ts,html,css}`
- Modify: `frontend/package.json`, `frontend/package-lock.json` (`echarts@6.1.0`)
- Create: `docs/adr/0045-echarts-is-an-experiment-with-a-removal-condition.md`; modify `docs/adr/README.md`

**Interfaces:**
- Consumes: Task 4's contract.
- Produces: `barOptions(data: BarChartData, format: ValueFormat, colours: Readonly<Record<SeriesRole, string>>, reducedMotion: boolean): EChartsCoreOption`; `readChartColours(element: Element): Record<SeriesRole, string>`; `<backoffice-echarts-bar-chart [data] [format] />` (class `EchartsBarChart`).

- [ ] **Step 1: The ADR, then the dependency**

Write `docs/adr/0045-echarts-is-an-experiment-with-a-removal-condition.md`:

```markdown
# 0045 — ECharts is an experiment with a removal condition

**Status**: accepted (the author, 2026-09-29, with R-9's spec)

## Context

R-9 needs charts, and the author chose to compare two engines rather than pick
one: our own SVG, and Apache ECharts. ECharts 6.1.0 is Apache-2.0 — **tier 1**
under the licensing policy, since it ships inside the backoffice — and weighs
555 KB minified / 192 KB gzip with only bars, line, donut, grid, tooltip, aria
and the SVG renderer (measured 2026-09-29), against about 900 KB for the whole
backoffice.

## Decision

1. `echarts@6.1.0` enters `frontend/package.json` for the comparison, pinned.
   `ngx-echarts` does not: the binding is a directive of ours.
2. It is imported by parts from `echarts/core` and only in a deferred block, so
   it is its own chunk and never in the initial bundle.
3. **The removal condition**: when the comparison on *sales by day* (R-9, Plan 1)
   is decided, the engine that loses is deleted — code, token value and, for
   ECharts, the dependency and its lockfile entries — in the same pull request
   that records the numbers in the spec.

## Consequences

- For the length of the comparison, the backoffice has two ways to draw a bar,
  behind one contract (`backoffice-bar-chart`).
- Colours reach ECharts through JavaScript, read from the tokens at run time; the
  tokens test does not see them. If ECharts stays, a test must hold that no
  colour reaches it except through `readChartColours`.
```

Add to `docs/adr/README.md`'s index, after 0044: `| [0045](0045-echarts-is-an-experiment-with-a-removal-condition.md) | ECharts is an experiment with a removal condition | accepted |` (match the index's existing column format).

Then: `cd frontend && npm install echarts@6.1.0 --save-exact`. Check `package.json` shows `"echarts": "6.1.0"` and nothing else was added.

- [ ] **Step 2: Write the failing options test**

`echarts-bar-options.spec.ts`:

```ts
import { describe, expect, it } from 'vitest';
import type { BarChartData } from './chart-contract';
import { barOptions } from './echarts-bar-options';

const DATA: BarChartData = {
  categories: ['Mon', 'Tue'],
  series: [
    { key: 'current', label: 'This week', role: 'data', values: [520, 930] },
    { key: 'previous', label: 'Week before', role: 'previous', values: [470, 720] },
  ],
};
const COLOURS = { data: 'rgb(42, 105, 176)', previous: 'rgb(107, 101, 95)', agent: 'rgb(184, 69, 31)' } as const;

describe('ECharts options for a bar chart', () => {
  it('draws one bar series per series, in its role colour from the tokens', () => {
    const options = barOptions(DATA, String, COLOURS, false) as { series: { name: string; data: number[]; itemStyle: { color: string } }[] };

    expect(options.series.map((one) => [one.name, one.data, one.itemStyle.color])).toEqual([
      ['This week', [520, 930], 'rgb(42, 105, 176)'],
      ['Week before', [470, 720], 'rgb(107, 101, 95)'],
    ]);
  });

  it('keeps the categories, describes itself to a screen reader, and stays still when asked', () => {
    const options = barOptions(DATA, String, COLOURS, true) as { xAxis: { data: string[] }; aria: { enabled: boolean }; animation: boolean };

    expect(options.xAxis.data).toEqual(['Mon', 'Tue']);
    expect(options.aria.enabled).toBe(true);
    expect(options.animation).toBe(false);
  });
});
```

- [ ] **Step 3: Run it to see it fail**

Run: `cd frontend && npx nx test backoffice`. Expected: FAIL, module not found.

- [ ] **Step 4: Implement engine 2**

`chart-colours.ts`:

```ts
import type { SeriesRole } from './chart-contract';
import { roleColourToken } from './chart-contract';

/**
 * The tokens' colours, resolved where the chart sits — the only way a colour
 * reaches ECharts, which draws from JavaScript and cannot read `var(--…)`.
 */
export function readChartColours(element: Element): Record<SeriesRole, string> {
  const style = getComputedStyle(element);
  const read = (role: SeriesRole) => style.getPropertyValue(roleColourToken(role)).trim();
  return { data: read('data'), previous: read('previous'), agent: read('agent') };
}
```

`echarts-bar-options.ts`:

```ts
import type { EChartsCoreOption } from 'echarts/core';
import type { BarChartData, SeriesRole, ValueFormat } from './chart-contract';

/** A bar chart's options, as a pure function of the contract's data: tested without a canvas. */
export function barOptions(
  data: BarChartData,
  format: ValueFormat,
  colours: Readonly<Record<SeriesRole, string>>,
  reducedMotion: boolean,
): EChartsCoreOption {
  return {
    animation: !reducedMotion,
    aria: { enabled: true },
    grid: { left: 8, right: 8, top: 8, bottom: 24, containLabel: true },
    tooltip: { trigger: 'axis', valueFormatter: (value: unknown) => format(Number(value)) },
    xAxis: { type: 'category', data: [...data.categories], axisTick: { show: false } },
    yAxis: { type: 'value', axisLabel: { formatter: (value: number) => format(value) }, splitNumber: 4 },
    series: data.series.map((series) => ({
      name: series.label,
      type: 'bar',
      data: [...series.values],
      barGap: '8%',
      itemStyle: { color: colours[series.role], borderRadius: [4, 4, 0, 0] },
    })),
  };
}
```

`echarts.directive.ts`:

```ts
import { afterNextRender, DestroyRef, Directive, effect, ElementRef, inject, input } from '@angular/core';
import { BarChart } from 'echarts/charts';
import { AriaComponent, GridComponent, TooltipComponent } from 'echarts/components';
import * as echarts from 'echarts/core';
import { SVGRenderer } from 'echarts/renderers';

echarts.use([BarChart, GridComponent, TooltipComponent, AriaComponent, SVGRenderer]);

/**
 * ECharts' binding to Angular, ours (ADR 0045: no `ngx-echarts`): created after
 * the first render, given new options when its signal changes, resized with its
 * element, disposed with it.
 */
@Directive({ selector: '[backofficeEcharts]' })
export class EchartsDirective {
  readonly options = input.required<echarts.EChartsCoreOption>({ alias: 'backofficeEcharts' });

  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly destroyRef = inject(DestroyRef);
  private chart: echarts.ECharts | undefined;

  constructor() {
    afterNextRender(() => {
      this.chart = echarts.init(this.element, null, { renderer: 'svg' });
      this.chart.setOption(this.options(), { notMerge: true });
      const resize = new ResizeObserver(() => this.chart?.resize());
      resize.observe(this.element);
      this.destroyRef.onDestroy(() => {
        resize.disconnect();
        this.chart?.dispose();
      });
    });

    effect(() => {
      const options = this.options();
      this.chart?.setOption(options, { notMerge: true });
    });
  }
}
```

`DestroyRef` is injected as a field because `afterNextRender`'s callback runs outside an injection context.

`echarts-bar-chart.ts`:

```ts
import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, input } from '@angular/core';
import { readChartColours } from './chart-colours';
import type { BarChartData, ValueFormat } from './chart-contract';
import { barOptions } from './echarts-bar-options';
import { EchartsDirective } from './echarts.directive';

/** Engine 2 (R-9): the same bars through Apache ECharts, in its own deferred chunk. */
@Component({
  selector: 'backoffice-echarts-bar-chart',
  imports: [EchartsDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './echarts-bar-chart.html',
  styleUrl: './echarts-bar-chart.css',
})
export class EchartsBarChart {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  readonly data = input.required<BarChartData>();
  readonly format = input.required<ValueFormat>();

  protected readonly options = computed(() =>
    barOptions(this.data(), this.format(), readChartColours(this.host), matchMedia('(prefers-reduced-motion: reduce)').matches),
  );
}
```

`echarts-bar-chart.html`: `<div class="plot" aria-hidden="true" [backofficeEcharts]="options()"></div>`

`echarts-bar-chart.css`:

```css
.plot {
  inline-size: 100%;
  block-size: 12.5rem;
}
```

The drawing is `aria-hidden` here too: the contract's table is read, not ECharts' own description. The same text reaches both engines' readers, so the comparison is fair. The `aria` component stays on, so the SVG ECharts writes carries a description if the drawing is ever exposed.

- [ ] **Step 5: Run the tests and the build**

Run: `cd frontend && npx nx test backoffice` → PASS, including Task 5's tests.
Run: `cd frontend && npx nx build backoffice --configuration=production` → succeeds, with no initial-budget warning. ECharts is in a lazy chunk.

- [ ] **Step 6: Hand mutation, then commit**

In `echarts-bar-options.ts` change `colours[series.role]` to `colours.data`; run; expect the colour test red; restore.

```bash
git add frontend/package.json frontend/package-lock.json frontend/apps/backoffice/src/app/charts docs/adr
git commit -m "feat(backoffice): engine 2, Apache ECharts, deferred, behind the same contract (R-9)" -m "echarts 6.1.0 pinned (Apache-2.0, tier 1) under ADR 0045, an experiment with a removal condition; no ngx-echarts, a directive of ours. Options are a pure function of the contract's data; colours reach it only from the tokens, read at run time. Hand mutation: every series given the data colour; the colour test went red."
```

---

### Task 7: *Sales by day* on the dashboard

**Files:**
- Modify: `frontend/apps/backoffice/src/app/data-access/ordering.service.ts`
- Create: `frontend/apps/backoffice/src/app/features/dashboard/sales-series.ts`, `sales-series.spec.ts`
- Create: `frontend/apps/backoffice/src/app/features/dashboard/sales-by-day-widget.{ts,html,css}`
- Modify: `frontend/apps/backoffice/src/app/features/dashboard/dashboard-page.{ts,html}`
- Modify: `frontend/apps/backoffice/public/i18n/en.json`, `es.json` (under `dashboard`, alphabetical)

**Interfaces:**
- Consumes: `SalesSeries`, `SalesPoint`, `SeriesStep` from `@tendero/shared-api`; `BarChart`; `formatPrice`; `CultureStore`.
- Produces: `OrderingService.salesSeries(period: DashboardPeriod, timeZone: string): Observable<SalesSeries>`; `toBarChart(series: SalesSeries, culture: string, labels: { current: string; previous: string }): BarChartData`; `<backoffice-sales-by-day-widget [period] />`.

- [ ] **Step 1: Write the failing mapping test**

`sales-series.spec.ts`:

```ts
import type { SalesSeries } from '@tendero/shared-api';
import { describe, expect, it } from 'vitest';
import { toBarChart } from './sales-series';

const point = (start: string, revenue: number) => ({ start, revenue, orders: 1, units: 1 });

const WEEK: SalesSeries = {
  period: 'Week',
  currency: 'EUR',
  timeZone: 'Europe/Madrid',
  step: 'Day',
  current: [point('2026-10-12T00:00:00+02:00', 520), point('2026-10-13T00:00:00+02:00', 930)],
  previous: [point('2026-10-05T00:00:00+02:00', 470), point('2026-10-06T00:00:00+02:00', 720)],
};

describe('a sales series as a chart', () => {
  it('labels each day in the reader culture and zone, and keeps the period before as its own series', () => {
    const chart = toBarChart(WEEK, 'en', { current: 'This week', previous: 'Week before' });

    expect(chart.categories).toEqual(['Mon', 'Tue']);
    expect(chart.series.map((series) => [series.role, series.values])).toEqual([
      ['data', [520, 930]],
      ['previous', [470, 720]],
    ]);
  });

  it('labels a day by its hours', () => {
    const day: SalesSeries = { ...WEEK, period: 'Day', step: 'Hour', current: [point('2026-10-14T06:00:00Z', 10)], previous: [point('2026-10-13T06:00:00Z', 5)] };

    expect(toBarChart(day, 'es', { current: 'Hoy', previous: 'Ayer' }).categories).toEqual(['08']);
  });

  it('labels a month by its dates', () => {
    const month: SalesSeries = { ...WEEK, period: 'Month' };

    expect(toBarChart(month, 'es', { current: 'Mes', previous: 'Anterior' }).categories).toEqual(['12 oct', '13 oct']);
  });
});
```

- [ ] **Step 2: Run it to see it fail**

Run: `cd frontend && npx nx test backoffice`. Expected: FAIL, module not found.

- [ ] **Step 3: Implement**

`ordering.service.ts`, beside `sales()`:

```ts
  /** Sales by hour or by day, bucketed in the reader's zone (R-9). */
  salesSeries(period: DashboardPeriod, timeZone: string): Observable<SalesSeries> {
    return this.http.get<SalesSeries>(`${this.baseUrl}/api/dashboard/sales/series`, {
      params: new HttpParams().set('period', period).set('timeZone', timeZone),
    });
  }
```

Import `SalesSeries` from `@tendero/shared-api` with the others.

`sales-series.ts`:

```ts
import type { SalesSeries } from '@tendero/shared-api';
import type { BarChartData } from '../../charts/chart-contract';

/**
 * The series as the chart draws it (R-9): each bucket labelled in the reader's
 * culture and in the zone the server bucketed it in — an hour for a day, a
 * weekday for a week, a date for a month.
 */
export function toBarChart(series: SalesSeries, culture: string, labels: { current: string; previous: string }): BarChartData {
  const format = new Intl.DateTimeFormat(culture, labelOptions(series));
  return {
    categories: series.current.map((point) => format.format(new Date(point.start)).replace('.', '')),
    series: [
      { key: 'current', label: labels.current, role: 'data', values: series.current.map((point) => point.revenue) },
      { key: 'previous', label: labels.previous, role: 'previous', values: series.previous.map((point) => point.revenue) },
    ],
  };
}

function labelOptions(series: SalesSeries): Intl.DateTimeFormatOptions {
  const timeZone = series.timeZone;
  if (series.step === 'Hour') return { timeZone, hour: '2-digit', hourCycle: 'h23' };
  if (series.period === 'Week') return { timeZone, weekday: 'short' };
  return { timeZone, day: 'numeric', month: 'short' };
}
```

`sales-by-day-widget.ts`:

```ts
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { BarChart } from '../../charts/bar-chart';
import { type DashboardPeriod, OrderingService } from '../../data-access/ordering.service';
import { toBarChart } from './sales-series';

/** Sales by hour or by day against the period before (R-9): the first chart of the dashboard. */
@Component({
  selector: 'backoffice-sales-by-day-widget',
  imports: [TranslocoDirective, BarChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sales-by-day-widget.html',
  styleUrl: './sales-by-day-widget.css',
})
export class SalesByDayWidget {
  private readonly ordering = inject(OrderingService);
  private readonly culture = inject(CultureStore);
  private readonly transloco = inject(TranslocoService);
  private readonly timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

  readonly period = input.required<DashboardPeriod>();

  protected readonly series = rxResource({
    params: () => ({ period: this.period() }),
    stream: ({ params }) => this.ordering.salesSeries(params.period, this.timeZone),
  });

  protected readonly chart = computed(() => {
    const series = this.series.value();
    if (!series) return null;
    const labels = {
      current: this.transloco.translate(`dashboard.salesByDay.current.${series.period}`),
      previous: this.transloco.translate(`dashboard.salesByDay.previous.${series.period}`),
    };
    return toBarChart(series, this.culture.active(), labels);
  });

  protected readonly format = computed(() => {
    const currency = this.series.value()?.currency ?? 'EUR';
    const culture = this.culture.active();
    return (value: number) => formatPrice(value, currency, culture);
  });
}
```

`sales-by-day-widget.html`:

```html
<article class="widget panel" *transloco="let t; prefix: 'dashboard.salesByDay'">
  <header class="widget__head">
    <h2 class="widget__title">{{ t('title') }}</h2>
  </header>
  @if (series.isLoading() && !series.hasValue()) {
    <p class="muted" role="status">{{ t('loading') }}</p>
  } @else if (series.error()) {
    <p class="failed" role="alert">{{ t('failed') }}</p>
  } @else if (chart(); as data) {
    <backoffice-bar-chart [data]="data" [format]="format()" [caption]="t('title')" />
  }
</article>
```

`sales-by-day-widget.css`: `:host { display: block; }`. The `widget`, `panel`, `widget__head` and `widget__title` classes come from the existing widgets' shared styles. Check where `.widget` is defined (`grep -rn "\.widget__title" frontend/apps/backoffice/src`) and reuse it; do not redefine it.

In `dashboard-page.ts` add `SalesByDayWidget` to `imports`. In `dashboard-page.html`, after the sales widget:

```html
    <backoffice-sales-by-day-widget class="grid__wide" [period]="period()" />
```

i18n under `dashboard`, keys kept alphabetical, in `en.json`:

```json
"salesByDay": {
  "current": { "Day": "Today", "Month": "This month", "Week": "This week" },
  "failed": "The sales by day could not be read.",
  "loading": "Reading the sales by day…",
  "previous": { "Day": "The day before", "Month": "The month before", "Week": "The week before" },
  "title": "Sales by day"
}
```

and `es.json`:

```json
"salesByDay": {
  "current": { "Day": "Hoy", "Month": "Este mes", "Week": "Esta semana" },
  "failed": "No se han podido leer las ventas por día.",
  "loading": "Leyendo las ventas por día…",
  "previous": { "Day": "El día anterior", "Month": "El mes anterior", "Week": "La semana anterior" },
  "title": "Ventas por día"
}
```

- [ ] **Step 4: Run the tests, the lint and the accessibility ratchet**

Run: `cd frontend && npx nx run-many -t lint test -p backoffice --skip-nx-cache` → PASS.
Build `src/AppHost` (see the memory note: Aspire runs projects from their own bin), start the stack, then run `cd frontend && npx nx e2e backoffice-e2e --skip-nx-cache`. Expected: PASS, and `accessibility-baseline.json` still `[]`. If the dashboard has a new finding, fix it rather than adding it to the baseline. Stop the stack and remove its containers when done.

- [ ] **Step 5: Hand mutation, then commit**

In `sales-series.ts` change `role: 'previous'` to `role: 'data'`; run; expect the mapping test red; restore.

```bash
git add frontend/apps/backoffice/src/app frontend/apps/backoffice/public/i18n
git commit -m "feat(backoffice): Sales by day on the dashboard, in the reader's zone and culture (R-9)" -m "The series as bars against the period before, labelled by hour, weekday or date as the period asks, through the chart contract. Hand mutation: the period before drawn as the current series; the mapping test went red."
```

---

### Task 8: The lab page — both engines on the same data, timed

**Files:**
- Create: `frontend/apps/backoffice/src/app/features/dashboard/lab/chart-engines-page.{ts,html,css}`
- Modify: `frontend/apps/backoffice/src/app/app.routes.ts` (a child route beside `dashboard`)
- Modify: `frontend/apps/backoffice/public/i18n/{en,es}.json` (`dashboard.lab`)

**Interfaces:**
- Consumes: `BarChart`, `CHART_ENGINE`, `OrderingService.salesSeries`, `toBarChart`.
- Produces: route `dashboard/lab/charts` (shopkeeper), which renders both engines and prints their redraw times.

- [ ] **Step 1: Implement the page**

`chart-engines-page.ts`:

```ts
import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { BarChart } from '../../../charts/bar-chart';
import { type BarChartData, CHART_ENGINE } from '../../../charts/chart-contract';

/**
 * One engine per box. The chart is declared in the box's own template, so the box's
 * provider is its injector parent — projected content would keep the page's injector
 * and draw both with the default engine. A one-line template is the documented
 * exception to the three-files rule: the box has no markup or style of its own; if
 * lint rejects it, give each box its own `.html`.
 */
@Component({
  selector: 'backoffice-svg-engine-box',
  imports: [BarChart],
  providers: [{ provide: CHART_ENGINE, useValue: 'svg' }],
  template: '<backoffice-bar-chart [data]="data()" [format]="format()" [caption]="caption()" />',
})
export class SvgEngineBox {
  readonly data = input.required<BarChartData>();
  readonly format = input.required<(value: number) => string>();
  readonly caption = input.required<string>();
}

@Component({
  selector: 'backoffice-echarts-engine-box',
  imports: [BarChart],
  providers: [{ provide: CHART_ENGINE, useValue: 'echarts' }],
  template: '<backoffice-bar-chart [data]="data()" [format]="format()" [caption]="caption()" />',
})
export class EchartsEngineBox {
  readonly data = input.required<BarChartData>();
  readonly format = input.required<(value: number) => string>();
  readonly caption = input.required<string>();
}

const DAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

function randomWeek(): BarChartData {
  const values = () => DAYS.map(() => Math.round(200 + Math.random() * 800));
  return {
    categories: DAYS,
    series: [
      { key: 'current', label: 'This week', role: 'data', values: values() },
      { key: 'previous', label: 'Week before', role: 'previous', values: values() },
    ],
  };
}

/**
 * R-9's comparison, not a screen of the shop: the same week drawn by both engines,
 * and the time each takes to redraw it fifty times. Removed with the engine that
 * loses (ADR 0045).
 */
@Component({
  selector: 'backoffice-chart-engines-page',
  imports: [TranslocoDirective, SvgEngineBox, EchartsEngineBox],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './chart-engines-page.html',
  styleUrl: './chart-engines-page.css',
})
export class ChartEnginesPage {
  protected readonly data = signal<BarChartData>(randomWeek());
  protected readonly format = (value: number) => `€${value}`;
  protected readonly timing = signal<string | null>(null);

  protected async redraw(times = 50): Promise<void> {
    const started = performance.now();
    for (let index = 0; index < times; index++) {
      this.data.set(randomWeek());
      await new Promise((resolve) => requestAnimationFrame(() => resolve(null)));
    }
    this.timing.set(`${times} redraws in ${Math.round(performance.now() - started)} ms (both engines on screen)`);
  }

  protected readonly caption = computed(() => 'Sales by day — the same week, two engines');
}
```

`chart-engines-page.html`:

```html
<section *transloco="let t; prefix: 'dashboard.lab'">
  <h1 class="page-title">{{ t('title') }}</h1>
  <p class="page-hint">{{ t('hint') }}</p>
  <button type="button" class="button" (click)="redraw()">{{ t('redraw') }}</button>
  @if (timing(); as measured) { <p role="status" class="numeric">{{ measured }}</p> }
  <div class="engines">
    <article class="panel engine"><h2>SVG</h2>
      <backoffice-svg-engine-box [data]="data()" [format]="format" [caption]="caption()" />
    </article>
    <article class="panel engine"><h2>ECharts</h2>
      <backoffice-echarts-engine-box [data]="data()" [format]="format" [caption]="caption()" />
    </article>
  </div>
</section>
```

`chart-engines-page.css`:

```css
.engines {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(22rem, 1fr));
  gap: var(--space-4);
  margin-block-start: var(--space-4);
}

.engine {
  padding: var(--space-4);
}
```

Route, in `app.routes.ts` after the `dashboard` route:

```ts
        {
          path: 'dashboard/lab/charts',
          title: 'dashboard.lab.title',
          canActivate: [shopkeeper],
          loadComponent: () =>
            import('./features/dashboard/lab/chart-engines-page').then((m) => m.ChartEnginesPage),
        },
```

i18n `dashboard.lab` (alphabetical). `en`: `{"hint": "R-9's comparison: the same week drawn by both engines. Not a screen of the shop; it leaves with the engine that loses.", "redraw": "Redraw fifty times", "title": "Chart engines"}`. `es`: `{"hint": "La comparación de R-9: la misma semana dibujada por los dos motores. No es una pantalla de la tienda; se va con el motor que pierda.", "redraw": "Redibujar cincuenta veces", "title": "Motores de gráficos"}`.

- [ ] **Step 2: Check it in the browser**

Start the stack, sign in as the shopkeeper, open `/dashboard/lab/charts`. Both charts must draw the same week, and *Redraw fifty times* must print a timing. Take a screenshot of the page into `docs/blog/reports/2026-09-29-r9-dashboard/screens/chart-engines-lab.png`. Stop the stack.

- [ ] **Step 3: Lint, test, commit**

Run: `cd frontend && npx nx run-many -t lint test -p backoffice --skip-nx-cache` → PASS.

```bash
git add frontend/apps/backoffice/src/app frontend/apps/backoffice/public/i18n docs/blog/reports/2026-09-29-r9-dashboard/screens/chart-engines-lab.png
git commit -m "feat(backoffice): a lab page with both chart engines on the same week, timed (R-9)" -m "Not a screen of the shop: R-9's comparison, removed with the engine that loses (ADR 0045)."
```

---

### Task 9: Measure, write it down, and ask the author

**Files:**
- Create: `tools/chart-engines/measure.mjs`
- Modify: `docs/specs/backoffice-dashboard.md` (a new section *As measured — the chart engines*), `docs/blog/notebook.md`

**Interfaces:**
- Consumes: the production build's stats (`npx nx build backoffice --configuration=production --stats-json`).
- Produces: a Markdown table on stdout — bytes per engine (minified and gzip) and lines of our own code per engine.

- [ ] **Step 1: Write the script**

`tools/chart-engines/measure.mjs`:

```js
// R-9's chart comparison (ADR 0045): what each engine adds to the backoffice,
// read from the production build's esbuild metafile, and how much code of ours
// each engine is. Run after:
//   cd frontend && npx nx build backoffice --configuration=production --stats-json
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { gzipSync } from 'node:zlib';
import { join } from 'node:path';

const root = new URL('../../', import.meta.url).pathname.replace(/^\/([A-Z]:)/, '$1');
const dist = join(root, 'frontend/dist/apps/backoffice');
const stats = JSON.parse(readFileSync(join(dist, 'stats.json'), 'utf8'));

// Which output chunk holds which inputs: the engine's bytes are what its inputs contribute.
const groups = {
  'ECharts (library)': (path) => /node_modules[\\/](echarts|zrender|tslib)[\\/]/.test(path),
  'ECharts (our binding)': (path) => /app[\\/]charts[\\/]echarts/.test(path) || /chart-colours/.test(path),
  'SVG (ours)': (path) => /app[\\/]charts[\\/](svg-bar-chart|scale)/.test(path),
};

const bytes = Object.fromEntries(Object.keys(groups).map((name) => [name, 0]));
for (const output of Object.values(stats.outputs ?? {})) {
  for (const [input, { bytesInOutput }] of Object.entries(output.inputs ?? {})) {
    for (const [name, matches] of Object.entries(groups)) if (matches(input)) bytes[name] += bytesInOutput;
  }
}

// Gzip is measured on the whole chunk that holds ECharts, since gzip does not split by input.
const browser = join(dist, 'browser');
const chunks = readdirSync(browser).filter((file) => file.endsWith('.js'));
const echartsChunk = chunks.find((file) => readFileSync(join(browser, file), 'utf8').includes('zrender'));
const gzip = echartsChunk ? gzipSync(readFileSync(join(browser, echartsChunk))).length : 0;

const lines = (pattern) =>
  readdirSync(join(root, 'frontend/apps/backoffice/src/app/charts'))
    .filter((file) => pattern.test(file) && !file.endsWith('.spec.ts'))
    .reduce((total, file) => total + readFileSync(join(root, 'frontend/apps/backoffice/src/app/charts', file), 'utf8').split('\n').length, 0);

const kb = (value) => `${(value / 1024).toFixed(1)} KB`;
console.log('| | minified | lines of ours |');
console.log('|---|---:|---:|');
console.log(`| SVG (ours) | ${kb(bytes['SVG (ours)'])} | ${lines(/^(svg-bar-chart|scale)\./)} |`);
console.log(`| ECharts library | ${kb(bytes['ECharts (library)'])} (its chunk ${kb(gzip)} gzip) | — |`);
console.log(`| ECharts binding (ours) | ${kb(bytes['ECharts (our binding)'])} | ${lines(/^(echarts|chart-colours)/)} |`);
console.log(`\nThe contract (bar-chart, chart-contract) is shared by both and counted in neither.`);
statSync(browser); // fails loudly if the build is missing
```

- [ ] **Step 2: Build with stats and run it**

```bash
cd frontend && npx nx build backoffice --configuration=production --stats-json && cd ..
node tools/chart-engines/measure.mjs
```

Expected: a table where ECharts' library is in the hundreds of KB and ours in the low KB. If `stats.json` is not where the script looks, find it with `find frontend/dist -name stats.json` and fix the path. Keep the output.

- [ ] **Step 3: The redraw times and the accessibility reading**

Start the stack, open `/dashboard/lab/charts`, press *Redraw fifty times* three times and keep the three timings. Then check each engine's box with the keyboard (Tab reaches the table in both, the drawing is skipped) and with the accessibility ratchet's dashboard run. Stop the stack.

- [ ] **Step 4: Write it down**

Add to `docs/specs/backoffice-dashboard.md`, after *The charts: one contract, two engines*:

```markdown
### As measured — the chart engines (2026-09-29)

| | Our own SVG | Apache ECharts 6.1.0 |
|---|---|---|
| Bundle (minified) | <from measure.mjs> | <from measure.mjs> |
| Its chunk (gzip) | — | <from measure.mjs> |
| Lines of our own code | <from measure.mjs> | <from measure.mjs> |
| Fifty redraws, both on screen | <timing> | <timing> |
| Tokens test sees its colours | yes | no — read at run time |
| Screen reader | the contract's table | the contract's table |
| Keyboard | the table | the table |

**The author's decision**: <engine>, because <reason>.
```

Fill the `<…>` from Steps 2 and 3, **except the decision**, which is the author's. Add a notebook entry under today's date with the same numbers and one paragraph on what surprised, if anything did.

- [ ] **Step 5: Commit, then stop for the author**

```bash
git add tools/chart-engines docs/specs/backoffice-dashboard.md docs/blog/notebook.md
git commit -m "docs(r9): the chart engines measured — bytes, lines of ours, redraws, accessibility" -m "Numbers from the production build's metafile and the lab page; the decision is the author's."
git push
```

Then **show the author the table and ask which engine stays.** Do not start Task 10 without the answer.

---

### Task 10: Keep one engine, remove the other

**Files (the loser's):**
- If **SVG stays**: delete `echarts-bar-chart.*`, `echarts.directive.ts`, `echarts-bar-options.*`, `chart-colours.ts`; `npm uninstall echarts` in `frontend/`; remove the `echarts` case from `bar-chart.html` and the import from `bar-chart.ts`; `ChartEngine` becomes `'svg'` only, or the token goes (keep it only if Plan 2 still needs a seam).
- If **ECharts stays**: delete `svg-bar-chart.*` and `scale.*` if nothing else uses them; make `'echarts'` the token's default and the only case; add the test ADR 0045 asks for — no colour reaches ECharts except through `readChartColours` (a spec that fails if `barOptions` output holds a hex that is not from the colours argument).
- Either way: delete the lab page and its route and i18n keys; mark ADR 0045 *closed* with the decision and the date; update the spec's *The charts* section to say which engine the widgets use; update `docs/delivery-plan.md` R-9's row.

- [ ] **Step 1: Remove the loser and the lab** (the files above).
- [ ] **Step 2: Run everything**: `cd frontend && npx nx run-many -t lint test build -p backoffice --skip-nx-cache`, then `cd frontend && npx nx test shared-i18n --skip-nx-cache`; the build's lazy chunk no longer holds the loser.
- [ ] **Step 3: Commit and push**

```bash
git add -A frontend docs
git commit -m "refactor(backoffice): <engine> draws the charts; <the other> leaves with the lab (R-9, ADR 0045)" -m "The comparison's numbers are in the spec and the notebook. <One sentence on why, in the author's words.>"
git push
```

Replace every `<…>` in the message with the actual engine and reason before committing.

---

## After this plan

Plan 2 (the widgets, the Overview and the tabs, full screen, the phone) is written once Task 10 is committed, on the engine that stayed. The spec's *Building it, in order* is the outline; its D-3 to D-6 become Plan 2's tasks.
