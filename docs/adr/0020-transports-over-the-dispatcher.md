# ADR 0020 — MCP and UCP are transports over the dispatcher

**Status:** accepted · 2026-09-08

## Context

Phase 9 gave Tendero a second and a third way in: an MCP server at `/mcp` and a
UCP business profile at `/.well-known/ucp`. Both answer questions the HTTP API
already answers — what is in the catalogue, what one product looks like, what is
on offer, how long a return window runs.

The failure mode is well known and it does not announce itself. A second surface
starts as "just a thin adapter", grows one query of its own because a shape did
not quite fit, and ends up a second implementation of the same business question
— reached by a different route, and with the validator, the audit decorator and
the tests attached to only one of the two. Nothing goes red. The two answers
merely drift, and the day somebody notices is the day a customer sees two
different prices.

## Decision

**A transport builds a query record, hands it to `IQueryDispatcher`, and maps the
answer. It implements no handler and it reaches no `*.Domain` namespace.**

Two architecture tests enforce it, and they are written over a `Transports` array
rather than over a project name, so a fourth surface joins the rule by being
added to a list:

- `A_transport_implements_no_handler_of_its_own` — no `IQueryHandler<,>`,
  `ICommandHandler<,>` or `IDomainEventHandler<>` in `Mcp` or `Ucp`.
- `A_transport_never_reaches_a_domain` — the forbidden namespaces are computed
  from the contexts, asserted non-empty so a broken discovery cannot pass
  vacuously, and checked with NetArchTest.

Both were verified by mutation: one `IQueryHandler` declared inside `src/Mcp`
that also reads `ReturnRequest.Window` turns both red, and both name the type.

**Same process, separate assembly.** The process is shared so reuse is free — the
container is built and every handler is registered. The assembly is separate
because a rule needs something to point at.

## What the rule cost, exactly once

`GET /api/returns/policy` answered from `ReturnRequest.Window` inline, with no
handler, and the comment above it said why: a dispatcher round trip around a
field read is ceremony. That was true while HTTP was the only caller.

With a second transport that may not touch `*.Domain`, the choice stopped being
"one line or a round trip" and became **"one source or two copies of a
constant"** — and that endpoint exists precisely because the window had been
written down twice. It has a query now.

That is the rule working rather than the rule being expensive: it converted a
convenience into a duplication the moment a duplication became possible.

## Consequences

A tool is about six lines because the answer already exists in a slice. When it
cannot be six lines, the question has no slice yet, and the answer is a slice in
the context that owns it — not a handler beside the tool, where the validation
step and the audit decorator are not.

Transports still own three things, and all three are protocol rather than logic:
mapping a refusal into what the protocol calls an error (a `ValidationException`
becomes ProblemDetails over HTTP and a JSON-RPC error over MCP), translating a
shape somebody else specified (UCP's Product, in minor units), and saying which
culture an answer is in when the protocol has no header for it.

The rule does not say a transport may add no shapes of its own. It says it may
not answer a question of its own.
