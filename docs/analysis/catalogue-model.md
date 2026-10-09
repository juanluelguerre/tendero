# The catalogue's model: pictures, variants, categories and attributes

Written 2026-10-08, during phase 13a, at the author's ask: *what is the exact
model, how does it compare with Amazon, Shopify and Zalando, and what should
change?* It describes the code as it is, sets it beside the others, and proposes
two changes — because the rest of the phase's work (a picture held to its
variant's colour, attributes offered by category) sits on this model, and it is
cheaper to move it now than after more is built on it.

**Ruled since** (2026-10-08): proposal 1 is taken as the tag on the picture and
is built as far as the API — ADR 0057, which also records the level between
product and variant as **deferred, not rejected**, and what would bring it back.
Proposal 2 goes ahead on its proposed answers. Each proposal still ends with
what was the author's to rule, and now says how it was ruled.

## The model as it is

**Product** (`Catalog/Domain/Product.cs`)

- Name and description as `LocalizedText`, brand, a base price, a status
  (draft, active, archived), the source it came from.
- **One category**, held as a code (`COOKWARE`). It is the product's type and
  the leaf of the navigation tree at once.
- **Attributes of its own**: typed values — an option, a number, a boolean,
  text.
- **A gallery**: an ordered list of pictures, each with its alt text in both
  languages. The cover is the first.
- **The axes its variants differ by**, in order (`COLOR`, `SIZE`).
- **Its variants.**

**Variant** (`Variant.cs`)

- A SKU unique across the catalogue, a price, a tax class, a status (available,
  discontinued).
- Its place on the axes: `{COLOR: NAVY_BLUE, SIZE: 38}`.
- **One picture, optional**: an id that is one of the gallery's. None means
  *the cover*.
- Stock and tariffs are not here: Inventory and Pricing hold them by SKU.

**Attribute definition** (`AttributeDefinition.cs`)

- A code, a label in both languages, a kind, a unit, the aliases a source calls
  it by.
- What it is for: an axis of variants, a facet, searchable, shown in pictures.
- For an option: its options, each with a label, a swatch and the options it
  may be mistaken for.
- **Global**: `MATERIAL` means one thing for the whole catalogue.

**Category** (`Category.cs`)

- A code, a name in both languages, a parent, its whole path.
- **The attributes it declares**; a product has those of its whole branch (ADR
  0056). Offered, never enforced.

Categories and attribute definitions are read from the seed's files today, not
from the database, and nothing in the backoffice edits them.

## Beside the others

Amazon and Shopify from their public documentation; Zalando from its partner
API as remembered, to be checked before any of this is printed in an article.
Akeneo stands for the PIMs.

| | Tendero | Shopify | Amazon | Zalando | Akeneo |
|---|---|---|---|---|---|
| **What says which attributes a product has** | Its category, inheriting from the ancestors | The category of the standard taxonomy | The product type, a formal schema each | The article's type, a schema each | The family |
| **Navigation** | The same tree | Apart: collections | Apart: browse nodes | Apart | Apart: category trees |
| **Required attributes** | None: only *offered* | None | Yes, by product type | Yes | Yes, by family and channel: completeness |
| **Levels of variant** | One: a flat list on axes | One: up to three options | Two: parent and children, with a variation theme | Three: model, colour, size | Up to two, and the family says what varies where |
| **Pictures** | The product's gallery; one a variant | The product's media; one a variant | Several for each child, with roles | For each colour | Assets, at whichever level the family says |

**Where we stand with them.** Shopify is the nearest: flat variants on axes, a
gallery on the product, one picture a variant, attributes hung on the category
and inherited. ADR 0056 is its shape. Typed attributes with localized options,
and the SKU as the key Inventory and Pricing share, is what everybody does.

**Where we fall short**, in the order it hurts:

1. **Several pictures of a colour.** In fashion a colour has five or six
   pictures and every size of it shares them. Here a variant points at one, and
   a shoe in blue and eight sizes says the same id eight times.
2. **No required attributes.** Nothing can say *this product lacks its
   material*, and there is no completeness by category — the first thing a PIM
   offers.
3. **One tree for two things.** Type and navigation are the same node. Fine at a
   hundred products; it breaks the day a section crosses types (*gifts*,
   *sale*).
4. **An attribute lives on the product or is an axis.** A variant cannot hold
   one of its own that is no axis: a barcode, a weight by size.
5. **Pictures have an order and no role**: no *main*, *swatch*, *in use*; no
   video.

This document proposes the first two. The other three are real and nothing asks
for them yet; they are named so that the first two are not built in a way that
closes them.

---

## Proposal 1 · A picture says what it shows

### The problem, concretely

`Variant.Image` is one id. Three things follow:

- **One picture a colour.** A second view of the navy shoe has nowhere to say it
  is the navy one's: it is a picture of the gallery and of no variant.
- **The same fact written once a size.** Navy in eight sizes is eight variants
  with the same `Image`; change the picture and eight rows change, or seven.
