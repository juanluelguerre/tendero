# Spec: the storefront's filters (facets)

**Status**: draft — the proposal, for the author to read before anything is built (R-7.3)
**Slice(s)**: Search (the index and `SearchProducts`), the storefront's category and search pages, WebMCP
**Article**: *Filters are a contract, not a sidebar*

## Problem

A shopper who reaches a category can scroll it and nothing else. "Faceted search"
is on the floor of the product vision (`CLAUDE.md`), and the storefront has none:
no brand, no size, no colour, no price range, no "in stock only". The author
asked on 2026-09-28 whether the shop should have a category menu on the left,
always visible or hideable, and whether it should allow several selections. The
answer given then, and developed here, is that a permanent left menu on every
page is not what the shop needs. The shop needs **filters on the pages that list
products**, where several selections do make sense.

**Measured on 2026-09-28**, on the laboratory's 100 products:

| Type | Products | Brands | Price | Attributes present |
|---|--:|--:|---|---|
| Shoes | 13 | 2 | 29.90–169.00 | colour, size, material, gender, drop |
| Cookware | 12 | 1 | 18.50–89.90 | material, diameters, induction, pieces, capacity |
| Desk lamps | 11 | 1 | 19.90–129.00 | colour temperature, power, colour |
| Tableware | 10 | 1 | 22.90–64.00 | material, pieces, colour |
| Shirts | 7 | 2 | 24.50–74.90 | colour, gender, size, material |
| … 11 more | 3–6 each | 1–2 | | |

Three departments, 23 categories, 3 to 13 products per category. The author's
correction, twice on the same day: **the catalogue will grow, and the design is
for that shop, not for this one — think of Amazon and the shops like it**. The
laboratory is where it is proven, with the data it has. Brand is a first-class
filter for that reason, even though most categories have one brand today.

### The model: what Amazon and the large shops do

Read on 2026-09-28 against Amazon, El Corte Inglés and Zalando, and kept only
where it answers a job the shop has:

- **Filters are where the results are, and above all on search.** Most shoppers
  arrive at a listing by typing, not by walking a tree, so the search results
  page is the primary surface, and a category page is the same page with a
  category chosen.
- **A department filter at the top of a search**: *En Moda (34) · En Hogar (12)*,
  the category tree narrowed to where the results are. It is navigation inside
  the results.
- **Brand for hundreds of brands.** The seven with most results come first, then
  *Ver más*, and past twenty a box to find a brand. That is built now, with two
  brands, so the day there are three hundred it does not change.
- **Each filter drawn as what it is.** Colours as swatches with their name
  (never colour alone), sizes as a grid of buttons, price as two inputs plus
  bands computed from the results, switches for yes-or-no facts.
- **Facts the shop already holds, as filters:** *En oferta* (a running promotion
  applies — Pricing knows), *Novedades* (listed in the last 30 days —
  `AvailableFrom`), *Disponible* (in stock).
- **A count that says how much there is**: *1–24 de 55 resultados para
  «mochila»*, and the sort beside it.
- **Not copied, because the shop has no data for it:** customer reviews (there
  are none yet), sellers (one shop), delivery speed (one carrier). Each is a
  filter the day its data exists, and the shape below takes it without a change.

What the index holds today (`ProductSearchDocument`):
- one document per variant, collapsed by product;
- brand, categories, variant axis values, price, stock;
- the other attributes only as free text (`AttributesText`), so material, gender
  and induction cannot be filtered on as they stand.

## Behavior

### Where the filters live

- **On search results and on category pages (a department or a leaf), which are
  the same listing with a category chosen. Nowhere else.** The home page, the product page, the cart and the checkout keep their
  width for the product and the purchase. Baymard recommends a checkout closed
  to navigation.
- **On a desktop (≥ 64 rem): a column on the left**, beside the results. It
  holds the department's subcategories at the top and the filters below them. It
  folds: one button hides and shows it, and the choice is remembered per browser
  (a convenience, so `localStorage`).
