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