- **The question is asked backwards.** What the shop wants to know of a picture
  is *what is this of* — to show it when navy is chosen, to hold it to navy
  when a model reads it (I-4d). The model stores the opposite, *which picture
  does this variant have*, and `ProductPicturesReader` turns it round: the
  variants whose `Image` is this one, plus, for the cover, those that have none.

### The change

**A picture says which of the product's axis values it shows**, and a variant
has no picture of its own.

```
ProductImage(Id, Alt, SortOrder, Shows)        Shows: { COLOR: NAVY_BLUE } · or nothing
```

- `Shows` names values of the product's axes — usually the colour alone. A
  picture that says nothing shows the product and no variant in particular: the
  back, a detail, the box. That is the picture the pannier's fourth already is.
- **A variant's pictures are derived**: those of the gallery whose `Shows` its
  own coordinates satisfy, in the gallery's order; with none, the product's
  untagged ones. Its *picture* — the one a cart line and a search result show —
  is the first of those.
- **A colour's gallery comes free**: every picture that shows navy, then the
  ones that show nothing. That is what the product page shows when navy is
  chosen, and what Zalando keeps a level for.
- `Variant.Image` goes.

Why this and not the alternatives:

- **A list of pictures on each variant** keeps the duplication a size and adds
  an order to keep in step with the gallery's.
- **A level between product and variant** (Zalando's colour, Akeneo's first
  variant level) is the general answer and a much larger one: the cart, the
  order line, stock and tariffs all speak SKU today and would each learn a new
  thing. A tag on the picture gives what that level is wanted for here — the
  pictures of a colour — and leaves the level possible later.
- **One fact, in one place**: which variant a picture is of is said by the
  picture. *It belongs to another variant* — the third decision on a doubt —
  becomes changing one tag, which is why that decision can then be applied from
  the doubt itself.

### What it touches

| Where | Today | After |
|---|---|---|
| `Product`, `Variant` | `Variant.Image`, `SetVariantImage` | `ProductImage.Shows`, `Show(picture, values)`; `PicturesOf(variant)`, `PictureOf(variant)` |
| Persistence | a column on the variant | `shows` inside the gallery's JSON; a migration that turns each variant's `Image` into a tag of its colour on that picture, and drops the column |
| Import (`ExternalVariant.Image`, added this week) | a variant names a place in the list | an image says what it shows (`ExternalImage.Shows`); the seed's `variants[].image` becomes `images[].shows` |
| `GetProduct`, the cart, search's variant document, UCP's media | `variant.Image ?? cover` | `product.PictureOf(variant)` — one method, four callers |
| `ProductPicturesReader` (I-4d) | turns the model round | reads the tag: a picture's colour is what it says it shows |
| Storefront product page | swaps one picture on a colour | shows that colour's gallery |
| Backoffice | — | on the product's pictures, which colour each shows (the variants screen has the options) |
| `tools/SeedImages` | a variant's picture is an entry named by its file | the same, read from the picture's tag |

Not touched: the SKU, stock, tariffs, orders, the claims, the doubts' own
record.

### What it costs and what could go wrong

- A migration over data that is one seed product with three colours and
  whatever the author's dev database holds: small, and reversible on paper —
  a tag back into a column — only while every tag is a single colour.
- The public API's product response gains pictures by variant; the contract
  test and the generated types move with it.
- **A picture tagged with a colour no variant has** shows on no variant. The
  aggregate refuses a tag that names no axis of the product or no value a
  variant holds; removing the last variant of a colour leaves the tag to be
  cleared, and that is a rule to write, not to discover.

### The author's to rule

1. Go with the tag on the picture, or the level between product and variant?
   **Ruled 2026-10-08: the tag.** The level is deferred for a later phase —
   ADR 0057 says why, what would bring it back (a colour needing a URL, a
   price, a description or a search result of its own), and that a tag is
   already the data such a level would be built from.
2. A picture may show **one** value of an axis, or several (*navy and black
   together*)? One is simpler and is what every catalogue picture here is.
   **Taken: one.**
3. With pictures of a colour **and** untagged ones, does a colour's gallery end
   with the untagged, or are those shown only when the colour has none of its
   own? Proposed: it ends with them — a detail of the sole is every colour's.
   **Taken: it ends with them.**

### Built (2026-10-08)

The aggregate, persistence with its migration, the four read paths, the import,
the seed and the picture tool: `Variant.Image` is gone and nothing reads a
variant's picture except through `Product.PictureOf` and `PicturesOf`. A
variant in the API keeps `imageId` and gains `imageIds`. The storefront's
gallery is the chosen colour's, and the backoffice says which colour a picture
shows, beside the picture, on the product's review (`SetImageShows`). Still to
build: nothing. *It belongs to another variant* is applied from the doubt
(ruled and built 2026-10-08): the decision travels the outbox to Catalog's own
command, which files the picture under the colour seen.

---

## Proposal 2 · A category says what a product must have

### The problem, concretely

ADR 0056 lets a category declare attributes, and declares them *offered, never
enforced*. That narrowed what an extractor is offered and what a picture is
asked. It cannot say that a pan with no material is **incomplete**: every
declared attribute is equally optional, so the catalogue cannot tell a product
that is finished from one that arrived half described, and the review queue
shows a draft without saying what it lacks.