- **On a phone: a *Filtrar (n)* button** above the results, which opens a sheet.
  It is a native modal `<dialog>`, like the menu drawer. Choices there are made
  as a batch and applied with **Ver 12 productos**, so the page does not reload
  under a thumb on every tap. On a desktop each choice applies at once.
- **Applied filters are chips above the results**, each removable, plus **Quitar
  todo**. That is how a person sees what narrowed the list without opening the
  column.
- **A sort control** beside the count: relevance (search) or recommended
  (category), price up and down, newest. `SearchProducts` already takes `sort`;
  the page never offered it.

### What can be filtered

| Filter | Kind | Drawn as | Source | Shown when |
|---|---|---|---|---|
| Department / category | tree, narrowing | links inside the results | `CategoryCodes` | search results with 2 or more departments |
| Brand | options, multi | checkboxes, top 7, *Ver más*, a box past 20 | `Brand` | 2 or more brands |
| Price | range | two inputs and bands from the results | `PriceAmount` | always |
| On offer | switch | switch | **new**: a running promotion applies (Pricing, projected) | some results are on offer |
| New arrivals | switch | switch | `AvailableFrom` in the last 30 days | some results are new |
| Available | switch | switch | `InStock` | always |
| Colour (variant axis) | options, multi | swatches with the name | axis values | 2 or more values |
| Size (variant axis) | options, multi | a grid of buttons | axis values | 2 or more values |
| Material, gender (option attributes marked `IsFacet`) | options, multi | checkboxes | **new**: attribute values indexed as keywords | 2 or more values |
| Induction (boolean attributes marked `IsFacet`) | switch | switch | **new** | some results have it |

The order is the one above, and the attribute filters follow their definitions'
order. A new attribute marked `IsFacet` in the backoffice appears as a filter
after a reindex, with no code.

- **A filter appears only when it splits the results.** A brand filter over a
  category with one brand is a control with nothing to do. When the catalogue
  grows, brand appears on its own; nothing is configured per category. The rule is
  the server's (ADR 0043's lesson: a rule the screen counts by is the server's).
- **Within a filter the choices add up (OR), across filters they narrow (AND).**
  Two brands show both; a brand and a size show that brand's shoes in that size.
- **Every option carries its count**, and the counts are **disjunctive**: a
  filter's own counts are computed without its own selection. Choosing Vertex
  still shows how many Caminante would add. Without that, a shopper could never
  add a second brand.
- **An option with no results is shown disabled, not hidden.** The column keeps
  its shape, and nothing moves under the pointer (the lesson of the backoffice
  chips, 2026-09-28).
- Labels are localized: the index holds option codes, and the labels come from
  the attribute definitions in the page's culture, as `DescribeSkus` does.
- **Categories stay single-select.** A category is a place, with its URL,
  breadcrumb, canonical and `category_opened`. Inside search results, the
  category *is* offered as a filter with several choices, because there it
  narrows a list rather than naming a place.

### The URL, and search engines

- Filters and sort live in the query string:
  `?brand=vertex,caminante&size=42&price=30-80&stock=1&sort=price-asc`.
  They can be shared, the back button undoes one step, and a reload keeps them.
- **A filtered page is never a new page for a search engine.** Its canonical
  stays the unfiltered category, and the filter controls are buttons, not links,
  so no crawler follows a combination. One category does not become a thousand
  indexable URLs.

### The API

- `GET /api/search` gains the filters as parameters, and its answer gains
  `facets`: each with its code, kind, localized label, and options with code,
  label, count and whether chosen, or for a range the minimum and maximum.
- **Counts come from Elasticsearch aggregations**, one request per page view,
  with a filter aggregation per facet for the disjunctive counts. That scales
  with the catalogue; nothing counts in memory.
