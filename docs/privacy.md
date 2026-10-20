# Privacy: what the shop keeps about a shopper's browsing

This note covers one piece of personal data: the **trail** behind *Sigue donde lo
dejaste* and *Tu historial* (R-8, `docs/specs/storefront-home.md`). Orders,
accounts and the audit log are covered where they are built; this is the first
data the shop keeps about what somebody *looked at* rather than what they did.

## What is kept

| Who | Where | What | How long |
|---|---|---|---|
| A guest | the browser's `localStorage`, on that device | the products viewed (by public code) and the searches made, with when | until the browser clears it, or the shopper does; **never sent to the shop** |
| A signed-in customer | Accounts, `accounts.browsing_histories`, one row per customer | the last **50** products viewed (public code and time) and the last **20** searches (the words and time), each once, most recent first | **90 days** from each entry |

Nothing else: no product names, prices or pages, no device, no location, no
referrer. A product is its public code — Accounts never learns what the product
is — and a search is the words as typed, with the spacing tidied.

## How long, exactly

An entry older than 90 days is never shown: the aggregate (`BrowsingHistory`)
filters it when the trail is read and drops it whenever the trail is written.
For a customer who never comes back, `ForgetOldBrowsingWorker` sweeps the table
every six hours and removes every entry past the 90 days in one SQL statement,
so an entry outlives its retention by six hours at most.

## Who can read it

Only the customer's own session. Every route under `/api/accounts/me/history`
requires the `Shopper` policy — never anonymous, never an agent, not even one
acting for the customer — and the customer is the caller: no route or body names
a customer. `EndpointAuthorizationTests` holds the policy on every route.

It is not shared, not used for advertising, and nothing is inferred from it. The
suggestions it feeds (*Puede que te guste*) are computed on request from the
codes the page sends and are not stored.

## What the customer controls

- **Remove one entry** — `DELETE /api/accounts/me/history/{kind}/{key}`.
- **Remove everything** — `DELETE /api/accounts/me/history`.
- **Switch it off** — `PUT /api/accounts/me/history/settings {"enabled": false}`.
  What was kept is cleared in the same change, and nothing is recorded — nor
  merged from the browser — until it is switched on again. The storefront stops
  writing the guest trail too.
- **At sign-in**, the browser sends its guest trail once
  (`POST /api/accounts/me/history/merge`) and clears it locally, so there is one
  trail, not two.

## What the logs keep

Every command that changes something is audited (who, what, when —
`AuditStep`): the audit row says that a customer merged, removed, cleared or
switched off their trail, and when. **Recording** a view or a search is not
audited at all (`IUnauditedCommand`): it is the customer's passage, not a change
to the shop, and a row per click would keep what every customer looked at in a
log that exists for something else. **The codes and the words are redacted**
(`[AuditRedacted]`) — an audit log that kept what the customer deleted would make
"delete" a lie. The OpenTelemetry spans carry counts and the kind, never a code or
a search.

## What Steward reads, and what may leave the machine

Steward, the backoffice copilot (phase 12), answers from six reads the contexts
own (`docs/specs/steward.md`, *What Steward reads*). They carry **aggregates and
products only**: product codes and names, SKUs, units, revenue, days of cover,
return reasons. They never carry a customer's name, email, address or id, an
order id, or a return's comment — the shopper's own words — and a test holds the
returns summary's types to it.

With the local model (`steward-model`) nothing leaves the machine. **With a hosted
profile** (Gemini, DeepSeek — S-7), those figures and the shopkeeper's question are
what is sent to the provider, along with catalogue text: product names and
descriptions. Conversations are not kept; each model call leaves one row in
`telemetry.ai_invocations` with its tokens, cost and latency, never the prompt or
the answer (ADR 0053).

