# 0019 — The copilot proposes commands, never prose

**Status**: accepted (S-8, 2026-10-05, under the author's standing approval of Steward's plans). Reserved by `roadmap.md` before phase 12 and written at its close, once the thing it decides had been built and measured.

## Context

The backoffice copilot is the project's fourth differentiator: natural language
over the shop's own data, *proposing approvable actions*. Invariant 12 already says
a model never writes to an aggregate. It does not say what a model's output **is**
when the shopkeeper asks for a change — "give these twelve products an English
name", "take the padel shoes off sale" — and there are three answers.

1. **The model acts.** A tool that writes: `update_product(...)`. Rejected by
   invariant 12 before any design.
2. **The model advises.** It answers in prose — "I suggest renaming MUG-1 to
   *Stoneware mug*" — and a person carries it out by hand in another screen. Nothing
   is written by a model, so the invariant holds; and the copilot is a search box
   that talks. The advice cannot be approved, counted, audited or tested: whether it
   was right is a matter of reading it.
3. **The model proposes a command.** It fills in one of the shop's own commands and
   stores it, unapplied, for a person to decide.

## Decision

1. **What Steward produces that can change the shop is a proposed command, and
   nothing else is.** A proposal is a registered command's name, a payload that
   command's own validators accept, the reason a person will read, a snapshot of
   what it would change, and where it came from (profile, model, prompt version,
   conversation). It is stored as an `ActionProposal` (ADR 0052) and changes nothing.
2. **The model's tools are reads and `propose_` tools, a closed set.** One
   `propose_<name>` per `IProposableCommand` a context registered, its schema
   generated from the command's payload. No tool writes, and none approves or
   rejects: an architecture rule reads every entry's command and refuses one that
   decides proposals.
3. **Prose explains; it never stands in for a proposal.** A change the model only
   describes changes nothing and cannot be approved. The prompt tells it to call the
   tool rather than describe the change or ask the shopkeeper to write the text.
4. **What no registered command expresses cannot be proposed, and Steward says so.**
   A picture's alt text has no proposable command and no model here can see a
   picture: the answer is that it cannot, not an invitation to dictate one.
5. **A person decides, as a domain operation.** Approving runs the command as the
   approving person, through the dispatcher — validation, audit and handler as a
   click would — and its audit row carries the proposal's id. Rejecting records who
   and why. Neither is a chat message.
6. **Whatever can be decided by code, is** (invariant 12's second half). The payload
   is checked when it is proposed and a refused item goes back to the model as the
   tool's answer; a proposal whose snapshot no longer matches is stale and does not
   run; the rows a read returned are drawn as a table built by the read's context,
   not retold; rows the model copies are taken out; a tool call it writes as text is
   replaced by a fixed message.

## Consequences

- **The copilot's ability to change things is exactly the list of proposable
  commands.** Adding one is a class and a registration in its context. There is no
  "do this for me" outside that list, by design.
- **It can be evaluated without a judge model.** A proposal is a command, so the
  copilot suite compares what was stored with what should have been — name,
  subject, the fields that matter — and a model's quality is a count, not an opinion.
- **A proposal is evidence.** It records the prompt version it was made under, so a
  rejected one is what a change to the prompt is judged against.
- **The conversation is not audited; what it proposes is.** A turn is an unaudited
  command; proposing is an audited one, and so is every approval.
- **Nothing forces a model to act.** Measured on the local 4B: asked to propose a
  Spanish name for a product that already has one, it declines in three runs of five,
  and told a warehouse's real code it asks the shopkeeper instead of asking again.
  Code cannot propose on a model's behalf — that would be the model's decision made
  by a regular expression — so this is a model's quality, measured per profile.
- **The shopkeeper reads proposals, not paragraphs.** A hundred changes are a
  reviewable list with a bulk decision, each row a value before and after.

## Rejected

- **Advice in prose** (2 above): cheaper to build, and it gives up approval, audit
  and evaluation — the three things that make an AI's suggestion something a shop
  can rely on.
- **Free-form actions the model composes** (a script, SQL, a patch to a document):
  nothing validates them but the model, and the reviewer would be reading code.
- **Auto-approval above a confidence**: a model's confidence is its word for itself.
  If a class of change ever earns it, that is a rule of the shop's, written in the
  shop's code and decided in its own ADR — not a threshold on a model's number.
