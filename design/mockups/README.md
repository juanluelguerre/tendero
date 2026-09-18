# Mockups

> *"Representación estática y de calidad media que muestra las funcionalidades
> básicas y en color."*
> — *De 0 a N con Task[in]*, chapter 3, 2013

The second rung. It decides **hierarchy and the visual system**. Static, in colour,
and — the part that matters here — built on the real tokens rather than on invented
ones.

## What is in here

| file | what it is |
|---|---|
| `search-evaluation-panel.pen` | the backoffice search-evaluation panel, designed blind against the shipped component as a measured experiment (ADR 0033). 589 nodes, 0 literal colours |
| `design-system.pen` | the system itself laid out for the first time: palette in both themes, type scale, spacing, the awning stripe, components, density |
| `*.html` | the exports. Open one in a browser and you see the screen without installing anything |
| `proposals.json` | colours the system does **not** have yet, each with the reason it exists and its measured contrast ratio |

Both formats are committed on purpose (ADR 0033). The `.pen` is documented JSON, so
a reader opens it in any text editor and sees what a mockup is made of; the HTML is
for a reader who just wants to look at the screen.

## The rule, and it is executable

**`../tokens.css` is the source of truth. A mockup projects it; it never copies
it.** Two design documents in this repository once carried **575 hand-written
hexadecimal colours** and described a product this shop does not sell, and no test
could see them, because the rule that forbids a hex outside `tokens.css` reads
`.css` files.

So the same rule now runs one format further out.
[`design-mockups.spec.ts`](../../frontend/libs/shared/i18n/src/lib/design-mockups.spec.ts)
reads every `.pen` and every `.html` here and holds their colours to the palette
`tokens.css` owns. It needs no application, no account and no container.

## A mockup may propose a colour — declared, never smuggled

A value the system does not have yet is allowed **if a person put it in
`proposals.json` with a reason**. Then:

```
.pen proposes  →  a person approves  →  tokens.css changes
```

never the other way round. It is the shape a `ProductClaim` already has, and it is
not hypothetical: laying the system out found **six contrast failures**, five of
them in the light theme, including white on `--accent` at **3.87:1** on the one
clay action of every storefront view.

## Editing these

pen.dev opens **one document at a time**, and its MCP ignores the file path it is
given — every call lands in whatever document the editor has open. Open the file
you mean to edit before asking an agent to touch it, and check afterwards that the
other one did not grow frames that belong here.
