# 0056 — A category declares its products' attributes: inherited down the branch, offered and never enforced — and, since its amendment, which of them a product must have

**Status**: accepted (phase 13a, 2026-10-08; proposed to the author on 2026-10-07 after the extractor typed a strap's grey as a lamp's colour temperature, and built on his go-ahead)

## Context

An attribute's definition was global: `POWER_W` meant the same for every product,
which is right, and was *offered* for every product, which is not. Whoever reads a
source for claims — the extractor over a description, the questions a picture is
asked — was handed the whole vocabulary whatever the product was.

Three things were measured before deciding (`docs/specs/images.md`, I-4 and I-4b):

- **A model with no right drawer picks a wrong one.** Offered every attribute with
  a picture's reading, the shop's 4B typed seven colours as `COLOUR_TEMPERATURE`
  and a toaster's slots as `CUPS`: twelve of seventeen typed claims were wrong.
  Narrowing what a picture is offered to what a picture can show took that to two
  of four; the list that did it was two lines in an adapter.
- **A pair is not two pieces.** The question that counts what a picture shows
  counts the two shoes of a pair as two, and `PIECES` is not what a pair has. With
  one vocabulary for all, every pair would have proposed it.
- **A photograph does not show a bed's size.** On ImplicitAVE the same model
  answers *Queen* twenty-seven times of thirty: the mistake is in what was asked.

How others do it, and where we differ: Amazon's product types and commercetools'
product types each carry their own attribute schema; Akeneo separates the *family*
that defines attributes from the *category* that is navigation; Shopify's standard
taxonomy hangs attributes on the category and inherits them from its ancestors.
Tendero's `product_type` already **is** the leaf of its category tree, so there is
one tree, and Shopify's shape is the one that fits with nothing added.

## Decision

1. **A category declares which of the catalogue's attributes are its products'.**
   By code, in the category's own record (`Category.Attributes`), beside its name
   and its place in the tree — catalogue data, in the file the tree is read from,
   reviewed in a diff.
2. **Inherited down the branch.** What a product has is what its category and
   every ancestor declare (`CategoryTree.AttributesOf`): `APPAREL` says colour,
   size, gender and material once, and `SHOES` adds the drop.
3. **Offered, never enforced.** The declarations decide what a product is
   *offered* — the extractor's vocabulary, a picture's questions. They are not a
   rule a product is held to: an import carrying an attribute its category never
   declared keeps it, and `SetProductAttribute` does not ask the category. Real
   catalogues arrive wrong in exactly this way, and refusing the product would
   lose the data that shows the declaration is missing.
4. **A branch that declares nothing has not said.** `AttributesOf` answers null,
   not an empty list, and whoever asks offers what it offered before there were
   declarations. A catalogue with none keeps working; one that is half declared is
   not half silent.
5. **What a picture can show is the attribute's own to say**
   (`AttributeDefinition.IsShownInPictures`). The category says an attribute
   belongs to the product; the attribute says whether a picture shows it. A
   picture is asked the intersection, and is not asked at all when it is empty: a
   shoe's picture is described and not counted.
6. **The seed's products are the measure of the seed's declarations.** A test
   holds that every attribute a seed product carries is declared by its category's
   branch, and that every code a category declares is defined.

## Consequences

- `IAttributeVocabulary.CurrentAsync` takes the product. Its adapter in
  Persistence — which already knew both contexts (ADR 0014) — narrows by the
  product's category, by what the source can say, and by what no source is asked
  (the variant axes, the size range). The hard-coded list of what a picture shows
  is gone.
- No migration: categories and attribute definitions are read from their seed
  files today. The day they are edited in the backoffice and stored, the
  declarations are a column of the category and this decision does not change.
- **Not done here, and each its own piece of work**: facets by category (a facet
  for `CUPS` on a listing of backpacks is the same mistake on another screen); a
  backoffice screen that edits the declarations; a review signal for a product
  holding an attribute its category does not declare.
- The claims suite (`--suite claims`) offers its extractor the seed's whole
  vocabulary, as it did when its floors were set, and that stays its gate.
  **Measured by category the same day** (`--vocabulary category`, twenty
  products, the 4B): precision 0.91 → 0.94, recall 0.82 → 0.85, typed right
  0.88 → 0.94, verbatim 0.984 → 1.000, one more spurious claim. Its own floors
  are owed before it gates.

## Amended 2026-10-08 — a declaration carries how much it is wanted

*Offered, never enforced* could not say that a pan with no material is
**incomplete**: every declared attribute was equally optional, so the catalogue
could not tell a finished product from one that arrived half described, and the
review showed a draft without saying what it lacked
(`docs/analysis/catalogue-model.md`, proposal 2; the author's go-ahead the same
day, on the proposed answers).

- **Two levels: required and expected.** A declared attribute is *expected*, as
  it was; a category may say it is *required*
  (`{ "code": "MATERIAL", "required": true }` in the seed, a plain code still
  meaning expected). Not three: *recommended* is a number nobody acts on until
  there is a channel that needs it.
- **Inherited, and only raised.** `CategoryTree.RequiredOf` is what the whole
  branch requires. A category below raises what an ancestor only expects by
  requiring it; there is no way to say *not required*, so nothing an ancestor
  requires is lowered. A required attribute is declared by being required:
  nothing is required that is not offered.
- **Still never enforced.** A product without a required attribute is imported,
  kept and **may be published** — it is *incomplete*, and said to be:
  `Product.MissingRequired(tree)`, a new kind of catalogue gap
  (`MissingRequiredAttribute`, which Steward's `catalogue_gaps` reads, each row
  saying what it lacks), and a note on the product's review before it is
  published. Amazon's answer — refuse the listing — is a guard on
  `PublishProduct`, one line and a different product: a shop whose imports
  arrive wrong needs to see them, not lose them.
- **An axis counts as held while a variant still sold holds it.** A shirt in two
  colours says no colour of its own.
- **The seed starts complete.** What a seed category requires is what every one
  of its seed products already holds — five attributes today: a garment's
  colour, a bag's material, a coffee maker's cups, a lamp's colour temperature,
  a knife set's pieces — and a test holds it. The first incomplete product the
  shop sees is then a real one. The list is the author's to prune or extend;
  the nearly-universal ones were left *expected* on purpose (a bag's colour,
  12 of 13: the packing cubes are three colours and say none).
- **Evidence of use** (ADR 0036): the gap is read through `catalogue_gaps`,
  whose calls are already counted by tool and argument; nothing new is emitted.
- **Measured, and it found something** (`docs/analysis/steward-models.md`, the
  same day): with the kind named in the tool's description, Steward's suite
  fell from about 24 of 26 to 22 — in every run a case that proposed without
  reading first — and every row of every kind carried an empty `lacks`, 350
  tokens a read. The description went back to its measured words, the kind is
  named in the argument and the enum, the empty field is not written, and three
  runs gave 24, 24 and 25. Tino's suite, whose prompt did not change, did not
  move. **Owed**: a case that asks Steward for the new kind.
