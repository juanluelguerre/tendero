# Dashboard Plan 3 (D-7) — the data the dashboard still lacks

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** give R-9's dashboard every figure its spec lists that no endpoint answers today — by category, by channel (web · MCP · UCP), units per order, the order defect and late shipment rates, sell-through, stock coverage, a campaign's same-weekday baseline, customers and agent sales by day, and search's status — each computed on the server by the context that owns it, and drawn by the backoffice's widgets.

**Architecture:** the order learns three facts at the moment they happen and never guesses them later: the **channel** it came through (from `IActorAccessor`, already resolved per request), the **moments it was confirmed and shipped** (in `Confirm` and `Ship`), and each line's **category** (snapshotted from Catalog through `IPurchasableReader`, as the name already is). Ordering's dashboard figures read those; Inventory composes coverage and sell-through from its stock and a read port of its own that Persistence answers from the orders; Search registers one health check per stage behind its ports and a publisher keeps each stage's last change of state. Orders placed before the migration have none of the new facts and every figure counts them apart rather than inventing them.

**Tech Stack:** .NET 11 (preview), EF Core + Npgsql (jsonb complex types), NodaTime, Carter, xUnit + Testcontainers; Angular 22, `@ngrx/signals`, ECharts (ADR 0045), Vitest, Playwright.

**Spec:** `docs/specs/backoffice-dashboard.md` — *The data*, *Definitions*, *The author's answers (2026-09-29)*, *Building it, in order* (D-7).

## Global Constraints

- Every figure is computed on the server by the context that owns it; the browser draws and formats, it never counts (spec, *The data*).
- Bounded contexts share no entities: `Inventory` and `Pricing` reference SharedKernel and nothing else; a crossing is a port in the consuming context's `Ports/`, answered by Persistence (CLAUDE.md invariant 1; `Inventory_knows_only_the_shared_kernel`).
- A day is the shop's day: stored and sent in UTC, counted in `ShopOptions.TimeZone` with NodaTime (ADR 0046).
- Test orders (`OrderMode.Test`) are left out of every figure (spec, *Definitions*).
- A closed set is an enum on the wire (`WireJson.Enums()`, `WireEnum<T>` in a query) (ADR 0043).
- Nothing is anonymous by default: every new endpoint is `RequireAuthorization(TenderoPolicyNames.Shopkeeper)`, and search status is never served through the open `/health` (spec, *Search status*).
- User-facing strings in es AND en (invariant 6); tokens only in CSS (the token test); an Angular component is three files; stores follow ADR 0047.
- OpenAPI is the source of the frontend's types: `UPDATE_OPENAPI=1 dotnet test tests/Api.Tests`, then `npm run generate:api-types` (CLAUDE.md).
- Before any push: `dotnet build -warnaserror`, `dotnet format --no-restore --verify-no-changes --severity error`, `dotnet test`, and `npx nx run-many -t lint test build` (memory: the format step is the one the build does not run).
- Targets, verbatim from the spec: order defect rate **below 1 %**; late shipment rate **below 4 %**; coverage warns **fewer than 14 days**; the pace is **units sold in the last 28 days ÷ 28**; the baseline is the **same weekday over the four weeks before** the campaign opened.

## Review Focus

1. **Orders placed before this plan** — no channel, no category, no confirmed or shipped moment. Every figure must count them apart (*before these figures existed*), never as web, never as *no category* silently merged with real ones, never as on-time. Tests in Tasks 3, 4 and 5.
2. **A period with nothing in it** — zero orders, zero shipped, a SKU never sold: rates are `null` (*no data*), not `0 %` and not a division by zero; a SKU with no sales in 28 days is *no sales*, not infinite coverage. Tests in Tasks 3, 5 and 6.
3. **A campaign with fewer than four weeks of history before it** — no baseline, said so; and a campaign day still in the future is not a day with zero sales. Test in Task 6.
4. **The shop's day at a DST change** (Europe/Madrid, last Sunday of October) — a day of 25 hours is still one day in the baseline's weekday matching and in the series. Test in Task 6.
5. **A search stage that flaps** — down, up, down within a minute: the status says the latest change and *since when*, not the first. Test in Task 7.