A turn (S-3b) sends the model the conversation the shopkeeper's browser posts,
the screen it was asked on, and the tools' answers — each inside a `{ data, rows,
total }` envelope that marks catalogue text as data — and nothing else. Every
tool's answer type is checked by an architecture rule for any field that names a
customer, an order, an address, a phone or a comment (ADR 0054).

## What Tino reads, and what may leave the machine

Tino, the storefront's assistant (phase 12b, `docs/specs/tino.md`), answers from
four reads of the shop: its search, a product's page, a product's specifications
with the claims a person approved, and the return policy. **Everything they carry
is what the storefront already shows to anyone**: products on sale, their text,
their prices. They carry nothing of the person asking.

A fifth read, `my_orders`, is about the person asking and nobody else: **their
own orders**, found by who is signed in, or — for a guest — the one order on the
screen they asked from, whose id their browser sent beside the question. What it
hands the model is sentences: when the order was placed and delivered, its
status, what it holds, its total, and what can still be returned and until which
day. **Not the order's id** — the model sees `order1` — and no name, email, phone
or address: an architecture rule holds a shopper's reads to that list (ADR 0054,
amended). It takes no order number, so one typed into the chat reads nothing.

Anyone may ask, signed in or not. **The conversation is not kept**: it travels
with the browser, turn by turn. What stays is one row per model call in
`telemetry.ai_invocations` — tokens, cost, latency, the prompt's version — with
the person's subject when one is signed in and nothing when nobody is, and never
the question or the answer. The question's text is not sent as telemetry either.

**With the local model nothing leaves the machine**, and Tino runs on nothing else:
no hosted profile is configured for it. One would receive the shopper's questions
and catalogue text, and — once orders are read — a signed-in shopper's order
status; that is a decision the author takes with this page in front of them, not
a default.

## A question a shopper speaks

Speaking to the shop (phase 14, `docs/specs/voice.md`) takes a short spoken
question from anyone, signed in or not, for Tino's composer and the search box on
a phone. **It is never kept**, and the words are written into the box unsent: the
shopper sees them before anything is asked.

| What | Where it goes |
|---|---|
| The audio | memory, for the length of one request: read, sent to the shop's own whisper.cpp server, transcribed, discarded. Never a store, a file, a log or a trace |
| What the request leaves behind | a span with four facts: the engine, the language, how many seconds the question lasted, and whether anything was heard. No audio, no words, no hash of the audio |
| The words | the response to the browser, and nothing else. They are a question only once the shopper sends them |

**The engine is the shop's own**: whisper.cpp runs beside the API, and the audio
does not leave the machine. The browser's own speech recognition is used only
where it recognises on the device (`processLocally`). Where it would send the
audio to the browser maker's servers, it is not offered (the author's ruling of
2026-10-16).

## A picture a shopper searches with

Searching by picture (phase 13a, `docs/specs/images.md`, *I-6*) takes a
photograph from anyone, signed in or not. **It is never kept.**

| What | Where it goes |
|---|---|
| The picture's bytes | memory, for the length of one request: read, turned into the model's 224 by 224 pixels, embedded, discarded. Never the image store, a file, a log or a trace |
| Its metadata — where and when it was taken, the device | **not read**. The only thing taken from it is which way up the picture is; nothing else in it is parsed, and nothing of it is written anywhere |
| What the request leaves behind | a span with three facts — the picture's width, its height and its format — how many products were found, and how near the nearest was. No pixels, no file name, no hash of the picture |
| The vector the model made of it | memory, for the same request; it is compared and dropped, and never joins the catalogue's collection |

**The model runs inside the shop's own process**, on its own CPU: the picture is
not sent to a model server, hosted or local. Nothing about it leaves the machine.

The storefront makes the picture small in the browser before sending it —
longest side 1,536, redrawn as a JPEG — so what travels is a redrawn copy
without the file's metadata, and the original never leaves the device. The
picture shown beside the results is read from the page's own memory and is
gone with the tab. None of the server's behaviour above depends on that: the
endpoint can be called by anything, so what it does with a picture is decided
on the server.

### The objects in it, and the camera (the camera search)

Since `docs/specs/camera-search.md` (CS-6, CS-7), a picture is also asked
**which objects it shows** (`POST /api/search/by-image/objects`): the same
rules as above — read in memory by a detector in the shop's own process,
discarded, a span with its size and how many objects, never the pixels — and
the answer is boxes and kinds, nothing more. The browser then cuts each object
out and searches that crop, one request each, under the same rules.

- **A part marked is the only part that leaves the device.** Marked before
  sending, the browser cuts the box out and sends that crop alone: the face
  above the t-shirt, the room round the lamp, never travel.
- **The camera** is the browser's own, asked for only when the shopper presses
  *Take a photo* on a phone, and only for that moment: the stream is shown in
  the page, stopped the instant a frame is frozen or the viewfinder is closed,
  and nothing of it is sent but the frame the shopper chooses to use. Refused
  or absent, the phone's own camera app is opened instead, which hands the page
  a file as choosing one does. The browser remembers the permission, not the shop.
- **What is counted**, with analytics consent only (`search_by_image_submitted`):
  how the picture was given, whether a part was marked, how many objects were
  found and how many of them are of a kind the shop does not sell, how many
  products came back, and whether words narrowed them — **never the picture,
  its boxes, which kinds, or the words**.

What is counted is who asked how often — twelve visual searches a minute for a
client, by the person when one is signed in and by the address when nobody is,
and twelve photographs a minute asked for their objects — and that count is not
kept either: it lives in the process and is gone with the minute.

Words asked against the pictures (`GET /api/search/pictures`) are a search like
any other: the words are embedded in the same process and are not recorded by
the endpoint; a signed-in customer's own history keeps a search only where the
storefront says so, above.
