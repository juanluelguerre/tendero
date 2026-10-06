# Steward S-5 · Claims in bulk — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A product's proposed claims can be approved together through the same review Steward's proposals go through — every row seen, any row unticked — and a weakly supported claim is never approved in bulk: the server refuses it whatever the client sends.

**Architecture:** Knowledge gains a bulk approval command that dispatches the existing `ReviewClaimCommand` once per claim, each in a DI scope of its own (so each is its own audit row and one failure leaves nothing half-written for the next), after checking the claim's support on the server. The claims queue can be narrowed to one product. In the backoffice, the review dialog's frame (head, scrolling table, foot with the count and the actions) becomes one presentational component that the proposals review and the new claims review both use — spec decision 5, *one review component for everything a model proposes*.

**Tech Stack:** .NET 11, Carter, FluentValidation; Angular 22, `@ngrx/signals`, Transloco.

**Spec:** `docs/specs/steward.md` — decision 5, *The bulk review of claims*; mock-up `design/unpublished/mockups/backoffice-steward/6-review-claims-in-bulk.png`; R-2's P4 in `docs/specs/redesign.md`.

## Global Constraints

- The weak rule is the server's (`ProductClaim.IsWeaklySupported`); the screen never keeps a copy of 0.8.
- Each approved claim is its own command and its own audit row.
- Strings in es and en; tokens only; one clay action per view.
- OpenAPI and frontend types regenerated after the new endpoint.

## Review Focus

1. A client that sends a weak claim's id in a bulk approval: it is refused (WeakSupport), the others are approved.
2. A claim decided by someone else between opening the review and approving: NotWaiting on its row, the others go on.
3. A product with only weak claims: Approve is disabled, *Open the weak one* is the way on.
4. The batch cap (100) and an empty batch: refused by the validator.
5. The proposals review keeps working exactly as before after its frame is extracted.

---

### Task 1: The server

**Files:** `src/Knowledge/Features/ReviewClaims/ReviewClaims.cs` (product filter); `src/Knowledge/Features/ApproveClaims/ApproveClaims.cs` (new slice); `src/Knowledge/Ports/KnowledgePorts.cs`; `src/Persistence/EfKnowledgeRepository.cs`; tests in `tests/Knowledge.Tests`, `tests/Api.Tests` (policy), `tests/Integration.Tests` (product filter on Postgres).

- `GET /api/knowledge/claims?product={id}` — the queue narrowed to one product.
- `ApproveClaimsCommand(IReadOnlyList<ClaimId> Ids)` → `ClaimApprovals(Rows: ClaimApproval(ClaimId, BulkApprovalOutcome))`, `BulkApprovalOutcome { Approved, WeakSupport, NotWaiting, NotFound }`; `POST /api/knowledge/claims/approve { ids }`, shopkeeper.
- [ ] Tests: weak refused and not approved, the others approved; already decided → NotWaiting; unknown → NotFound; duplicates once; validator 1..100; the product filter.

### Task 2: One review, two uses

**Files:** `frontend/apps/backoffice/src/app/worklist/review-dialog/` (frame); `features/steward/proposal-review/` (uses it); `features/claims-review/claims-bulk-review/` (dialog + store + specs); `features/claims-review/claim-pane/` (the door to it); `data-access/knowledge.service.ts`; i18n.

- [ ] Store spec: loads the product's claims; weak rows unticked and not tickable; Approve sends the ticked; each row's outcome; *Open the weak one* names the first weak claim.
- [ ] The proposals review's specs pass unchanged.

### Task 3: The record

- [ ] R-2's P4 amended in `docs/specs/redesign.md`; S-5 as built in the spec; the board; the notebook.