---

## File structure

| Where | What | Task |
|---|---|---|
| `src/Ordering/Domain/Order.cs` | `Order.Channel`, `ConfirmedAt`, `ShippedAt`; `OrderLine.CategoryCode` | 1 |
| `src/Ordering/Domain/OrderChannel.cs` (new) | `enum OrderChannel { Web, Mcp, Ucp }` and `OrderChannels.From(string surface)` | 1 |
| `src/Ordering/Ports/ICartRepository.cs`, `Domain/Cart.cs` | `PurchasableVariant.CategoryCode`, `CartLine.CategoryCode` | 1 |
| `src/Ordering/OrderPlacement.cs` | passes the channel and the categories to `Order.Place` | 1 |
| `src/Persistence/EfCartRepository.cs`, `Configurations/OrderConfiguration.cs`, `Configurations/CartConfiguration.cs`, `Migrations/<ts>_OrderChannelMomentsAndCategory.cs` | the columns and the snapshot | 2 |
| `src/Ordering/Features/Dashboard/Dashboard.cs` | `SalesFigures.UnitsPerOrder`; `OperationsSummary` gains the defect and late rates | 3 |
| `src/Ordering/Features/Dashboard/SalesMix.cs` (new) | `GET /api/dashboard/mix`: by channel and by category | 4 |
| `src/Ordering/Features/Dashboard/SalesSeries.cs` | `SalesPoint` gains agent orders/revenue and new/returning customers | 4 |
| `src/Inventory/Ports/ISalesPace.cs` (new), `src/Inventory/Features/StockHealth/StockHealth.cs` (new), `src/Persistence/EfSalesPace.cs` (new) | coverage and sell-through, `GET /api/dashboard/stock` | 5 |
| `src/Ordering/Features/Dashboard/CampaignUplift.cs` (new) | `GET /api/dashboard/uplift?from=&to=&category=` | 6 |
| `src/Search/Health/*` (new), `src/Api/Program.cs`, `src/Api/SearchStatusEndpoints.cs` (new) | a health check per stage, the memory of their changes, `GET /api/dashboard/search-status` | 7 |
| `docs/openapi/tendero.json`, `frontend/libs/shared/api/src/generated/schema.ts` | regenerated | 8 |
| `frontend/apps/backoffice/src/app/features/dashboard/**` | the sources, the widgets extended and the four new ones, the layouts, i18n | 9, 10 |
| `docs/adr/0048-*.md`, spec, delivery plan, notebook, material map | what this decided | 11 |

---

### Task 1: The order records its channel, its moments and its lines' categories

**Files:**
- Create: `src/Ordering/Domain/OrderChannel.cs`
- Modify: `src/Ordering/Domain/Order.cs` (`Place`, `Confirm`, `Ship`, `OrderLine`), `src/Ordering/Domain/Cart.cs` (`CartLine`), `src/Ordering/Ports/ICartRepository.cs` (`PurchasableVariant`), `src/Ordering/OrderPlacement.cs`, `tests/OrderBuilder.cs`
- Test: `tests/Ordering.Tests/OrderMomentsTests.cs` (new), `tests/Ordering.Tests/OrderPlacementChannelTests.cs` (new)

