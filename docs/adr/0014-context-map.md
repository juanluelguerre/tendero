# 0014 — Contexts share no entities; the count is not the rule

**Status**: accepted · supersedes the *title* of [ADR 0002](0002-bounded-contexts-snapshot.md), not its argument

## Context

ADR 0002 is called "Two bounded contexts", and CLAUDE.md's first invariant reads
"`Catalog` and `Ordering` never share entities". Both were written when there
were two, and both stated the count where they meant the principle.

Phase 3 needed a third. Pricing is not a Catalog concern — a price list is not a
fact about a product, and Catalog would have to learn what a customer segment is
in order to host one. It is not an Ordering concern either — the storefront has
to show tax-inclusive, discounted prices long before an order exists, which would
force `Catalog → Ordering`, the wrong direction. The roadmap has three more
coming for the same kind of reason: `Inventory`, `Accounts`, `Knowledge`.

So the choice was between amending the count every time and saying what the rule
actually is.

## Decision

**A bounded context owns its entities and shares none of them. Contexts
communicate through events and through values, never through references.** The
number of contexts is a consequence of the domain, not a constraint on it.

Concretely, and each of these is already enforced by a test or is the reason an
existing one exists:

1. **No context references another context's domain types.** A crossing carries
   values — a SKU, a price, a name — or an event record, never an entity.
2. **A context may reference a shared vocabulary**: `SharedKernel` and nothing
   else by default. `Pricing` is held to exactly that by an architecture rule
   that computes its references by reflection.
3. **What crosses a boundary is snapshotted, not resolved later.** ADR 0002's
   rule, now stated once for every context: `OrderLine` freezes the product name,
   the applied discount freezes the promotion's code and label, and phase 11's
   order will freeze the mandate scope.
4. **`Search` may reference any context it projects; no context may reference
   `Search`.** That is what ADR 0007 was protecting all along, and it generalises
   cleanly to the stock and specs projections still to come.
5. **One `TenderoDbContext`, one schema per context.** The single context is what
   puts a domain event and its state change in one transaction, which is the
   outbox's entire correctness argument. Configurations split per context, and a
   configuration may only reference its own context's domain.

Modules that own no state — `Mcp`, `Ucp`, `Assist` — are **not** contexts. They
are transports and workflows over slices that already exist, and they get their
own rules (a transport implements no handler).

## Consequences

- CLAUDE.md invariant 1 stops naming two contexts and states the principle. The
  guardrail it carried — *"do not quietly add a context"* — survives in a better
  form: adding one is fine, adding one that shares an entity is not, and a test
  says which happened.
- ADR 0002's title is now wrong and its body is not. It stays as written, with a
  pointer here, because rewriting an accepted ADR to look prescient is the
  opposite of what an ADR log is for.
- The count reaching six is a prediction, not a licence. Each context still has
  to earn its boundary the way Pricing did — its own lifecycle, its own actor, or
  a consistency boundary that is genuinely its own.
- **A context with no writer is not a context yet.** Pricing today reads
  committed files and writes nothing; when promotions become editable it grows a
  repository, and only then does it need a schema. Creating one first is what
  phase 2 did with the attribute definitions table, and undid the next day.
- The rule that a context references only `SharedKernel` is a default, not an
  absolute: `Search` already crosses into `Catalog` by ADR 0007, deliberately and
  with the direction stated. New crossings need the same treatment — named, one
  direction, and a test.
