# Plan: the catalogue's model — a picture says what it shows, a category says what is required

2026-10-08 · phase 13a · from `docs/analysis/catalogue-model.md`, on the
author's go-ahead (*adelante con la propuesta 1 y 2*).

The analysis left three rulings open and the author asked to start before
giving them. They are the proposals' own open points, so the plan takes the
recommended answer for each **as a starting point, to be corrected**:

| Ruling | Taken here |
|---|---|
| A tag on the picture, or a level between product and variant | The tag |
| A picture shows one value of an axis, or several | One |
| A colour's gallery ends with the untagged pictures, or shows them only when it has none | It ends with them |
| An incomplete product publishes | Yes, with what it lacks shown |
| Which attributes are required | What every seed product of the category already holds |
| Two levels or three | Two: required, expected |

## Proposal 1 · A picture says what it shows

Each slice ends green and can be committed alone. Tests first; a hand mutation
for whatever is written beside its test.

**Where it is (2026-10-08): slices 1 to 4 built**, and the tag ruled by the
author over the level (ADR 0057). One thing came out differently from what is
written below: the import tells a picture what it shows **whether or not every
image arrived** — a tag travels with its picture, where a place in a list did
not — and a tag the aggregate refuses is logged and skipped. Slices 5 to 7 and
proposal 2 are next.

1. **The aggregate.** `ProductImage.Shows` — the axis values a picture shows, at
   most one an axis. `Product.Show(picture, values)` refuses an axis the product
   does not vary by and a value no live variant holds. `PicturesOf(variant)`:
   the gallery's pictures whose `Shows` the variant's coordinates satisfy, then
   the untagged, in the gallery's order; `PictureOf(variant)` is the first.
   `Variant.Image` and `SetVariantImage` stay until slice 3 and are read by
   nobody new.
2. **Persistence.** `shows` inside the gallery's JSON. A migration that turns
   every variant's `Image` into a tag of that variant's colour on that picture
   (a variant with no colour axis tags nothing: its picture is the cover
   already), then drops the column. Round trip against Postgres.
3. **The read paths, behind one method.** `GetProduct`, the cart, search's
   variant document and UCP's media read `product.PictureOf(variant)`;
   `ProductPicturesReader` reads the tag. `Variant.Image` goes. The product's
   response gains the pictures of each variant; OpenAPI and the frontend types
   regenerate.
4. **The import and the seed.** `ExternalImage.Shows` replaces
   `ExternalVariant.Image` (added this week, published nowhere); the seed's
   `variants[].image` becomes `images[].shows`; `tools/SeedImages` reads a
   variant's picture from the picture's tag. The real import of the pannier
   still gives olive, black, navy and a back that is nobody's.
5. **The storefront.** Choosing a colour shows that colour's gallery.
6. **The backoffice.** On a product's pictures, which colour each shows.
7. **The doubt's third decision, applied.** *It belongs to another variant*
   retags the picture — a Catalog command the doubt's decision raises through
   the outbox, as an approved claim reaches the catalogue (ADR 0018). Its own
   ruling before it is built: today a doubt changes nothing but itself.

Deferred, with its reason: pictures' roles and kinds; a level between product
and variant (ADR 0057, *Deferred, on purpose*, and the delivery plan's standing
backlog).

## Proposal 2 · A category says what is required

1. **The declaration.** A declared attribute is a code and a level; a plain
   code in the seed still reads as *expected*. `CategoryTree.RequiredOf`,
   inherited, raised by a descendant and never lowered.
   `Product.MissingRequired(tree)`; an axis counts as held when its variants
   hold it.
2. **The seed.** What every seed product of a category holds is marked
   required there, by a test that says so — the seed starts complete.
3. **The gaps.** `CatalogueGap.MissingRequiredAttribute`, its query and
   wording in both languages; the review queue says what a draft lacks.
4. **ADR 0056 amended**: required is a level of the declaration, and incomplete
   is shown, not refused.

## What is parked while this runs

- The captures of the picture doubts' screen: the page is part of what moves.
- The board's colour line, re-recorded with the plain question: independent of
  the model, finishes on its own.

## Done when

Build clean with `-warnaserror`; every test green; the pannier imported for
real with its three colours and its back; the storefront shows a colour's
pictures; nothing reads `Variant.Image`; the analysis's table of what it
touches has no row left.