Amazon refuses a listing that lacks a required attribute of its type. A PIM
does not refuse: it shows a completeness, by family and channel, and lets
publishing be held behind it. The second is the one that fits a shop whose
imports arrive wrong.

### The change

**A declaration carries how much it is wanted.**

```
categories.sample.json     "attributes": [ { "code": "MATERIAL", "required": true }, "CAPACITY_L" ]
```

- Two levels: **required** and (the default, as now) **expected**. A plain code
  stays valid and means expected, so nothing declared today changes meaning.
- Inherited like the declaration itself; a category below may raise an
  ancestor's *expected* to *required* and never lower it.
- **Still never enforced on import**: a product missing a required attribute is
  imported, kept and shown — as *incomplete*.
- `CategoryTree.RequiredOf(category)`, and on the product
  `MissingRequired(tree)`: the required codes it holds no value for. An axis
  counts as held when its variants hold it.

What reads it:

- **The catalogue's gaps** (`CatalogueGaps`, which Steward and the dashboard
  already read): a new kind, *missing a required attribute*, beside missing
  name, picture and alt text.
- **The review queue**: a draft says what it lacks before it is published.
- **Publishing**: see the ruling below.
- **The claims**: a claim that would fill a required attribute the product
  lacks is the most useful one in the queue, and can be shown first. The
  extractor is not told — it is offered the same vocabulary — code orders.
- **Pictures**: nothing. What a picture is asked is still what its category
  declares and a picture shows.

### What it touches

| Where | Change |
|---|---|
| `Category` | a declaration is a code and a level; `Attributes` keeps returning the codes, so its callers do not move |
| `CategorySeedFile`, the seed | reads either form; the seed marks what is required |
| `CatalogueGaps` | a kind, its query, its test; the dashboard's and Steward's wording in both languages |
| Review queue, product detail in the backoffice | what a draft lacks |
| ADR | 0056 amended, or 0057: *required is a level of the declaration, and incomplete is not refused* |

No migration: categories are read from their file.

### The author's to rule

1. **Does an incomplete product publish?** Proposed: yes, with the gap shown —
   *offered, never enforced* stays whole, and a shop decides what it sells. The
   other answer, Amazon's, is a guard on `PublishProduct`, one line and a
   different product.
2. **Which attributes are required**, per category. Proposed to start from what
   every seed product of the category already holds — the seed then starts
   complete, and the first incomplete product is a real one — and for the
   author to prune.
3. Two levels, or three (*required, recommended, optional*, as Akeneo's
   completeness and Google's feed have)? Proposed: two. A third level is a
   number nobody acts on until there is a channel that needs it.

**Taken, all three as proposed** (2026-10-08, the author's *go ahead with
proposals 1 and 2*): an incomplete product publishes with what it lacks shown,
required starts from what every seed product of the category holds, and there
are two levels.

### Built (2026-10-08)

ADR 0056 carries the amendment. `Category.Required`, `CategoryTree.RequiredOf`
and `Product.MissingRequired`; the seed's declarations read as a code or as a
code and its level; the gap `MissingRequiredAttribute`, each row saying what it
lacks in the reader's language; the review's note before publishing.

**What the rule gave on the seed** — an attribute a category declares, and how
many of that category's seed products hold it:

| Category | Required (all hold it) | Left expected (nearly all) |
|---|---|---|
| Clothing | colour 32/32 | size range 30, material 29, gender 26 |
| Bags | material 13/13 | colour 12, capacity 11 |
| Coffee makers | cups 5/5 | power 2 |
| Desk lamps | colour temperature 11/11 | power 10 |
| Knives | pieces 3/3 | — |

Five required, and no seed product incomplete. **Then the author took three of
the middle column** (the same day): a bag's colour, a lamp's power, a garment's
material. Eight required, and five products incomplete — the packing cubes
(three colours, and so none), a ceramic table lamp (no power), a tech tee and
two pairs of boots (no material; one is *leather ankle boots*). The gap and the
review's note now have something to show.

Not built: a backoffice screen that edits the declarations; ordering the claims
queue by what would complete a product; publishing held behind completeness —
a ruling, not an omission.

---

## Order, and what it does to the phase

**Proposal 1 first.** It changes a contract added this week
(`ExternalVariant.Image`, the seed's `variants[].image`) and the reader the
colour check stands on; every picture and every doubt made before it is made on
the model that is going away. It is also the larger: the aggregate, a
migration, four read paths, the storefront's gallery and a backoffice control.
A plan of its own (`docs/plans/`), in slices: the aggregate and the migration;
the read paths behind one method; the import and the seed; the storefront; the
backoffice; then the doubt's third decision applied from the doubt.

**Proposal 2 after**, and it is small: an optional field, a derived list, one
new gap. It amends an ADR written this week, which is the right time to amend it.

Both before the catalogue grows by its twenty products in new families: those
bring colours with several pictures and categories with attributes of their
own, and should be born on the model they will live on.

**Left as they are, named so they stay possible**: navigation apart from type
(a second tree, or collections, over the same products); an attribute of a
variant that is no axis; a picture's role and kind.