- **Search results count the whole result set, as Amazon's do.** Hybrid search
  merges BM25 from Elasticsearch with vectors from Qdrant in the application, so
  the counting request asks Elasticsearch for the union: the text query, OR the
  product ids the vector half returned (a bounded list, its `k`). One aggregation
  over that union counts exactly what the page lists. The lexical path never
  depends on the vector one (invariant 8): with Qdrant down, the union is BM25's
  results alone and the counts say so.

### Agents

- **WebMCP gains `apply_filter`.** Phase 6 deferred it, in a written decision,
  "until the shop has facets to filter on". This is that day. It calls the same
  service the page does, with the same codes, and the shopper sees the change.
- **The MCP server's `search_products` gains the same filters.** The facets in
  the answer tell an agent what it can narrow by, without a second call.

## Non-goals

- **Not a permanent left menu on every page**, for the reasons above; the header's
  menu (R-5.1) stays the way into the catalogue.
- **Not range filters over numeric attributes yet** (capacity in litres, power in
  watts). They need numeric fields per attribute in the index (a nested or
  flattened mapping), and today 2 to 5 products per type carry them. **Deferred,
  with the number**: they are worth building when a category holds 20 or more
  products with the same numeric attribute. In the standing backlog (the author,
  2026-09-28).
- **Not customer ratings yet, but planned for.** Amazon's *4 estrellas o más* is
  one of its most used filters, and the shop has no reviews to count. Ratings
  are a feature of their own, in the standing backlog: a review written after
  delivery, by the customer who bought it; moderation in the backoffice, where a
  model may propose and a person approves (invariant 12); an average and a count
  per product, projected to the index; then the filter and the stars on each
  result. The facet shape here takes it with no change: one more filter, drawn as
  star thresholds.
- **Not saved filters or alerts.** They need an account and a reason.
- **Not personalization of the order.** That is the vision's standard AI, later.

## Measures

- `SearchEval`'s NDCG@10 must not move: filters narrow, they do not rerank. The
  golden set runs unfiltered, and a test runs one query filtered to prove the
  filter is applied after retrieval, not instead of it.
- A contract test of the facet shape; unit tests of the disjunctive counts and of
  the "splits the results" rule; a Postgres-free test against a throwaway
  Elasticsearch, as `SearchEval` uses (not the author's work instance on :9200).
- e2e: choose two brands and a size on a category, check the count and that the
  URL holds them; open the sheet on a phone and apply as a batch; back button.
- Accessibility: every filter is a labelled group, options are checkboxes, the
  count is part of the accessible name, and the sheet traps focus as the drawer
  does. The ratchet runs the filtered page at every width.

## Evidence of use

- `filter_applied {facet, surface}`: facet is `category`, `brand`, `price`,
  `offer`, `new`, `stock`, `axis` or `attribute`, never the value; surface is `category` or
  `search`. It answers whether shoppers filter, and with what.
  - **Decision:** if under 5 % of category visits use a filter in a month, the
    column opens folded by default.
  - **Decision:** a facet nobody uses in a month is looked at before it is kept.
- `filters_cleared {surface}`: whether *Quitar todo* is used, or filters are
  removed one by one.
- The product views that follow a filtered listing already carry
  `arrived_from` (`product_viewed`). A new value, `filtered`, answers whether
  filtering leads anywhere.

## Open questions

1. ~~Price as two inputs, or as bands computed from the results?~~ **Decided by
   the author, 2026-09-28: both, as Amazon.** Three to five bands from the
   results' percentiles, rounded to prices a person would say (10, 20, 25, 50,
   100…), empty bands dropped, each with its disjunctive count; choosing a band
   fills the two inputs, and editing an input unchooses the band. The URL holds
   the range (`?price=30-60`), never the band, because bands move with the
   results and a link must not.
2. ~~The phone's sheet applies as a batch, the desktop at once.~~ **Decided by the
   author, 2026-09-28: both.** A phone applies as a batch with *Ver N productos*;
   a desktop applies each choice at once.
