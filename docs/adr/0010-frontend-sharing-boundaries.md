# 0010 — What the two frontends share, and what they duplicate

**Status**: accepted

## Context
`storefront` and `backoffice` live in one Nx workspace. Monorepos fail in a
predictable way: a `common/` library everything imports, which couples every app
to every other and makes the monorepo worse than separate repos would have been.

## Decision
**The two apps never import from each other**, enforced by
`@nx/enforce-module-boundaries` rather than by convention: each may depend only
on `scope:shared`, `scope:shared` may depend only on itself, and nothing may
depend on an app. It is the TypeScript twin of the NetArchTest rules.

What goes in `libs/shared` follows one rule:

> **Share what is hard because of its BEHAVIOUR.
> Duplicate what is hard because of its IDENTITY.**

A DatePicker is calendar arithmetic, keyboard handling and accessibility —
writing it twice throws away work. A layout is composition and voice: the
storefront shell (roomy, light, commercial) and the backoffice shell (dense,
dark, sidebar with the awning stripe) share nothing but the word, and unifying
them produces a component with a variant matrix that is worse than two
components.

Shared is split by purpose — `tokens`, `ui`, `util`, `api`, `i18n`, and since
phase 0 and phase 6 `auth` and `agent` — never one catch-all, and `shared/` never learns that the apps exist: no imports from
`apps/`, no app-named props, no `if (isBackoffice)`. A primitive that needs to
know who uses it is not a primitive.

Features live inside each app, not in `libs/`. Nx orthodoxy puts most code in
libraries with apps reduced to wiring, but that is for large workspaces; here
the apps share no features by design, so extracting them would be ceremony with
no consumer. A feature moves to `libs/` the day a second app needs it.

## Consequences
A cross-app import fails the lint, verified by mutation. Some UI is written
twice on purpose, and that is cheaper than the variant matrix that avoiding it
would produce. If `shared/ui` ever grows a component that behaves differently
per app, that is the signal it belongs in the apps instead.