**Interfaces:**
- Produces: `enum OrderChannel { Web, Mcp, Ucp }`; `OrderChannels.From(string surface) : OrderChannel?` (`internal` surface → null); `Order.Channel : OrderChannel?`; `Order.ConfirmedAt : DateTimeOffset?`; `Order.ShippedAt : DateTimeOffset?`; `OrderLine.CategoryCode : string?` (last positional parameter, default null); `CartLine.CategoryCode : string?`; `PurchasableVariant.CategoryCode : string?`; `Order.Place(..., AgentId? placedBy = null, OrderChannel? channel = null)`; `OrderBuilder.Through(OrderChannel)`, `OrderBuilder.ConfirmedAt(DateTimeOffset)`, `OrderBuilder.ShippedAt(DateTimeOffset)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Ordering.Tests/OrderMomentsTests.cs
public sealed class OrderMomentsTests
{
    [Fact]
    public void An_order_remembers_when_it_was_confirmed_and_when_it_shipped()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero));
        var order = OrderBuilder.Default().At(clock).InStatus(OrderStatus.PaymentAuthorized);

        order.Confirm(clock);
        clock.Advance(TimeSpan.FromHours(30));
        order.Ship(clock);

        Assert.Equal((clock.GetUtcNow().AddHours(-30), clock.GetUtcNow()), (order.ConfirmedAt, order.ShippedAt));
    }

    [Fact]
    public void Each_line_keeps_the_category_it_was_sold_in()
    {
        var line = OrderBuilder.Line(10m, "EUR") with { CategoryCode = "COOKWARE" };

        Assert.Equal("COOKWARE", OrderBuilder.Default().WithLines(line).Build().Lines[0].CategoryCode);
    }
}

// tests/Ordering.Tests/OrderPlacementChannelTests.cs — through OrderPlacement with the doubles
// OrderingDoubles already has; the actor accessor answers the surface.
[Theory]
[InlineData(Surfaces.Web, OrderChannel.Web)]
[InlineData(Surfaces.Mcp, OrderChannel.Mcp)]
[InlineData(Surfaces.Ucp, OrderChannel.Ucp)]
public async Task An_order_records_the_channel_it_came_through(string surface, OrderChannel expected) { /* place a one-line cart with the actor on `surface`; assert order.Channel == expected and order.Lines[0].CategoryCode == the cart line's */ }
```

- [ ] **Step 2: Run them — they fail to compile** (`Order` has no `ConfirmedAt`, `OrderLine` no `CategoryCode`). Run: `dotnet test tests/Ordering.Tests --filter "FullyQualifiedName~OrderMoments|FullyQualifiedName~OrderPlacementChannel"`.

- [ ] **Step 3: Implement.**
  - `OrderChannel.cs`: the enum and `From(surface)` mapping `Surfaces.Web/Mcp/Ucp`, anything else null.
  - `Order`: `public OrderChannel? Channel { get; private set; }`, `ConfirmedAt`, `ShippedAt`; `Place` takes `channel` and sets it; `Confirm` sets `ConfirmedAt = clock.GetUtcNow()` before `TransitionTo`; `Ship` sets `ShippedAt`. The transition table is untouched (invariant 5).
  - `OrderLine`: append `string? CategoryCode = null`. `CartLine` the same; `Cart.Add` copies it from `PurchasableVariant`; `OrderPlacement` copies it line by line into `OrderLine`.
  - `OrderPlacement`: inject `IActorAccessor actor`; pass `OrderChannels.From(actor.Current.Surface)` to `Order.Place`.
  - `OrderBuilder`: `Through`, `ConfirmedAt`, `ShippedAt` (set through reflection-free internal setters exposed by `Order.Restore…`? No — the builder drives `Confirm`/`Ship` with a clock at the given instants).

- [ ] **Step 4: Run** `dotnet test tests/Ordering.Tests` — all green.
- [ ] **Step 5: Hand mutation** — drop `ShippedAt = …` in `Ship`: `An_order_remembers…` fails; restore.
- [ ] **Step 6: Commit** `feat(ordering): an order records its channel, when it was confirmed and shipped, and each line's category` (the mutation named in the message).

### Task 2: Persistence — the columns, the snapshot and the migration

