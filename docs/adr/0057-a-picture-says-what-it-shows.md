# 0057 — A picture says what it shows: a variant has no picture of its own, and a colour's gallery is derived

**Status**: accepted (phase 13a, 2026-10-08; proposed in `docs/analysis/catalogue-model.md` and taken by the author the same day — *the tag, and the level between product and variant deferred*)

## Context

A variant pointed at one picture (`Variant.Image`, null meaning *the product's*).
Three things followed, and the phase's own work ran into each:

- **One picture a colour.** A second view of the navy shoe had nowhere to say it
  was the navy one's.
- **The same fact once a size.** Navy in eight sizes was eight rows naming the
  same picture.
- **The model answered the opposite of the question asked of it.** What the shop
  asks of a picture is *what is this of* — to show it when navy is chosen, to
  hold it to navy when a model reads it (`docs/specs/images.md`, I-4d). The
  model stored *which picture does this variant have*, and
  `ProductPicturesReader` turned it round, guessing for the cover.

How others do it: Shopify hangs one media on a variant and has the same three
limits; Amazon gives every child ASIN its own images, which repeats them a size;
Zalando and Akeneo keep a level between the product and the size — the colour —
and hang the pictures there.

## Decision

**A picture of the gallery says which of the product's axis values it shows.**
`ProductImage.Shows` is a list of `AxisValue(Axis, Value)`, at most one an axis —
`COLOR: NAVY_BLUE`, usually alone. A picture that says nothing shows the product
and no variant in particular: the back, a detail, the box.

- **A variant's pictures are derived**, never stored: `Product.PicturesOf(variant)`
  is the pictures whose `Shows` the variant's coordinates satisfy, then the ones
  that say nothing, in the gallery's order. `PictureOf(variant)` — what a cart
  line, a search result and an agent's line item show — is the first of them, and
  the cover when none is its own.
- **The aggregate guards the tag.** `Product.Show` refuses an axis the product
  does not vary by and a value no variant still sold holds; discontinuing the
  last variant of a colour clears that colour's tags. A tag can therefore not
  point at nothing.
- **A source says it the same way.** `ExternalImage.Shows` carries the source's
  own words (`color: negro`), resolved through the same definitions a variant's
  are, so a variant and its picture land on the same value. A tag the aggregate
  refuses costs the tag and a warning, never the product.
- **Stored inside the gallery's JSON**, with the picture. The migration turns
  every variant's old picture into a tag of that variant's colour before it
  drops the column.
- **The API gains, and loses nothing**: a variant keeps `imageId` and gains
  `imageIds`, its gallery.

## Deferred, on purpose: a level between product and variant

The general answer is a colour level — Zalando's, Akeneo's first variant level:
`Product → Colour → Size`, with the pictures, and possibly a price, a name and a
URL of its own, on the colour. **It was considered and deferred by the author on
2026-10-08**, not rejected.

- **Why not now.** The cart, the order line, stock, tariffs and search all speak
  SKU; each would learn a new thing, and nothing built today needs more of that
  level than the pictures of a colour — which the tag gives.
- **What would bring it back.** A colour that needs something *of its own* that
  is not a picture: its own URL and SEO (a page a colour, as Zalando has), its
  own price or promotion, its own description, or being the unit a search result
  shows. Or a second axis that needs pictures in combination often enough that
  tags stop being readable.
- **What this decision leaves ready for it.** A tag is already *the pictures of a
  value of an axis*: moving to a level is grouping the pictures that share a tag
  under a row, a migration over data that says exactly what the level would hold.
  Nothing here has to be undone.

## Consequences

- *It belongs to another variant*, the third decision on a picture doubt, is now
  changing one tag — and is **applied from the doubt itself** (the author's
  ruling, 2026-10-08). The decision leaves Knowledge through the outbox carrying
  the picture and the colour seen; a worker turns it into Catalog's own command,
  `SetImageShows`, keeping what the picture shows on its other axes; Catalog
  refuses a colour no variant is sold in. Knowledge still writes nothing to a
  product (ADR 0018), and the other two decisions still change nothing.
- A picture is no longer held to a colour by elimination: an untagged cover of a
  product in two colours is held to none, even when one of them is all that is
  left on sale.
- A picture shows **one** value of an axis. *Navy and black together* is a
  picture that says nothing.
- Not decided here: pictures' roles (*swatch*, *lifestyle*, *size chart*) and
  kinds (video, 3D). They would be another property of the picture, beside this
  one.
