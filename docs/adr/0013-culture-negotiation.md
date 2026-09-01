# 0013 — How an endpoint decides which language to answer in

**Status**: accepted

## Context
Everything user-facing is `LocalizedText` (invariant 6), so every endpoint that
returns product text has to decide a culture. Two endpoints written a day apart
disagreed about how: `/api/search` resolved query parameter → `Accept-Language`
→ `"es"`, while `/api/catalog/products` did `culture ?? "es"` and never looked
at the header. Neither sent `Content-Language`.

The question behind it is a fair one: `Accept-Language` is HTTP's standard
mechanism for language negotiation (RFC 9110), so why carry a parameter at all?

## Decision
**Both, and the explicit parameter wins.** They answer different questions:
`Accept-Language` says what a client wants *by default*, the parameter says what
*this request* wants.

The parameter has to exist, and has to win, because:

- A URL carrying its language is shareable, linkable and reproducible. With
  header-only negotiation the same URL shows different things to different
  people, which makes incidents miserable to reproduce.
- Each parameter value is its own cache key.
- The header often lies — corporate images in English, borrowed machines, a
  Spanish speaker who reads technical documentation in English — and changing it
  means digging through browser settings.
- **Agents have no browser locale.** Tendero exists to be used over UCP and MCP;
  without an explicit parameter an agent has no way to ask for a language. This
  is the argument that settles it for this project.
- `tools/SearchEval` scores both cultures. Language by header only would make
  the CI gate depend on simulating headers instead of asking for what it wants.

The header still decides when no parameter is given: that is what makes someone
arriving without asking see their language rather than ours. An unsupported
language falls to `"es"`.

**Every response declares `Content-Language` and `Vary: Accept-Language`.** The
`Vary` is not decoration: when culture can come from the header, a shared cache
without it serves Spanish to someone who asked for English.

`Content-Language` states the **negotiated** culture, not each item's. That is a
limit, not an oversight: `LocalizedText.In()` resolves culture → en → first, so
a product with no English requested in English comes back in Spanish, and a
response header cannot say "this row fell back but that one did not". Per-item
truth travels as data — `missingCultures` on each product — which is what the
review queue reads to flag what needs translating.

Query parameter rather than a path segment (`/es/products`) because this is a
JSON API that no search engine indexes. The `hreflang` and distinct-URL advice
comes from HTML and SEO; it becomes relevant if the storefront gains
server-rendered pages.

## Consequences
Every new endpoint returning localized text owes three things: the precedence
chain, `Content-Language`, and `Vary`. Forgetting the last two is invisible in
development and shows up as a cache serving the wrong language.

The chain is currently **copied** in two endpoints. Two cases is not a pattern
and an abstraction built on two is a guess; it moves to a shared model binder
when a third endpoint needs it. Until then the duplication is the signal.
