# Sketches

> *"A simple drawing on paper of a general idea, kept short and simple, without
> many details."*
> — *De 0 a N con Task[in]* ("From 0 to N with Task[in]"), chapter 3, 2013,
> translated from the Spanish

The first rung. It decides **what a screen must let a person do**, and nothing
else. It is the cheapest artefact in the ladder, and that is its whole value: a
sketch earns its keep by being thrown away.

## What is in here

Three sketches, drawn with Claude Code on pen.dev in grey, hand-drawn lines and
nothing else. Article 01 (*Design before code*) is about them.

- `claim-review-queue.pen` — **drawn before any of its code existed** (phase 10,
  2026-09-19), and it found three contradictions in the rules for that screen
  that no test could have found, because there was no code to test.
- `storefront-home.pen` and `backoffice-review-queue.pen` — drawn on 2026-09-21,
  **after** the screens they show, to put the first rung next to something a
  reader can compare it with. Each carries its question at the top: what must a
  person be able to do here.

Every colour in them is a design token variable, and `design-mockups.spec.ts`
holds them to that.

**The same three, in Excalidraw** (`*.excalidraw`, 2026-10-02). Redrawn through
Excalidraw's official MCP server, to compare it with pen.dev on the same screens:
open-source (MIT), free, hand-drawn without any trick, and JSON like a `.pen`.
Excalidraw has no colour variables, so every stroke is a literal hex, and
`design-mockups.spec.ts` reads these files too: a hex the palette does not own
fails it. Article 01 tells how the two compared.

## Until 2026-09-21 this folder was empty, and that was the finding

Tendero never drew one. The sketch's job here was done by **prose** — `P8-11` in
[`../../docs/delivery-plan.md`](../../docs/delivery-plan.md) states what the search
evaluation panel must do and must explain, and [`../DESIGN.md`](../DESIGN.md)
states the rules it must obey. It worked: a design agent, given only those two and
the data contract, reached all three of the structural things the panel needed
(`docs/blog/reports/2026-09-18-design-before-code/verdict.md`).

**The sketch rung can be prose. The mockup rung cannot**, because hierarchy is
spatial and words do not carry it.

## The caution this project already paid for

Two design documents from 2026-09-03 were, in their own words, *"wireframes de la
ronda 1 … de otro producto (reparto de barrio)"* — a sketch of a different shop.
A sketch does not protect anybody from sketching the wrong thing. What makes it
useful is that throwing it away is cheap, and what failed there was not the
sketching: **nobody threw it away.** It sat unreferenced for fifteen days.

## What belongs here

A drawing, a photograph of paper, a rough frame — anything whose job is to be
argued with and discarded. If what you have is in colour and built on the tokens,
it is not a sketch: it belongs in [`../mockups/`](../mockups/).
