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

**Amended 2026-09-03.** That last sentence scoped this ADR to the API and left
the storefront's half undecided, and the language switcher made the gap real: the
active culture lives in a signal and in `localStorage`, so one URL serves two
languages. Every serious commerce site puts the locale in the URL — `/es/`,
`/en/`, or a per-country domain — for the three reasons this ADR already gives
about the parameter: a shareable link, a cache key, and a reproducible page. To
which HTML adds a fourth the API does not have: a search engine needs a distinct
crawlable URL per language plus `hreflang` between them.

It is **not** fixed here, and the reason is that it is half a job on its own. The
query is not in the URL either, so a shareable link to a results page needs
`?q=` as much as it needs `/es/`; and without SSR a locale segment buys
shareability and no indexing at all. Both, plus `hreflang`, land with the product
detail page — see `P5-13` on the board. The per-culture slugs the catalogue
already generates and stores are waiting for exactly that.

**Amended 2026-09-08 — the third caller, and it did not need a new rule.**

The MCP server is the first non-HTML, non-browser client, and the roadmap
assumed it would need a variant of this chain: *"There is no `Accept-Language`,
so the chain becomes explicit tool parameter → session default captured at
`initialize` → `es`."* That was written with stdio in mind, and the transport
actually chosen is **streamable HTTP**. The client's POST carries whatever
headers it sets, so the middle link is not missing and does not have to be
invented. The tools call `CultureNegotiation.Resolve` with the same two
arguments an endpoint passes it — **the same chain, not an analogue of one**. A
client that sends no header, or a future stdio deployment where there is no
request at all, falls to `es`, which is the chain working rather than a gap in
it.

Two things do change, and both follow from JSON-RPC having no headers:

- **`Content-Language` becomes a field.** Every localized tool answers inside
  `Answered<T>`, which carries the negotiated culture beside the payload. The
  payload itself stays the query's own result, unmapped: this is one header
  becoming data, not the transport minting a contract of its own. It matters
  most exactly when it is least expected — an agent asking for `fr` is answered
  in Spanish, and without the field it would have to guess the language from the
  words.
- **`Vary` has no analogue and is not needed.** It exists so a shared cache
  cannot serve the wrong language; a JSON-RPC POST is not cached by anything in
  between.

Per-item truth needed nothing new: `ProductDetail.MissingCultures` already
travels, so `get_product` can answer *in* English and name English among the
languages that product has not been translated into.

One consequence worth writing down because it is invisible until it bites: **a
tool's `culture` parameter defaults to `null`, not to `"es"`.** A default of
`"es"` makes "the caller said nothing" and "the caller asked for Spanish" the
same value at the point of decision, and the rest of the chain can never run.
The first version of these tools had exactly that, and the header link would
have been dead code.

## Consequences
Every new endpoint returning localized text owes three things: the precedence
chain, `Content-Language`, and `Vary`. Forgetting the last two is invisible in
development and shows up as a cache serving the wrong language.

The chain was **copied** in two endpoints when this was written, on the grounds
that two cases is not a pattern and an abstraction built on two is a guess. The
third endpoint arrived and it moved: `CultureNegotiation` in the SharedKernel is
where it lives now, and it is what made the MCP tools free — a second transport
that agrees with the first by calling the same function rather than by restating
the same rule.
