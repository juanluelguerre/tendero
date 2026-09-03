# ADR 0027 — The audit log writes outside the command's transaction

**Status:** accepted · 2026-09-03

## Context

Audit was the last **Missing** row in the floor, and three later features read
it: the audit screen (phase 7), the agent activity panel (phase 11) and the
backoffice copilot (phase 12). One writer, three readers — which is why it is a
dispatcher decorator rather than logging sprinkled through handlers. A handler
that has to remember to audit is a handler that will one day forget, and the
forgetting is silent.

The decision that is not obvious is not *where the code goes*. It is **which
transaction the row belongs to**, and getting it wrong produces a log that looks
complete and is exactly inverted.

## Decision

**The audit entry is written on its own `DbContext`, from a factory, after the
handler returns — outside whatever transaction the command used.**

The obvious alternative is to reuse the scoped `TenderoDbContext`, so the audit
row commits atomically with the command. It is wrong here, and the reason is the
whole point of an audit log:

> **An audit row inside the command's transaction disappears exactly when the
> command fails.** The log would then hold every success and no refusal — and
> the row you most want is the one whose transaction rolled back.

A shop whose argument is that saying *no* is the interesting part cannot keep a
log that records only *yes*.

Three consequences follow, and each is a rule:

1. **Outcome is a three-value enum**, not a boolean. `Denied` is separate from
   `Failed` because a rule refusing is the system *working* — "cannot publish an
   archived product" belongs beside an agent denial, not beside the database
   being down. Phase 11's panel opens on the denials, and an implementation that
   filed them under "error" would make it unbuildable.

2. **Validation failures leave no row.** A command rejected by FluentValidation
   never reached a rule; it reached a form check. Recording those would fill the
   table with mistyped requests and bury the rows three features are built on.
   The pipeline order — validate, audit, handle — is what enforces it.

3. **Queries leave no row.** A query is not an action. Auditing reads would bury
   the hundred rows that matter under a hundred thousand that do not.

**Credentials are redacted, not stored.** `[AuditRedacted]` on a property
replaces its value with a marker. This is not hypothetical: the guest claim and
checkout both carry a **cart token**, a 256-bit bearer credential, and an audit
table is exactly the kind of thing that gets exported to a spreadsheet. The value
is replaced rather than dropped, so the row still shows the field was there.

**A failing audit writer never fails the command.** It is the one deliberate
exception swallow in the repository, and it is deliberate for a concrete reason:
the command already happened, so failing the request now would tell a shopper
their order did not go through when it did. The loss is tagged on the span,
so it is visible in a trace rather than nowhere.

## Consequences

**The cost is stated rather than hidden.** A process that dies between the
command committing and the audit write returning loses a row, and there is no
outbox behind it. An audit log that is complete about refusals and occasionally
short of a success is the better of the two failures. A second outbox would buy
durability for a table nobody reads in real time, and it is deferred with that
reason rather than because it is hard.

**The separation is the context INSTANCE, not the DI lifetime**, and getting
that backwards was caught by a test rather than by review. The factory was first
registered as a singleton — reasoning about the outbox worker, which has no
request scope — and `AddDbContext` registers its options as scoped, so the
container refused to construct it at all. `WorkerContainerTests` is the fifth
composition failure this repository has had and the second one caught before a
person found it.

**Nothing is required to have an audit writer.** A process with none runs
commands and says nothing: `tools/SearchEval` composes its own container and has
no database, and a pipeline step that demanded one would have been the sixth
composition failure.

## Related

- ADR 0024 — the outbox is the process manager. The audit log deliberately does
  **not** use it: durability there buys nothing a reader would notice.
- ADR 0022 (reserved) — an agent is a principal, not a customer. The entry
  carries both, which is what makes "who acted, and on whose behalf" answerable.