3. ~~Mock-ups in `.pen` or HTML?~~ **Decided by the author, 2026-09-28: `.pen`,
   before building**, in `design/unpublished/mockups/storefront-facets.pen`, with
   exported PNGs beside it as the redesign's. To draw: search results with the
   column (1440), the column folded, a category page, the phone's sheet (390),
   and the applied chips with the count and the sort.

## The mock-ups (2026-09-28)

Drawn in `design/unpublished/mockups/storefront-facets.pen` with `design/tokens.css`'s
light theme and no new colour; the PNGs beside it in `storefront-facets/`:

1. `1-search-results-1440.png` — search results with the column, three filters
   applied (a brand, a price band, *Disponible*), their chips, the count and the sort.
2. `2-search-folded-1440.png` — the column folded: five cards a row, and *Mostrar
   filtros (3)* saying how many apply while they are out of sight.
3. `3-category-department-1440.png` — a department, *Bolsas y mochilas*, 38
   products: the column opens on its sections, nothing applied, *Mostrar 14 más*.
4. `4-search-results-390.png` — a phone: *Filtros (3)* and the sort side by side,
   the applied chips in a row that scrolls sideways.
5. `5-filter-sheet-390.png` — the phone's sheet: price bands as buttons, the
   inputs under them, the switches, swatches, and *Ver 8 productos*, the view's
   one clay action.

**The header changed while it was being drawn, and the author approved it**
("me cuadra mucho más este diseño"). The author saw that R-5.1's *☰ Tienda*,
left of the logo, and the column's *Ocultar filtros* sat in the same corner and
both read as "opens a side panel". They are different kinds of thing:
navigating somewhere else, and narrowing this list. On a desktop the header now
follows Amazon, Zalando and El Corte Inglés:
- the **logo top left**, leading home;
- a **row of the departments, visible**, each opening R-5.1's panel, with
  *Ofertas* and *Novedades* at its end (a *☰ Todo* joins it when there are more
  departments than fit);
- the filter toggle carries **the sliders icon**, the one that means *filter* in
  nearly every shop.

A category is now one click from any page on a desktop, where R-5.1 made it two
(S1). The phone keeps R-5.1's header: the hamburger left of the logo is that
platform's convention. R-6's second walkthrough measures this header, not R-5.1's.

**What drawing found:**
- **The counts have to add up, and a mock-up that does not add up misleads.** The
  first draft said "1–24 of 55" beside three narrowing filters, and each
  filter's counts belonged to a different selection. Redrawn so every section sums
  to the list, and each filter counts the others' selection (disjunctive). That is
  the rule F-2 has to hold, and its first test.
- **Folding pays in columns, not in whitespace**: with the column gone the grid
  takes five cards a row instead of stretching four.
- **Price bands are buttons on a phone** (four in a row, each with its count), not
  radios: a thumb's target, and the same choice.
- The departments are *Ropa, Hogar, Bolsas y mochilas* (the seed's), not the
  names first drafted: a mock-up draws the shop's data, never placeholders that
  look like it.

## Building it, in order

| # | Increment | Size |
|---|---|:-:|
| F-0 | The desktop header: logo left, the departments' row (opening R-5.1's panel), *Ofertas* and *Novedades*; the phone's unchanged | S |
| F-1 | The index: option and boolean attributes marked `IsFacet` as keywords, *on offer* projected from Pricing's promotions, *new* from `AvailableFrom`; a reindex | M |
| F-2 | `SearchProducts`: filter parameters and facets with disjunctive counts, over browse and over hybrid search (the union), the "splits the results" rule; contract and ES tests | L |
| F-3 | One listing for search and category pages: the column with brand at scale, swatches, size grid, price, the chips, the sort, the count, the URL; folding remembered | L |
| F-4 | The phone's sheet; accessibility; e2e | M |
| F-5 | WebMCP `apply_filter` and MCP `search_products` filters | S |
| F-6 | The events, in the tracking plan | S |

Search results are no longer a later version: they are the primary surface, as
on Amazon, and they arrive with the first build.
