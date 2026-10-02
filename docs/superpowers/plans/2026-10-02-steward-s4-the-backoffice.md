# Steward S-4 · The backoffice — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The shopkeeper meets Steward in the backoffice: a panel beside every screen (a sheet on a phone), proposals arriving as a card, a review dialog that approves or rejects a batch, a Proposals list of work with its pane and rail badge, and an honest unavailable state — in es and en.

**Architecture:** The backend gains what the screens need and S-1 left out: each proposal stores, when proposed, a **display snapshot** its owning context wrote (the subject's title, the kind of change, each field *now → proposed*), so Assist lists proposals without reading Catalog (invariant 1, ADR 0052); and a list query, `GET /api/steward/proposals`, counted per filter on the server and narrowable to the ids a turn returned. The frontend is one feature folder, `features/steward/`: a root `StewardStore` for the conversation (the shell's button, the palette and the panel share it), the panel and its card, the review dialog, and the Proposals page on the `worklist/` pattern.

**Tech Stack:** .NET 11, EF Core (a migration), Carter; Angular 22, `@ngrx/signals`, Transloco, Tailwind over the tokens.

**Spec:** `docs/specs/steward.md` — *The panel*, *The review*, *The Proposals list*, *When no model answers*; mock-ups `design/unpublished/mockups/backoffice-steward/1…5`.

## Global Constraints

- Every user-facing string in es and en (Transloco `public/i18n/{es,en}.json`); none hardcoded in a template.
- Tokens only; every size a token; no arbitrary lengths in brackets. One clay action per view; agent-driven actions are clay.
- Component = `name.ts` + `name.html` (+ `name.css` only when needed); folders by feature; a store for state with a life of its own; services in `data-access/`; no `effect` in a store.
- OpenAPI regenerated and frontend types regenerated after any response change; types come from `@tendero/shared-api`.
- A closed set crosses the wire as an enum by name (`WireEnum<T>` in a query string).
- The shopkeeper never reads a type name: *Translate a product*, *English name*.
- `dotnet build -warnaserror`, `dotnet test`, `npx nx test backoffice`, `npm run format:check` green.

## Review Focus