**Files:**
- Modify: `src/Persistence/EfCartRepository.cs` (`EfPurchasableReader` reads `product.Category`), `src/Persistence/Configurations/OrderConfiguration.cs` (`channel` string(10) nullable with an index `(channel, created_at)`; `confirmed_at`, `shipped_at` `timestamptz` nullable; `lines[].category_code` inside the jsonb), `src/Persistence/Configurations/CartConfiguration.cs` (the cart line's jsonb gains `category_code`)
- Create: `src/Persistence/Migrations/<timestamp>_OrderChannelMomentsAndCategory.cs` via `dotnet tool run dotnet-ef migrations add OrderChannelMomentsAndCategory --project src/Persistence`
- Test: `tests/Integration.Tests/OrderSnapshotTests.cs` (new, Testcontainers)

- [ ] **Step 1: Write the failing integration test** — place an order through `Shop.OpenAsync` as an agent on the MCP surface, read it back through `IOrderRepository`: `Channel == Mcp`, `Lines[0].CategoryCode == the product's Category`, and after `Confirm` + `Ship` both moments survive a round trip.
- [ ] **Step 2: Run** `dotnet test tests/Integration.Tests --filter OrderSnapshot` — fails (columns missing).
- [ ] **Step 3: Implement** the configuration and the reader, then add the migration. Check the migration only adds nullable columns — no data rewritten: an order placed before has `null` everywhere, which is the honest answer (Review Focus 1).
- [ ] **Step 4: Run** the integration test and `dotnet test tests/Persistence.Tests` — green. `UPDATE_DOMAIN_MODEL=1 dotnet test --project tests/Architecture.Tests` to regenerate `docs/domain-model.md`.
- [ ] **Step 5: Hand mutation** — the reader without `CategoryCode`: the test fails; restore.
- [ ] **Step 6: Commit** `feat(persistence): the order's channel, moments and line categories are stored`.

### Task 3: Units per order, the order defect rate and the late shipment rate

**Files:**
- Modify: `src/Ordering/Features/Dashboard/Dashboard.cs` (`SalesFigures`, `OperationsSummary`, `DashboardFigures`, the operations handler), `src/Ordering/Ports/IShopHistory.cs` (+ `ReturnsOpenedAsync(from, to)` if the current read does not cover returns by creation), `src/Persistence/EfOrderRepository.cs`
- Test: `tests/Ordering.Tests/DashboardFiguresTests.cs`

**Interfaces:**
- Produces: `SalesFigures(decimal Revenue, int Orders, int Units, decimal AverageOrderValue, decimal UnitsPerOrder)`; `OperationsSummary` gains `decimal? DefectRate, decimal DefectTarget, RateStanding? DefectStanding, int DefectsCounted, decimal? LateShipmentRate, decimal LateShipmentTarget, RateStanding? LateShipmentStanding, int ShippedCounted, int ShippedBeforeMoments` (reuse the standing enum cancellation already uses).
- Definitions (spec): a **defect** is an order of the period with a return line for `Damaged` or `NotAsDescribed`, or stopped with `OrderStop.ShopCancelled`; rate = defects ÷ live orders of the period; target 1 %. **Late** = shipped in the period with `ShippedAt − ConfirmedAt > LateAfter` (48 h, the rule `LateToShip` already uses); rate = late ÷ shipped in the period with both moments; target 4 %; orders shipped without moments are counted in `ShippedBeforeMoments`, never as on time.

- [ ] **Step 1: Failing tests** in `DashboardFiguresTests`:
  - `Units_per_order_is_units_over_orders` (3 orders, 7 units → 2.33).
  - `A_damaged_return_or_a_shop_cancellation_is_a_defect_and_a_changed_mind_is_not`.
  - `A_period_with_no_orders_has_no_defect_rate_rather_than_zero` (null).
  - `An_order_shipped_more_than_48_hours_after_it_was_confirmed_is_late`.
  - `Orders_shipped_before_the_moments_were_recorded_are_counted_apart` (Review Focus 1).
- [ ] **Step 2: Run** — fail on the missing members.
- [ ] **Step 3: Implement** in `DashboardFigures` (pure), wiring the handler to pass returns of the period's orders.
- [ ] **Step 4: Run** `dotnet test tests/Ordering.Tests` — green.
- [ ] **Step 5: Hand mutation** — `>` to `>=` on 48 h, or `ChangedMind` counted as a defect: a test fails; restore.
- [ ] **Step 6: Commit** `feat(ordering): units per order, and the defect and late shipment rates against their targets`.

### Task 4: The sales mix (channel and category) and the richer daily series

**Files:**
- Create: `src/Ordering/Features/Dashboard/SalesMix.cs` — `GetSalesMixQuery(DashboardPeriod Period)`, `SalesMix(DashboardPeriod Period, string Currency, IReadOnlyList<ChannelSales> ByChannel, IReadOnlyList<CategorySales> ByCategory, ChannelSales Unrecorded)`, `ChannelSales(OrderChannel? Channel, int Orders, decimal Revenue)`, `CategorySales(string? CategoryCode, int Units, decimal Revenue)`; route `GET /api/dashboard/mix` in `DashboardEndpoints`
- Modify: `src/Ordering/Features/Dashboard/SalesSeries.cs` — `SalesPoint(DateTimeOffset Start, decimal Revenue, int Orders, int Units, int AgentOrders, decimal AgentRevenue, int NewCustomers, int ReturningCustomers)`
- Test: `tests/Ordering.Tests/SalesMixTests.cs` (new), `tests/Ordering.Tests/SalesSeriesTests.cs`, `tests/Api.Tests/EndpointAuthorizationTests.cs` (the new route is the shopkeeper's), `tests/Api.Tests/DashboardPeriodBindingTests.cs` (`/mix` binds `?period=`)

- [ ] **Step 1: Failing tests:**
  - `Sales_split_by_the_channel_they_came_through` (web 2, MCP 1, UCP 1 → three rows).
  - `Orders_before_the_channel_was_recorded_are_counted_apart` (Review Focus 1: `Unrecorded`, not web).
  - `Sales_by_category_rank_by_revenue_and_lines_without_one_are_their_own_row` (a line with `CategoryCode == null` → the row with a null code, last).
  - `A_series_point_counts_what_agents_bought_and_who_bought_for_the_first_time` (a customer first seen that day is new; one who ordered before the series began is returning).
- [ ] **Step 2: Run** — fail.
- [ ] **Step 3: Implement** `SalesMixFigures.Of(period, orders)` (pure) and its handler on `IShopHistory.OrdersPlacedAsync`; extend `SalesSeriesFigures.Points` — new vs returning needs the buyers who ordered before `plan.From` (`CustomersWhoOrderedBeforeAsync`, already on the port), then a buyer is *new* on the first point they appear in and *returning* otherwise.
- [ ] **Step 4: Run** `dotnet test tests/Ordering.Tests tests/Api.Tests` — green (the OpenAPI test will ask for regeneration in Task 8; run it with `UPDATE_OPENAPI=1` there, not here).
- [ ] **Step 5: Hand mutation** — the unrecorded orders folded into web: a test fails; restore.
- [ ] **Step 6: Commit** `feat(ordering): the sales mix by channel and category, and agents and customers by day`.

### Task 5: Stock coverage and sell-through (Inventory, with the pace through a port)

**Files:**
- Create: `src/Inventory/Ports/ISalesPace.cs` — `Task<IReadOnlyDictionary<string, SkuPace>> UnitsSoldAsync(IReadOnlyCollection<string> skus, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)`; `SkuPace(int Units, DateTimeOffset? FirstSoldAt)`; `src/Inventory/Features/StockHealth/StockHealth.cs` — `GetStockHealthQuery(DashboardPeriod Period)`, `StockHealth(int ToRestock, int SoldOut, int UnderTwoWeeks, int NoSales, IReadOnlyList<WarehouseCoverage> Warehouses, IReadOnlyList<SkuCoverage> Closest, IReadOnlyList<SellThrough> SellThrough)`, `WarehouseCoverage(string WarehouseCode, decimal? MedianDays)`, `SkuCoverage(string Sku, int Available, decimal PerDay, decimal Days)`, `SellThrough(string Sku, int Sold, int Left, decimal Rate)`; `StockCoverage` (pure) with the spec's formula; route `GET /api/dashboard/stock` (Shopkeeper) in the slice's own `ICarterModule`; `src/Persistence/EfSalesPace.cs` (internal, reads live sale lines from `ordering.orders`) registered `AddScoped<ISalesPace, EfSalesPace>()`
- Test: `tests/Inventory.Tests/StockCoverageTests.cs` (new), `tests/Inventory.Tests/StockHealthHandlerTests.cs` (new, fakes in `InventoryFakes.cs`), `tests/Integration.Tests/SalesPaceTests.cs` (new), `tests/Architecture.Tests` (unchanged: must still pass — Inventory references SharedKernel only)

Definitions (spec, verbatim): coverage (days) = units available ÷ (units sold in the last 28 days ÷ 28); a product on sale for fewer than 28 days divides by the days it has been on sale; no sales in 28 days → *no sales*, not infinite; sold out → 0; the widget shows the count under 14 days, the median per warehouse and the closest to running out. Sell-through = sold ÷ (sold + left), by SKU, for the period.

- [ ] **Step 1: Failing tests** in `StockCoverageTests`:
  - `A_backpack_with_18_available_and_42_sold_in_28_days_covers_12_days` (the spec's example).
  - `A_sku_that_sold_nothing_in_28_days_has_no_sales_rather_than_an_endless_coverage` (Review Focus 2).
  - `A_sku_on_sale_for_ten_days_divides_by_ten`.
  - `Sell_through_is_sold_over_sold_and_left`.
- [ ] **Step 2: Run** `dotnet test tests/Inventory.Tests` — fail.
- [ ] **Step 3: Implement** `StockCoverage.Of(...)`, the handler composing `IStockRepository.AllAsync` with `ISalesPace`, and `EfSalesPace` (sale lines of live orders in `IsSale` statuses, grouped by SKU, with the first sale's instant).
- [ ] **Step 4: Run** the unit, integration and architecture tests — green.
- [ ] **Step 5: Hand mutation** — `÷ 28` always, ignoring the days on sale: the ten-day test fails; restore.
- [ ] **Step 6: Commit** `feat(inventory): stock coverage and sell-through, with the pace read through a port`.

### Task 6: A campaign's baseline — the same weekday over the four weeks before

**Files:**
- Create: `src/Ordering/Features/Dashboard/CampaignUplift.cs` — `GetCampaignUpliftQuery(DateTimeOffset From, DateTimeOffset To, string? Category)`, `CampaignUplift(string Currency, bool HasBaseline, decimal Sold, decimal? Baseline, decimal? Uplift, IReadOnlyList<UpliftDay> Days)`, `UpliftDay(LocalDate-as-DateTimeOffset Start, decimal Sold, decimal? Baseline, bool Future)`; `CampaignBaseline` (pure, NodaTime); route `GET /api/dashboard/uplift?from=&to=&category=` (Shopkeeper). *Dates in, figures out*: the backoffice reads the campaign's window from Pricing's calendar and asks this with it, so Ordering never reads Pricing.
- Test: `tests/Ordering.Tests/CampaignBaselineTests.cs` (new)

- [ ] **Step 1: Failing tests:**
  - `A_saturday_that_sold_330_against_240_270_250_and_280_has_a_baseline_of_260_and_an_uplift_of_70` (the spec's example, Europe/Madrid).
  - `With_fewer_than_four_weeks_of_history_there_is_no_baseline` (Review Focus 3).
  - `A_day_of_the_campaign_still_to_come_is_future_not_zero` (Review Focus 3).
  - `A_campaign_across_the_october_clock_change_keeps_its_weekdays` (Review Focus 4: 2026-10-25 is a 25-hour Sunday).
  - `A_category_campaign_counts_only_its_category` (lines by `CategoryCode`, and its ancestors are the caller's to pass — the code given is matched exactly).
- [ ] **Step 2: Run** — fail.
- [ ] **Step 3: Implement** with the shop's zone: for each day of the window, the four same-weekday days before `From`'s date, sales summed per shop day; history starts at the first live order.
- [ ] **Step 4: Run** `dotnet test tests/Ordering.Tests` — green.
- [ ] **Step 5: Hand mutation** — average over three weeks instead of four: the 260 test fails; restore.
- [ ] **Step 6: Commit** `feat(ordering): a campaign's baseline and uplift, the same weekday over the four weeks before`.

### Task 7: Search's status — a health check per stage, and when it last changed

**Files:**
- Create: `src/Search/Health/SearchStage.cs` (`enum SearchStage { Words, Meaning, Reranking }`, `enum StageState { Up, Down, Off }`), `src/Search/Health/SearchStageHealthChecks.cs` (three `IHealthCheck`s: words through a new `ILexicalIndexProbe.PingAsync` on the Elasticsearch adapter; meaning through `IVectorLayer.CheckAsync`; reranking through `IReranker.IsConfigured` plus a one-pair `ScoreAsync`), `src/Search/Health/SearchStatusMemory.cs` (singleton: the last state of each stage and since when; `Record(stage, state, at)` keeps the instant only when the state changes), `src/Search/Health/SearchStatusPublisher.cs` (`IHealthCheckPublisher` filtered to the `search` tag), `src/Api/SearchStatusEndpoints.cs` (`GET /api/dashboard/search-status` → `SearchStatus(IReadOnlyList<StageStatus> Stages)`, `StageStatus(SearchStage Stage, StageState State, DateTimeOffset Since)`, Shopkeeper)
- Modify: `src/Search/.../SearchServiceCollectionExtensions.cs` (register the checks with tag `search`, not `live`), `src/Api/Program.cs`
- Test: `tests/Search.Tests/SearchStatusMemoryTests.cs` (new), `tests/Api.Tests/EndpointAuthorizationTests.cs`, `tests/Api.Tests/HealthEndpointTests.cs` (the open `/health` does not name the stages)

- [ ] **Step 1: Failing tests:**
  - `A_stage_that_never_changed_is_up_since_the_first_check`.
  - `A_stage_that_went_down_says_since_when` .
  - `A_stage_that_flaps_says_its_latest_change` (Review Focus 5).
  - `Meaning_switched_off_is_off_not_down` (`VectorLayerStatus.IsConfigured == false`).
  - `The_status_is_the_shopkeepers_and_the_open_health_endpoint_does_not_list_stages`.
- [ ] **Step 2: Run** — fail.
- [ ] **Step 3: Implement.** The publisher runs on the health-check service's schedule (default 30 s; set `Delay` 5 s, `Period` 30 s in `Program.cs`). The checks are registered only where Search is (the API), and the open `/health` keeps mapping only what it maps today.
- [ ] **Step 4: Run** `dotnet test tests/Search.Tests tests/Api.Tests` — green.
- [ ] **Step 5: Hand mutation** — `Record` keeps every instant: the flapping and since-when tests fail; restore.
- [ ] **Step 6: Commit** `feat(search): each stage's health through its ports, and the shopkeeper's status of them`.

### Task 8: The contract — OpenAPI and the frontend's types

- [ ] **Step 1:** `UPDATE_OPENAPI=1 dotnet test tests/Api.Tests` then `dotnet test tests/Api.Tests` green; the closed sets (`OrderChannel`, `SearchStage`, `StageState`) are declared as enums (ADR 0043).
- [ ] **Step 2:** from `frontend/`: `npm run generate:api-types`; `libs/shared/api` exports the new types (`SalesMix`, `StockHealth`, `CampaignUplift`, `SearchStatus` and their rows).
- [ ] **Step 3:** `node tools/api-map.mjs dashboard` lists `/mix`, `/stock`, `/uplift`, `/search-status`.
- [ ] **Step 4: Commit** `docs(openapi): the dashboard's new reads, and the types generated from them`.

### Task 9: The sources and the widgets that already exist

**Files:** `frontend/apps/backoffice/src/app/data-access/ordering.service.ts` (`mix`, `uplift`), `data-access/inventory.service.ts` (`stockHealth`), a `data-access/search.service.ts` method `status()`; `features/dashboard/sources/dashboard-sources.ts` (the same cache per key and instant); the widgets: **key figures** (units per order, its change), **health** (three rates against their targets, *no data* when null), **by channel** (people · MCP · UCP, and *before channels* apart), **stock** (under 14 days, median per warehouse, the closest five), **customers** (new and returning by day, ECharts stacked bars), **agent sales** (by day); i18n es/en in `libs/shared/i18n`.

- [ ] **Step 1:** each widget store's spec first (ADR 0047): the store reads the new fields and derives what the component shows — e.g. `health.store.spec.ts`: *a rate of null reads as no data, not 0 %*; `by-channel.store.spec.ts`: *orders before channels are their own slice, labelled apart*.
- [ ] **Step 2: Run** `npx nx test backoffice` — red.
- [ ] **Step 3: Implement** the stores and templates; the size contract (S figure · M chart · L chart and table) unchanged.
- [ ] **Step 4: Run** `npx nx run-many -t lint test -p backoffice shared-i18n` — green.
- [ ] **Step 5: Hand mutation** in one store per widget family; restore.
- [ ] **Step 6: Commit** `feat(backoffice): the dashboard's widgets read what the shop now knows`.

### Task 10: The four new widgets and where they sit

**Files:** `features/dashboard/widgets/{by-category,sell-through,campaigns,search-status}-widget/` (three files + store + store spec each), `widgets/dashboard-widgets.ts` (keys), `dashboard-widget-registry.ts`, `tabs/dashboard-layouts.ts` (Sales: By category, Sell-through; Overview and Growth: Campaigns; Operations: Search status), i18n.

- [ ] **Step 1:** store specs first: *by category ranks by revenue and keeps the uncategorised apart*; *sell-through shows what moves and what sits*; *campaigns: a running campaign with fewer than four weeks of history says it has no baseline*; *search status says the stage and since when, as what the shopper gets*.
- [ ] **Step 2: Run** — red.
- [ ] **Step 3: Implement.** The campaigns widget reads Pricing's calendar for the running and last-ended campaigns, then `/uplift` with each window (and the category for a category campaign).
- [ ] **Step 4: Run** unit, lint, build; then the browser suite against the stack: `dashboard.spec.ts` gains *every tab draws its new widgets at S, M and L*; the accessibility ratchet on every tab in both themes.
- [ ] **Step 5: Hand mutation**; restore.
- [ ] **Step 6: Commit** `feat(backoffice): by category, sell-through, campaigns against their baseline, and search status`.

### Task 11: What this decided, written down

- [ ] ADR 0048 *An order records the facts the dashboard needs when they happen* (channel, confirmed and shipped moments, the line's category; orders before are counted apart, never back-filled).
- [ ] The spec's status line and *The data* table (every *new* now *yes*); `docs/delivery-plan.md` R-9 line (Plan 3 built) and *Where I left off*; `docs/domain-model.md` regenerated (Task 2).
- [ ] Notebook entry with the numbers (tests before/after, migration, the widgets' counts) and the material map's pointer; the ledger.
- [ ] Commit `docs: R-9 Plan 3 — the data the dashboard lacked`.

---

## Self-review

- **Spec coverage:** category (1, 2, 4, 10) · surface (1, 2, 4, 9) · units per order (3, 9) · defect and late rates (1, 3, 9) · sell-through (5, 10) · coverage via a read port (5, 9) · baseline (6, 10) · customers and agent sales series (4, 9) · search status (7, 10). *Top queries* stays deferred (spec). The events and the after captures are Plan 4 (D-8).
- **Placeholders:** Task 1's placement test and Task 2's integration test are described rather than written in full because they drive existing doubles (`OrderingDoubles`, `Shop.OpenAsync`) whose exact setup the executor reads on the spot; their assertions are fixed here.
- **Type consistency:** `OrderChannel?` everywhere (null = before channels); `CategoryCode` on `PurchasableVariant`, `CartLine`, `OrderLine`; `DashboardPeriod` for every period.
