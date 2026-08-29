# Tendero — design system

One token file (`design/tokens.css`), four surfaces. The tokens never change per
surface; what changes is **density and voice**.

## Direction

A corner shop, not a SaaS dashboard. Clay awning, stone counter, chalkboard ink,
olive crates. The system is quiet on purpose — hairline borders, modest radii,
almost no shadow — so that the one loud element earns its place.

**Signature: the awning stripe.** A repeating clay/blush rule taken straight from
the logo. It marks section breaks on the landing, the active tab in the
backoffice, the loading bar during import, and the left edge of the docs sidebar.
It is the only decoration in the system, and it always means Tendero.

## Type

| Role | Face | Where |
|---|---|---|
| Display | **Bricolage Grotesque** 800 | Headlines, hero, section titles, wordmark |
| Body | **Instrument Sans** 400/500/600 | Everything else in the UI |
| Utility | **JetBrains Mono** 400/500 | Prices, SKUs, order ids, metrics, latencies, code |

Two decisions worth defending:

- **No serif display.** The warm-cream + high-contrast-serif + clay-accent
  combination is the house style of AI-generated design right now. Bricolage is
  a variable grotesque with genuine quirk in its terminals — characterful without
  landing in that cluster.
- **Numbers are monospaced everywhere.** Prices on tags, totals on receipts,
  metrics on dashboards: commerce runs on figures in columns. `font-variant-numeric:
  tabular-nums` means a price column actually aligns. It is the market vernacular
  doing real work, not a stylistic flourish.

Uppercase appears in exactly one place: `.eyebrow` micro-labels. Everything else
is sentence case, including buttons and headings.

## Surfaces

### Landing (`tendero.dev` or GitHub Pages)
Light, generous, `--text-md` body at `--container-prose`. Hero opens with the
thing that makes the project unusual — a live search box querying the demo
catalog — not a screenshot carousel. Awning rule under the hero and between
sections. Max one page of scroll before the "run it locally" command block.

### Docs (`docs/`, rendered with VitePress or Astro Starlight)
Prose measure capped at `--container-prose`; body `--text-md`, line height
`--leading-body`. Sidebar carries a `.awning-rule--thin` on its right edge.
Code blocks in JetBrains Mono on `--stone-50` with a hairline border, no
syntax-highlight theme heavier than two accent colours. Mermaid diagrams inherit
`--ink-900` / `--clay-500`.

### Storefront
Roomy: `--text-md` body, `--space-6` between blocks, product cards on
`--bg-surface` with `--radius-lg` and a hairline border, no shadow until hover.
Prices in `.price` (mono, tabular). Images do the selling, so chrome stays
minimal — the accent is reserved for the primary action on each screen
(add to cart, checkout) and nothing else. One clay button per view.

### Backoffice
Dense: body drops to `--text-sm`, tables to `--text-xs`, row height 36px.
Dark mode enabled here by default (`data-theme="dark"`), light elsewhere. Status
is colour-coded and always paired with a word, never colour alone:

| State | Token | Used for |
|---|---|---|
| Active / passing / in stock | `--positive` | Published products, green CI, NDCG above threshold |
| Needs review / pending | `--warning` | AI-generated copy awaiting approval, draft products |
| Failed / rejected | `--danger` | Import errors, declined payments, cancelled orders |
| Agent activity | `--accent` | Anything a UCP agent did, so it is instantly separable from human actions |

That last row is the one worth copying: **agent-driven actions are always clay.**
Scanning the orders table, you can see at a glance what an agent bought.

## Rules

- Never write a hex outside `tokens.css`. If a component needs a colour that does
  not exist, that is a design decision, not a CSS fix.
- One accent action per view. If two things are clay, neither is primary.
- Shadows only on things that genuinely float (menus, modals, toasts).
- Colour never carries meaning alone — always pair with text or an icon.
- Every interactive element has a visible `:focus-visible` ring. No exceptions.
- Respect `prefers-reduced-motion`; the awning bar is the only thing that animates.

## Copy voice

Plain, active, sentence case. The interface speaks like a shopkeeper, not a
system: "Add to cart", not "Submit". "Nothing here yet — import a catalog to get
started", not "No records found". An action keeps its name end to end: the button
says *Publish*, the toast says *Published*.

Errors say what happened and what to do: "Import failed for 12 products — open
the error log to see why". No apologies, no vagueness.

Everything user-facing exists in Spanish and English. Copy is written in English
first, translated to Spanish, and both live in the Transloco files — never
hardcoded in a template.
