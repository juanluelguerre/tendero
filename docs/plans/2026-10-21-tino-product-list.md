# Tino's list of products — what is shown, what comes first, what the link counts

The board's standing-backlog row (found by the author on 2026-10-14): asked
*coffee makers under €30*, then *and between 31 and 50?*, Tino showed one card,
the 12-cup Aroma, which was out of stock. It left out the 4-cup induction one
(€44.90) and the Aroma Compacta (€49.90), and its *See the 11 results* counted
pans.

## Decided

- **Option C** (the author, 2026-10-21). A card the model wrote nothing about is
  shown when a search of the turn found it for what was asked, under **one line
  written by code**: *Coincide con tu búsqueda* / *Matches your search*. Code
  knows that line is true, so it states nothing a model said. It amends the rule
  of 2026-10-06 and does not replace it: a product whose reasons **all fell to
  grounding** is still left out and never brought back. That was the injected
  description's case.
- **No narrowing by the words' category** (the author, 2026-10-21, after the
  measurement). The first plan narrowed the search to a category when the
  question's words named one. Over the search golden set, 12 of the 57 queries it
  would narrow hid relevant products, 45 in all (`docs/search-evaluation.md`,
  *Narrowing a search by the category its words name: refuted*).
- **The search stays in relevance order.** Putting in-stock products first inside
  the search was built, then undone: an in-stock pan would have come ahead of an
  out-of-stock coffee maker in what the model reads. Stock orders the cards
  instead, and code draws those.
- Invariant 12 throughout. Code chooses how many cards appear, which, and in what
  order. The model chooses which products it says something about, and words
  what it says.

## What was built

1. **A search says what it found** (kernel, Search). `ReadAnswer.Found` carries
   four things:
   - the products in ranked order, each with its category code and whether it
     can be bought;
   - the search's category facet counts;
   - the search as it was run (`SearchLink`, prices as `Money`);
   - its total.

   `search_products` fills it, except when it found only the nearest products:
   those are not what was asked, so *Matches your search* would be false.
2. **The sheet keeps it** (`FactSheet.Searches`, `FactSheet.Found(code)`).
3. **Grounding leaves out a product whose reasons all fell**, and records it in
   `TinoBody.Fell`. It keeps a product the model named without a reason.
4. **`TinoCards` draws the cards**:
   1. the products the model explained;
   2. the products it named without a word, when a search found them;
   3. up to three in all, filled from what the searches found **in the category
      of the products the model named**, in-stock ones first.

   - Among the cards, the in-stock ones come first, and each group keeps its
     order.
   - A card with no reason carries `note`, a `Message<ProductNote>` with the code
     `MatchesSearch`, in es and en.
   - A product in `Fell` never comes back.
5. ***See all* is `TinoAnswer.More`** (`SeeAll`).
   - It is the last search that found one of the cards, as it was run.
   - When the cards share one category and the search named none, the link goes
     inside that category, and the total is that category's facet count.
   - When the cards already show everything, there is no link.
   - The panel writes the link with `listing-filters.ts`, as the listing's own
     parameters (`q`, `within`, `price`, `brand`, `offer`, `stock`).
6. **Telemetry**: `tino_result_followed` gains `card` (`reasoned`, `matched`,
   `none`).
   - The question: is a card code added ever opened?
   - The decision it would change: if not, topping up stops.
7. **Tino's golden set**:
   - `Expectation` gains `minProducts` and `onlyProducts`;
   - four cases: coffee makers under 30, and *and between 31 and 50?* after it,
     each in es and en.

   Measured on the local model before and after, with the same judge and cases;
   the results go in `docs/analysis/tino-models.md`.

## Not in this run

- Narrowing the storefront's own search box: refuted for Tino, and the same
  numbers stand against it there.
- Telling the model about stock in its prompt: code orders the cards.
