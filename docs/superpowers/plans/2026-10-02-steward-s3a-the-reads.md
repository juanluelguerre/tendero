# Steward S-3a · The reads — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every figure Steward's six read tools will need exists as an ordinary query in the context that owns it, shopkeeper-only, capped at 50 rows with the total, carrying no customer's personal data — so S-3b's tools are thin wrappers, and the backoffice gains the same reads.

**Architecture:** Three tools already have their query (`ListProductsQuery`, `GetSalesSummaryQuery` + `GetSalesMixQuery`, and — for the shape of a restock — `GetStockHealthQuery`); the rest are new slices in their own context. Each new figure is computed by a pure function beside its handler (the dashboard's `DashboardFigures` pattern), so the tests read rules, not mocks; handlers only fetch and call it. A shared cap, `ReadLimits.MaxRows = 50`, lives in the SharedKernel because every context's read honours it.

**Tech Stack:** .NET 11, C# 14, custom CQRS, Carter, FluentValidation, EF Core + Npgsql (one raw SQL id-subquery, as `Matching` already does), xUnit v3.

**Spec:** `docs/specs/steward.md` — *What Steward reads* (the table and the common rules), *When no model answers* (the non-AI filter). The author's decisions of 2026-10-02: S-3 split into S-3a (these reads) and S-3b (the agent); "missing a required attribute" is out of v1 and in the backlog (`docs/delivery-plan.md`, *Required attributes per category*).

## Global Constraints

- "Tools are thin wrappers over `IQuery<T>` handlers the contexts own … No free SQL, no traces, no business logic in `Assist`." The logic is here, in the contexts.
- "At most 50 rows per call, always with the total (*50 of 312*)."
- "No customer's personal data: no names, addresses or emails — aggregates and products only." No `OrderId`, no `CustomerId`, no return comment in any new result.
- Every new endpoint is shopkeeper-only (`TenderoPolicyNames.Shopkeeper`); `EndpointAuthorizationTests` lists it.
- A period is `DashboardPeriod` (`Day | Week | Month`), bound as `WireEnum<DashboardPeriod>?` on HTTP (ADR 0043), default `DashboardPeriods.Default`; a window is `DashboardWindow.Ending(now, period)`.
- A culture is validated as one `CultureNegotiation.Supports`; an endpoint resolves it with `CultureNegotiation.Resolve`.
- Every enum on the wire is declared as one (ADR 0043); after each response change: `UPDATE_OPENAPI=1 dotnet test tests/Api.Tests`, then `npm run generate:api-types` from `frontend/`.
- New query names get no audit names (queries are not audited). Each slice with I/O has an `ActivitySource` span.
- No new dependency. `dotnet build -warnaserror` clean. No attribution trailers. Restore a hand mutation in a file the task is changing with the inverse edit, never `git checkout --`.

## Rulings already taken

1. **`find_products` wraps `ListProductsQuery`**, not `SearchProductsQuery`: the catalogue, every status, shopkeeper-only, no vectors and no search metric — Search is the shopper's (anonymous, hybrid, Active only). Its `Text` is a substring of the code or a name, so S-3b's tool passes the model's words as given; tokenising is S-3b's choice.
2. **`sales_summary` combines `GetSalesSummaryQuery` and `GetSalesMixQuery`** in S-3b's tool; no new query. The mix has no previous period, and the tool says so.
3. **"Worst" is among products that sold in the period**, lowest units first; a product that sold nothing is not in Ordering's orders, and finding it would be Ordering reading the catalogue. The stock read's `NoSales` count already names how many SKUs sold nothing, and S-3b's tool can quote it.
4. **Returns in a period are returns *opened* in it** (`CreatedAt` inside the window), whatever their status now, read through `IShopHistory.ReturnsOpenOrChangedSinceAsync(window.From)` and filtered. Grouped by reason and by SKU, units summed; no order, no customer, no comment.
5. **The restock list is per SKU, summed across warehouses**, as `StockHealth` counts it, with `Restocking.Needed` deciding membership and `StockCoverage` the days; a `warehouse` narrows to that warehouse's shelves first.
6. **Catalogue gaps are computed in memory over the live catalogue** (Draft and Active, a hundred products): the scale rule's "deferred with its number" — at the full catalogue it becomes SQL. The kinds are `MissingName` and `MissingImageAlt` (per culture), `MissingImage` and `DraftWaiting`; "missing a required attribute" is backlog.
7. **The non-AI fallback is a filter on Products**: `ListProductsQuery` gains `MissingCulture`, served as `?missing=en`, computed in SQL like the text filter; `catalogue_gaps`' `MissingName` reads the same rule.

## Review Focus

1. **A culture that is not supported** in `missing=` or a gap kind that needs one → 400 from the validator, never an unfiltered list. Pinned in Tasks 1 and 2.
2. **More than 50 matches** → 50 rows and the real total. Pinned in Tasks 2–5.
3. **A period with no sales or no returns** → empty rows, totals of zero, the currency still said; never an exception. Pinned in Tasks 3 and 4.
4. **A category filter that matches a parent** → its children's lines count (`OrderLine.IsIn`). Pinned in Task 3.
5. **A return line's comment** → never in the result. Pinned in Task 4 by an architecture-style test over the result types.

---

### Task 1: Products missing a culture — the filter the fallback needs

**Files:**
- Modify: `src/Catalog/Features/ListProducts/ListProducts.cs` (the query, validator, handler, endpoint)
- Modify: `src/Catalog/Ports/IProductRepository.cs` (`IProductCatalogReader.ListAsync` gains `string? missingCulture`)
- Modify: `src/Persistence/EfProductRepository.cs` (the filter in SQL)
- Modify: `tests/Catalog.Tests/InMemoryProductCatalogReader.cs` (the same filter in memory), every other caller of `ListAsync` (`grep -rn "\.ListAsync(" src tests`)
- Test: `tests/Catalog.Tests/ListProductsTests.cs`, `tests/Integration.Tests/ProductListSearchTests.cs` (the SQL)

**Interfaces:**
- Produces: `ListProductsQuery(ProductStatus? Status, string Culture, int Page, int PageSize, string? Text = null, string? MissingCulture = null)`; `IProductCatalogReader.ListAsync(ProductStatus? status, string? text, string? missingCulture, int page, int pageSize, CancellationToken ct)`; `GET /api/catalog/products?...&missing=en`.

- [ ] **Step 1: Failing tests.** In `ListProductsTests` (read the file's existing builder for products and its handler construction, and use them): a product named only in Spanish and one named in both → `new ListProductsQuery(null, "es", 1, 20, MissingCulture: "en")` returns only the first, `Total` 1, and the per-status counts count only products missing English. A validator test: `MissingCulture: "fr"` is invalid; `null` is valid. In `ProductListSearchTests` (Postgres, its existing seeding): the seed has products with both names; give one an `es`-only name through `Product.Localize` on a fresh one (or import a fixture product without `en`), then `ListAsync(null, null, "en", 1, 50, ct)` returns exactly it.
- [ ] **Step 2: Run** — `dotnet test tests/Catalog.Tests --filter "FullyQualifiedName~ListProductsTests"`. Expected: build FAILS (`MissingCulture` missing).
- [ ] **Step 3: Implement.** Query: add `string? MissingCulture = null`. Validator:

```csharp
        // A culture the shop does not speak would filter nothing and look like it
        // worked; refused, like an unknown status.
        RuleFor(x => x.MissingCulture)
            .Must(culture => culture is null || CultureNegotiation.Supports(culture))
            .WithMessage("Supported cultures: es, en.");
```

Handler: pass `query.MissingCulture` to `ListAsync`. Endpoint: bind `string? missing` and pass it. Reader port: the new parameter after `text`, documented "products whose name has no value in that culture". `EfProductRepository.ListAsync`, after the text filter:

```csharp
        if (missingCulture is { Length: > 0 } culture)
        {
            var missing = Missing(Culture.Normalize(culture));
            query = query.Where(product => missing.Contains((Guid)(object)product.Id));
        }
```

with

```csharp
    /// <summary>The ids of the products whose name has no value in <paramref name="culture"/> —
    /// the review queue's "missing English", and Steward's gap. A key test on the
    /// jsonb, a parameter not text; the same id subquery as the text filter.</summary>
    private IQueryable<Guid> Missing(string culture) =>
        context.Database.SqlQuery<Guid>(
            $"""
            SELECT p.id AS "Value" FROM catalog.products AS p
            WHERE NOT (p.name ? {culture})
            """);
```

(The counts are computed after both filters, so the chips count the missing products.) In-memory reader: filter `product => !product.Name.Cultures.Contains(Culture.Normalize(missingCulture))` when set.
- [ ] **Step 4: Run** — Catalog tests; the integration test (Docker up). Expected: PASS. Then `UPDATE_OPENAPI=1 dotnet test tests/Api.Tests`, `npm run generate:api-types` (frontend), `dotnet test tests/Api.Tests`, `npx nx run-many -t lint test` (frontend). Expected: the document gains `missing`; green.
- [ ] **Step 5: Commit** — `feat(catalog): products missing a culture, the filter Steward's fallback points at (S-3a)`.

---

### Task 2: Catalogue gaps

**Files:**
- Create: `src/Catalog/Features/CatalogueGaps/CatalogueGaps.cs`
- Modify: `src/SharedKernel/Paging.cs` (or a new `src/SharedKernel/ReadLimits.cs`: `public static class ReadLimits { public const int MaxRows = 50; }`)
- Modify: `src/Catalog/Ports/IProductRepository.cs` (`IProductCatalogReader.LiveAsync(CancellationToken)`), `src/Persistence/EfProductRepository.cs`, `tests/Catalog.Tests/InMemoryProductCatalogReader.cs`
- Test: `tests/Catalog.Tests/CatalogueGapsTests.cs`; `tests/Api.Tests/EndpointAuthorizationTests.cs` (the route)

**Interfaces:**
- Produces: `enum CatalogueGap { MissingName, MissingImage, MissingImageAlt, DraftWaiting }`; `CatalogueGapsQuery(CatalogueGap Kind, string? Culture, int Limit = ReadLimits.MaxRows) : IQuery<CatalogueGaps>`; `CatalogueGaps(CatalogueGap Kind, string? Culture, int Total, IReadOnlyList<GapRow> Rows)`; `GapRow(string ProductId, string Code, string Name, ProductStatus Status)`; `static class CatalogueGapRules { static bool Has(Product product, CatalogueGap kind, string? culture); }`; `GET /api/catalog/gaps?kind=&culture=&limit=`, operation `ListCatalogueGaps`, Shopkeeper.

- [ ] **Step 1: Failing tests** — `CatalogueGapsTests`:
  - `CatalogueGapRules.Has`: a product with only `es` has `MissingName` for `en` and not for `es`; a product with no image has `MissingImage`; a product whose image's alt has only `es` has `MissingImageAlt` for `en`, and one with no image does **not** (no image is the other gap); a Draft has `DraftWaiting`, an Active does not.
  - The handler over an in-memory reader of 60 products missing English: `Total` 60, 50 rows, ordered by code; `Name` in the asked culture's fallback (`Name.In(culture ?? "es")`).
  - The validator: `MissingName` and `MissingImageAlt` need a supported culture (`null` and `"fr"` invalid); `MissingImage` and `DraftWaiting` take none; `Limit` 1–50.
  - Archived products are never gaps (the reader returns Draft and Active only — assert in the in-memory reader's test that an archived one is absent).
- [ ] **Step 2: Run** — Expected: build FAILS.
- [ ] **Step 3: Implement** `src/Catalog/Features/CatalogueGaps/CatalogueGaps.cs`: the records above; `CatalogueGapRules`:

```csharp
public static class CatalogueGapRules
{
    /// <summary>What the catalogue lacks, one rule per kind — the same rule the
    /// Products filter applies for a missing name (S-3a, ruling 7).</summary>
    public static bool Has(Product product, CatalogueGap kind, string? culture) => kind switch
    {
        CatalogueGap.MissingName => !product.Name.Cultures.Contains(Culture.Normalize(culture!)),
        CatalogueGap.MissingImage => product.Images.Count == 0,
        CatalogueGap.MissingImageAlt => product.Images.Any(image =>
            image.Alt is null || !image.Alt.Cultures.Contains(Culture.Normalize(culture!))),
        CatalogueGap.DraftWaiting => product.Status == ProductStatus.Draft,
    };
}
```

the validator (`Kind` in enum; `Culture` required and supported for the two per-culture kinds, null otherwise; `Limit` between 1 and `ReadLimits.MaxRows`); the handler (`IProductCatalogReader.LiveAsync` → filter with the rule → order by `Code` → `Total`, `Take(Limit)`, `GapRow` with `Name.In(query.Culture ?? CultureNegotiation.Default)`; span `catalog.gaps` with tags kind and total); the endpoint (`kind` bound as `WireEnum<CatalogueGap>`, required; `culture` optional; `limit` optional). Reader: `LiveAsync` returns every product whose status is not Archived, no tracking. In-memory reader: same.
- [ ] **Step 4: Run** — Catalog tests; Api tests (policy route added); OpenAPI regenerated and types generated; frontend lint/test. Expected: green.
- [ ] **Step 5: Commit** — `feat(catalog): what the catalogue lacks, one rule per kind, fifty rows and the total (S-3a)`.

---

### Task 3: Product performance

**Files:**
- Create: `src/Ordering/Features/Dashboard/ProductPerformance.cs`
- Modify: `src/Ordering/SalesRanking.cs` (an optional line filter)
- Test: `tests/Ordering.Tests/ProductPerformanceTests.cs`; `EndpointAuthorizationTests`

**Interfaces:**
- Produces: `SalesRanking.Rank(IEnumerable<Order> orders, Func<OrderLine, string> key, Func<OrderLine, bool>? lines = null)` (existing callers unchanged); `enum PerformanceRanking { Best, Worst }`; `GetProductPerformanceQuery(DashboardPeriod Period, PerformanceRanking Ranking, string? Category = null, int Limit = 10) : IQuery<ProductPerformance>`; `ProductPerformance(DashboardPeriod Period, PerformanceRanking Ranking, string? Category, string Currency, int Total, IReadOnlyList<ProductSales> Rows)`; `ProductSales(string ProductId, string Name, int Units, decimal Revenue)`; `static class ProductPerformanceFigures { static ProductPerformance Of(DashboardPeriod, PerformanceRanking, string?, int limit, IReadOnlyList<Order> placed); }`; `GET /api/dashboard/products?period=&ranking=&category=&limit=`, operation `GetProductPerformance`, inside the dashboard group (Shopkeeper).

- [ ] **Step 1: Failing tests** — `ProductPerformanceTests` over `ProductPerformanceFigures.Of` with orders built by `tests/OrderBuilder.cs` (read it; it builds live, paid orders with lines): best ranks by units then revenue (`SalesRanking`'s own order); worst is the reverse of best; a category keeps only lines `IsIn` it, so a parent category counts its children's lines; a rehearsal or unpaid order never counts; 60 products → `Total` 60, `Limit` rows; no orders → `Total` 0, no rows, currency `DashboardCurrency.Default`. A validator test: `Limit` 1–50, `Ranking` and `Period` in enum.
- [ ] **Step 2: Run** — Expected: build FAILS.
- [ ] **Step 3: Implement.** `SalesRanking.Rank` gains `Func<OrderLine, bool>? lines = null` applied as `.SelectMany(order => order.Lines).Where(lines ?? (_ => true))`. `ProductPerformanceFigures.Of`:

```csharp
    public static ProductPerformance Of(
        DashboardPeriod period, PerformanceRanking ranking, string? category, int limit, IReadOnlyList<Order> placed)
    {
        var ranked = SalesRanking.Rank(placed, SalesRanking.ByProduct,
            category is { Length: > 0 } wanted ? line => line.IsIn(wanted) : null);

        // Worst is the same ranking read from the other end: among what sold, the
        // least. What sold nothing is not in an order (S-3a, ruling 3).
        var ordered = ranking == PerformanceRanking.Best ? ranked : [.. ranked.Reverse()];

        return new ProductPerformance(period, ranking, category, DashboardCurrency.Of(placed), ranked.Count,
            [.. ordered.Take(limit).Select(sold => new ProductSales(sold.Key, sold.Name, sold.Units, sold.Revenue))]);
    }
```

The handler joins `DashboardHandlers` (or is its own, as `SalesMix` is — follow `SalesMix.cs`): `history.OrdersPlacedAsync(window.From, window.To)` then the figures; span `ordering.dashboard.products`. The endpoint in `DashboardEndpoints`' group, `ranking` bound as `WireEnum<PerformanceRanking>?` (default `Best`), `period` as the others.
- [ ] **Step 4: Run** — Ordering tests (the existing `SalesRanking` callers still pass); Api tests; OpenAPI and types regenerated. Hand mutation: drop the `lines` filter inside `Rank` → the category test red; restore with the inverse edit.
- [ ] **Step 5: Commit** — `feat(ordering): the products that sell most and least, by category, from the ranking the home uses (S-3a)`.

---

### Task 4: Returns in a period

**Files:**
- Create: `src/Ordering/Features/Dashboard/ReturnsSummary.cs`
- Test: `tests/Ordering.Tests/ReturnsSummaryTests.cs`; `EndpointAuthorizationTests`

**Interfaces:**
- Produces: `GetReturnsSummaryQuery(DashboardPeriod Period, int Limit = 10) : IQuery<ReturnsSummary>`; `ReturnsSummary(DashboardPeriod Period, int Returns, int Units, IReadOnlyList<ReasonUnits> ByReason, int SkuTotal, IReadOnlyList<SkuReturns> BySku)`; `ReasonUnits(ReturnReason Reason, int Units)`; `SkuReturns(string Sku, int Units, IReadOnlyList<ReasonUnits> ByReason)`; `static class ReturnsFigures { static ReturnsSummary Of(DashboardPeriod, int limit, DashboardWindow window, IReadOnlyList<ReturnRequest> returns); }`; `GET /api/dashboard/returns?period=&limit=`, operation `GetReturnsSummary`, Shopkeeper.

- [ ] **Step 1: Failing tests** — `ReturnsSummaryTests` over `ReturnsFigures.Of` (build `ReturnRequest`s the way `tests/Ordering.Tests` already does for returns — read `ReturnsTests` or the returns builder): only returns whose `CreatedAt` is inside the window count, whatever their status; units by reason are summed over lines, every reason listed (zero included) in enum order; SKUs ordered by units, then SKU; `SkuTotal` is all SKUs, `BySku` is `Limit` of them; no returns → zeros. And the privacy test the spec asks for:

```csharp
    /// <summary>No order, no customer, no comment: a return's comment is the
    /// shopper's own words, and these figures may leave the machine (the spec).</summary>
    [Fact]
    public void The_summary_carries_no_order_customer_or_comment()
    {
        string[] forbidden = ["OrderId", "CustomerId", "Comment", "Email", "Address"];
        var names = new[] { typeof(ReturnsSummary), typeof(ReasonUnits), typeof(SkuReturns) }
            .SelectMany(type => type.GetProperties()).Select(property => property.Name);

        Assert.Empty(names.Intersect(forbidden));
    }
```

- [ ] **Step 2: Run** — Expected: build FAILS.
- [ ] **Step 3: Implement** the records; `ReturnsFigures.Of` (filter `window.From <= r.CreatedAt && r.CreatedAt < window.To`; flatten lines; group); the handler (`history.ReturnsOpenOrChangedSinceAsync(window.From)` → figures; span `ordering.dashboard.returns`); the endpoint in the dashboard group. Read `ReturnRequest.Lines` (the property name) before writing it.
- [ ] **Step 4: Run** — Ordering, Api; OpenAPI and types regenerated.
- [ ] **Step 5: Commit** — `feat(ordering): returns opened in a period, by reason and by SKU, with nobody's words in them (S-3a)`.

---

### Task 5: What needs restocking, with its coverage

**Files:**
- Create: `src/Inventory/Features/StockHealth/RestockList.cs`
- Test: `tests/Inventory.Tests/RestockListTests.cs`; `EndpointAuthorizationTests`

**Interfaces:**
- Produces: `GetRestockListQuery(string? Warehouse = null, int Limit = ReadLimits.MaxRows) : IQuery<RestockList>`; `RestockList(string? Warehouse, int RestockAt, int Total, IReadOnlyList<RestockRow> Rows)`; `RestockRow(string Sku, int Available, decimal? PerDay, decimal? CoverageDays)`; `GET /api/dashboard/restock?warehouse=&limit=`, operation `GetRestockList`, Shopkeeper.

- [ ] **Step 1: Failing tests** — `RestockListTests`, with `FakeStock` (`tests/Inventory.Tests/InventoryFakes.cs`) and a pace fake like `StockHealthHandlerTests.FixedPace` (move it to `InventoryFakes.cs` if both need it, as a shared test fake): a SKU at or below `Restocking.At` is listed, above is not; two warehouses' shelves of a SKU are summed; `warehouse` narrows to that warehouse's shelves before summing; a SKU that sold has `PerDay` and `CoverageDays` from `StockCoverage`, one that did not has nulls; sold-out SKUs first, then by coverage days ascending (nulls last), then by SKU; 60 SKUs → `Total` 60, 50 rows.
- [ ] **Step 2: Run** — Expected: build FAILS.
- [ ] **Step 3: Implement** — the handler mirrors `StockHealthHandler`'s reads (`stock.AllAsync`, `pace.SoldAsync(skus, now - StockCoverage.PaceWindow, now)`, `StockCoverage.PerDay`, `StockCoverage.Days`), with the warehouse filter first and `Restocking.Needed` for membership; span `inventory.dashboard.restock`. The endpoint beside `/api/dashboard/stock` in `StockHealth.cs`'s module.
- [ ] **Step 4: Run** — Inventory, Api; OpenAPI and types regenerated.
- [ ] **Step 5: Commit** — `feat(inventory): every SKU to restock with its days of cover, not the five closest (S-3a)`.

---

### Task 6: The privacy note and the board

**Files:**
- Modify: `docs/privacy.md` (a section: what Steward's reads carry, and that with a hosted profile this is what leaves the machine), `docs/specs/steward.md` (S-3a *as built*: the table of tools → queries, the rulings), `docs/delivery-plan.md` (S-3a done under phase 12), `docs/blog/notebook.md` (an entry)

- [ ] **Step 1:** Write the docs; the spec's table gains a column *Query* naming each tool's query or pair.
- [ ] **Step 2:** `dotnet build -warnaserror`; `dotnet test` (Docker up); frontend `npx nx run-many -t lint test`. Expected: green.
- [ ] **Step 3: Commit** — `docs(steward): what Steward reads, query by query, and what may leave the machine (S-3a)`.
