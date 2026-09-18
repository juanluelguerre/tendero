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

## This folder is empty

Nothing has needed the rung yet. It fills when a screen is designed before it is
built and somebody wants to click it — phase 10's claim review queue is the first
candidate.

## The ladder, in this repository

| rung | folder | decides | what it costs now |
|---|---|---|---|
| sketch | [`../sketches/`](../sketches/) | what a person must be able to do | minutes, and prose can do it |
| mockup | [`../mockups/`](../mockups/) | hierarchy and the visual system | ~12 minutes on a canvas |
| prototype | here | whether it survives real data | one export plus one recorded response |
| the code | `frontend/` | everything else | |
