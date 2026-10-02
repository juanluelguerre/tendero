# 0052 — Assist is a context: a proposal has a life of its own

**Status**: accepted (the author approved the Steward spec on 2026-10-02, which proposed it)

## Context

CLAUDE.md said `Mcp`, `Ucp` and `Assist` are not contexts because they own no
state. Steward's proposals changed that for Assist: an `ActionProposal` has a
lifecycle (`Proposed → Approved → Applied | Failed`, `Rejected`, `Stale`), a
writer (the proposal tools), and actors of its own (whoever asked, whoever
decided). ADR 0014 says that is how a context earns its boundary.

A proposal runs another context's command. If Assist referenced Catalog to do
it, every new proposable command would add a reference, and Assist would learn
what a product is.

## Decision

1. `Assist` is the seventh context. It owns `ActionProposal`, references the
   SharedKernel and nothing else, and an architecture rule computes that.
2. A context makes one of its commands proposable by implementing
   `IProposableCommand` — through `ProposableCommand<TPayload, TCommand, TResult>`
   — and registering it. The entry checks a payload with the command's own
   validators, snapshots what the command would change, and runs it through
   `ICommandDispatcher`, so a proposal goes through the validation, the audit and
   the handler a person's click does.
3. Approving runs each command in the same request as the approving person, in
   a DI scope of its own, and the audit row of that command carries the
   proposal's id (`AuditEntry.Proposal`).
4. Approving or rejecting is never proposable; a rule reads every entry's command.
5. A snapshot of "now" is JSON and is compared by value. It is stored in jsonb,
   which keeps neither the text's spacing nor its key order: compared as text,
   every proposal read back from Postgres was stale.

## Consequences

- Adding a proposable command is a class and a registration in its context.
- The audit can say who proposed a change and who approved it, without joining.
- A proposal stores values (the command's name, its payload, the subject's
  code), never a reference into another context (invariant 1).
- A proposal carries a version (Postgres's xmin): of two approvals of one
  proposal, only the first save stands, and the other is told it is no longer
  waiting, so a double click runs a command once.
- Retrying an approval whose result is unknown is allowed only once it is older
  than an approval can take (five minutes), and it skips the staleness check,
  which relies on the commands being idempotent; a proposable command that is
  not idempotent would need this decision reopened.
