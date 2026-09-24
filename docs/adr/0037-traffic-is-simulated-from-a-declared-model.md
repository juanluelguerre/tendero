# ADR 0037 — Traffic is simulated from a declared model, seeded, and labelled as synthetic

Status: accepted · 2026-09-24 · Related: [ADR 0036](0036-observability-is-a-second-stack-behind-a-switch.md), [ADR 0022 (reserved)](README.md) · Analysis: [`observability-tooling.md`](../analysis/observability-tooling.md) §5–6

## Context

A laboratory has no customers. A real-time dashboard over no traffic is a flat
line, a funnel over no traffic is empty, and a comparison between two browser SDKs
over traffic that differs between runs compares nothing. Tendero needs traffic that
behaves like a shop's — and it has to say plainly that it is not one.

Three activities are easy to conflate: **synthetic monitoring** (a few scripted
checks: is it up?), **load testing** (volume: where does it break?) and **traffic
simulation** (a population following a behavioural model: what would the data look
like?). Tendero needs the third. Large companies get it by replaying production
traffic; with no production, the substitute is a model.

## Decision

**1 · One simulator, in .NET, in `tools/TrafficSim`.** People run in real headless
browsers through `Microsoft.Playwright`, so Web Vitals, JS errors and the browser →
API trace are real. Agents run over their protocols in the same process — the
`ModelContextProtocol` client already in the tree, and HTTP for UCP, with
dev-issuer tokens. One process means one scheduler and one seed. Node Playwright
was weighed and not taken: the e2e suites hold little to reuse, and a test (a fixed
path that asserts) is not a journey (drawn steps that assert nothing).

**2 · The behavioural model is declared, and its parameters carry their source.**
Each persona is a Customer Behavior Model Graph (Menascé): a discrete-time Markov
chain over page types, with transition probabilities and think times. Arrivals are
a non-homogeneous Poisson process following a compressed 24-hour profile. Every
parameter names its source and its grade — Baymard's 70.22 % cart abandonment and
its six reasons as the cart's exit weights, a conversion rate in the published
range — and queries are drawn from the golden set, zero-result queries included.
**The dashboard shows the simulator's assumptions, not the market**, and the
article says so.

**3 · Reproducible.** One master seed per run; each session's seed is derived from
(run seed, session index), so concurrency never changes an outcome. The seed is
recorded on the run's span.

**4 · Labelled, everywhere.** `user_agent.synthetic.type=test` (OTel, Development
status), a User-Agent suffix, `X-Tendero-Synthetic: <run-id>`, and baggage
`synthetic.run_id` / `synthetic.persona` promoted to span attributes and analytics
rows. Agent personas are labelled too: they are real principals with real tokens,
and the label is the only thing that tells a simulated agent from a real one.

**5 · Never on by itself.** An explicit-start Aspire resource in both observability
modes, sized for plausibility, not load. Its real cost per browser session is
measured and recorded when it first runs, per the scale rule.

## Consequences

- `Microsoft.Playwright` (MIT, 1.63.0) is a new NuGet dependency, tier 1, **approved
  by the author on 2026-09-24** (its licence is checked again at the exact version
  added), and downloads its browsers on first use.
- The simulator needs journeys the e2e suites do not have — checkout and payment,
  returns, cart abandonment, publishing a product — which is a reason to add them
  as e2e tests too.
- Synthetic A/B results show that the machinery works (an A/A test must not come
  out significant), not what people prefer — except for search, where simulated
  clicks follow the golden set's judgments.