1. A proposal stored before this change has no display snapshot: the list and the card still show it (its subject code, the action's label), never an error.
2. A turn's card asks for its ids; ids already decided by another person still show, with their status — the card never claims "12 to review" when 3 were decided meanwhile.
3. The review: unticking every row disables Approve; rejecting the unticked requires a reason; each row shows its own result after approving (applied, failed with its reason, stale).
4. The panel when the turn is cancelled (the person closes it or presses Stop): no answer is shown, the composer is usable again, nothing is retried.
5. The unavailable, too-long and over-budget answers each say what happened and offer the way on (the filter on Products, the Proposals list, a new conversation).

---

### Task 1: The display snapshot

**Files:** `src/SharedKernel/Proposals.cs`; `src/Catalog/Features/SetProductTranslation/SetProductTranslationProposal.cs`; `src/Catalog/Features/WithdrawProduct/WithdrawProductProposal.cs`; `src/Assist/Domain/ActionProposal.cs`; `src/Assist/Features/ProposeActions/ProposeActions.cs`; `src/Persistence/Configurations/AssistConfiguration.cs`; a migration `ProposalDisplay`; tests in `tests/Catalog.Tests`, `tests/Assist.Tests`, `tests/SharedKernel.Tests` (or the kernel's test project).

**Interfaces — Produces:**
- `ProposalChange(LocalizedText Field, string? Now, string? Proposed)`
- `ProposalDisplay(LocalizedText Title, LocalizedText Kind, IReadOnlyList<ProposalChange> Changes)` with `string ToJson()` and `static ProposalDisplay? FromJson(string?)` (null or unreadable → null)
- `ProposalSubject(string Subject, object Now, ProposalDisplay Display)`; `ProposalCheck.Display`
- `ActionProposal.Display` (string?, jsonb), set by `Propose(…, display)`

- [ ] Test: a translation proposal's check carries Title = the product's name, Kind = *English name* / *Nombre en inglés*, one change *Name · EN: (null) → "Mug"* (and Description when given). Withdrawal: Kind *Withdrawal* / *Retirada*, change *Status: Active → Withdrawn*.
- [ ] Test: `ProposeActions` stores the display; `ProposalDisplay.FromJson(ToJson(x))` round-trips; unreadable → null.
- [ ] Implement; migration adds nullable jsonb `display`.

### Task 2: The list query and endpoint

**Files:** `src/Assist/Features/ListProposals/ListProposals.cs`; `src/Assist/Ports/AssistPorts.cs` (or the repository's file); `src/Persistence/EfProposalRepository.cs`; tests `tests/Assist.Tests/ListProposalsTests.cs`, `tests/Api.Tests` (policy + binding), integration test of the repository's page and counts.

**Interfaces — Produces:**
- `enum ProposalFilter { ToReview, Applied, Rejected, Failed, All }` — ToReview = Proposed + Approved (result unknown); Failed = Failed + Stale.
- `GET /api/steward/proposals?filter=&page=&pageSize=&ids=&culture=` (shopkeeper) → `ProposalsPage(Items, Total, Page, PageSize, Counts: ProposalFilterCount[])`; with `ids`, the filter is ignored and those proposals come in the order asked.
- `ProposalView(Id, Command, Action, Subject, Title, Kind, Changes: ProposalChangeView[], Reason, Status, ProposedAt, DecidedBy, DecidedAt, Result, Provenance)`; `ProposalChangeView(Field, Now, Proposed)` resolved in the culture.
- `IProposalRepository.PageAsync(IReadOnlyCollection<ProposalStatus>? statuses, IReadOnlyList<ProposalId>? ids, int page, int pageSize)` and `CountAsync()` by status.

- [ ] Tests (fake repository): counts per filter; ToReview holds Approved; Failed holds Stale; ids keep their order; a row without display shows the subject as title and the action's label as kind; the action is the registry label in the culture, the command name when unregistered.
- [ ] Implement; regenerate OpenAPI and the frontend types.

### Task 3: Data access and the Proposals page

**Files:** `frontend/apps/backoffice/src/app/data-access/steward.service.ts`; `features/steward/proposals/` (`proposals-page.ts|html`, `proposals.store.ts|spec.ts`, `proposal-list/`, `proposal-pane/`); `app.routes.ts` (`steward/proposals`); `shell/nav/navigation.ts` (group `steward`, waiting `proposals`); `shell/waiting.store.ts`; i18n.

- [ ] Store spec: the filter in the URL (default ToReview), the counts as chips, the open proposal in the pane, approving and rejecting (reason required) reload the list and open the next.
- [ ] Pane: kind · status eyebrow, title, subject code, each change *now → proposed*, the reason, provenance (proposed by, prompt, asked by, command, trace), Approve (clay) and Reject… (the worklist confirm dialog with a reason); a decided proposal shows its result instead of the buttons.
- [ ] Rail badge from the ToReview count.

### Task 4: The review dialog

**Files:** `features/steward/proposal-review/` (`proposal-review.ts|html`, `proposal-review.store.ts|spec.ts`).

**Interfaces — Produces:** `<backoffice-proposal-review>` with `open(ids: string[])`; output `decided`.

- [ ] Store spec: loads the ids; only ToReview rows are tickable and ticked by default; `selected` count; Approve sends the ticked ids and writes each row's result in place; Reject the unticked requires a reason; nothing ticked disables Approve.
- [ ] Dialog: provenance in the head (model, prompt, when), table code · now · proposed, foot *11 of 12 selected*, Cancel, Reject the unticked one(s), Approve N.

### Task 5: The panel

**Files:** `features/steward/steward.store.ts|spec.ts` (root); `features/steward/panel/` (`steward-panel.ts|html`), `proposal-card/`, `cost-line/` as needed; `shell/shell.ts|html` (header button, layout); `shell/palette/palette.store.ts` (*Ask Steward*); `design/theme.css` (`--container-panel: 26.25rem`); i18n.

**Interfaces — Consumes:** `StewardService.turn(request, culture)`, `proposals({ids})`; `ProposalReview.open(ids)`.

- [ ] Store spec: open/close; the screen context from the route (page title + the route's subject or status); asking appends the question, sends the history and the conversation id, and appends the answer; Stop cancels the request and leaves no answer; the cost line sums the turns; Unavailable, ConversationTooLong, BudgetExceeded and NearContextLimit each produce their notice; New conversation clears everything.
- [ ] Panel: head (Steward, *On: …* chip, close), messages, per answer *Read … · N rows*, the text, the proposals card (*12 proposals · English name*, reason, three *now → proposed* rows, *and 9 more*, **Review 12 proposals**, *Kept in Proposals*), cost line, working line with Stop, composer and the promise. Unavailable: the notice, *Without Steward* links (Products missing an English name, Proposals), Try again.
- [ ] Shell: *Steward* in the header; from `lg` the panel is an aside beside the content (`w-panel`); below, a full-screen sheet (`dialog`). Palette option *Ask Steward*.

### Task 6: Close the step

- [ ] Spec as-built notes (S-4), delivery plan, notebook entry; full suite, `nx test backoffice`, format check; commit.
