# Prototypes

> *"Representación del producto final en HTML, PPT, animación o cualquier otro
> formato navegable, con el que el usuario puede interactuar."*
> — *De 0 a N con Task[in]*, chapter 3, 2013

The third rung. In 2013 it decided whether the thing could be **used**. Getting
here from a mockup used to be days of work; today it is one call —
`Export([...], "html-css", …)` — which is most of what changed about this ladder.

## So this project asks a prototype for something harder

The four rungs all grade how finished the artefact *looks*, and they share a
property nobody names: **the data is invented**. A wireframe carries lorem ipsum; a
mockup carries plausible numbers. The mockups in [`../mockups/`](../mockups/) show
`0.831` and `+0.139` because somebody made them up.

That is exactly how this repository's own design artefacts failed: the round-2
documents of 2026-09-03 looked correct and described a different product. **A
failure of data, not of appearance.**

So the rule here is stricter than the rung's definition:

> **A prototype of this shop is fed by a recorded real response from the committed
> contract, not by plausible figures.**

For the search-evaluation panel that means one real body from
`GET /api/search/evaluation`, as a fixture — not a live call, which would make the
artefact need a running stack. It is the first rung that cannot lie.

## What is in here

`claim-review-queue.html` — phase 10's claim review queue, **clickable, before any
of its code exists** (2026-09-21). It was built by Claude Code from
`../sketches/claim-review-queue.pen` and `../tokens.css`, and published as a
private Claude artifact for review. Its HTML lives here so the design tests read
it like any other design file.

It bends the rule above, and says so on the page: this screen has no API yet, so
there is no recorded response to feed it. What it uses instead is the next most
real thing — the seed catalogue's product names, descriptions and every quote,
with each quote's position computed from the real text. The claims, scores and
run are examples. That was enough to find two things the sketch could not: the
sketch had invented its quotes and a measurement ("24 cm" for a 26 cm pan), and
the real catalogue has a wok whose description says carbon steel while its
attribute says stainless steel.

## The ladder, in this repository

| rung | folder | decides | what it costs now |
|---|---|---|---|
| sketch | [`../sketches/`](../sketches/) | what a person must be able to do | minutes, and prose can do it |
| mockup | [`../mockups/`](../mockups/) | hierarchy and the visual system | ~12 minutes on a canvas |
| prototype | here | whether it survives real data | one export plus one recorded response |
| the code | `frontend/` | everything else | |
