# Images I-3 · Alt text, proposed and approved — plan (2026-10-06)

Spec: [`docs/specs/images.md`](../specs/images.md), step I-3. The map of what
exists came back with more missing than the spec assumed; this plan is the spec's
step redrawn against the code.

## What the code says

- `ProductImage.Alt` exists, is a `LocalizedText?`, is stored and is served — and
  **nothing can change it**. Only an import sets it. There is no domain method, no
  command, no endpoint, no screen.
- **A re-import erases it.** `ReplaceImages` rebuilds every image from the
  source's alt, and the seed carries none.
- Proposals (`Assist`) are text only: a `ProposalDisplay` has a title and a list of
  changes, no picture. There is **no edit decision**: approve takes ids.
- Nobody but Steward's tools creates a proposal. A job has no precedent.
- The API host has what a job needs — the image store, the model profiles, a
  person as principal, `Assist`. The worker host has none of them.
- Registering a proposable command gives Steward its `propose_` tool
  automatically, and Steward cannot see a picture.

## Slices, in order

| | What | Depends on a ruling |
|---|---|---|
| **A-1** | `Product.DescribeImage` and `SetImageAltText` (command, validator, handler, `PUT /api/catalog/products/{id}/images/{imageId}/alt`): **the non-AI path first**. A re-import keeps an alt the source does not carry | no |
| **A-2** | The proposable command `SetImageAltText`, with the picture carried on the proposal: `ProposalDisplay` gains an optional image, `ProposalView` serves it, the review draws it beside the text | no |
| **A-3** | The read: which (product, image) lacks alt text in which language — a kernel port Catalog implements, so `Assist` can ask without naming a product | no |
| **A-4** | The job, in `Assist`: for up to **five** pictures (the author's batch), open the bytes, ask the profile that reads pictures for `{es, en}` held to a schema, and send `ProposeActionsCommand` with a reason. Started by a shopkeeper from the backoffice (`POST /api/steward/alt-text/propose`), so who asked is a person. No profile that reads pictures: it says so and proposes nothing. A picture with an open proposal is skipped | no |
| **A-5** | The backoffice: the button that starts a batch, the picture in the review, the alt field on the product's screen (the non-AI path's screen) | no |
| **A-6** | The event `image_alt_proposal_decided`, the tracking plan, es and en, OpenAPI and the generated types | partly: *edited* |

## Rulings — all three taken as proposed (the author, 2026-10-06)

1. **"Approves, edits or rejects".** There is no edit today, for any proposal.
   *Proposed*: an edit is a **decision of its own** — approve with the text the
   person corrected — built once in `Assist` for every proposable command whose
   payload is text, and the event's *edited* counts it. The cheaper alternative:
   reject, and type the alt text on the product's screen (A-1's endpoint); then the
   event has no *edited* value.
2. **Steward's blind tool.** Registering the command gives Steward
   `propose_set_image_alt_text` while its turn sends no picture: it would write alt
   text without seeing. *Proposed*: the command is proposable by the job only —
   an entry may say it is not Steward's — until Steward's turn can carry a picture.
3. **Who a job's proposal is from.** The review says *Steward · model* for every
   proposal. *Proposed*: provenance names what proposed it (*Alt text job*).

## Decided here, not the author's

- Both languages in one command: alt text is user-facing (invariant 6), and one
  approval should not leave a picture described in half the shop.
- At most 300 characters a language; the reading aims under 125, what a screen
  reader reads comfortably.
- The job runs in the API host, on request. Not a polling worker: five at a time
  is a person's decision each time (the author, 2026-10-06), not a queue to drain.

## Progress

- **A-1** built 2026-10-06: the command, its endpoint, the import that keeps an alt.
- **A-2** built 2026-10-06, the server's half: `ProposalDisplay.Image` and
  `ProposalView.image`; `SetImageAltTextProposal` in Catalog, whose snapshot is
  that one picture's words; `IProposableCommand.OnlyFromAJob`, true for this entry
  alone, and Steward's tools leave such an entry out (ruling 2). The picture drawn
  in the review is A-5.
- **A-3** built 2026-10-06: `IUndescribedPictures` in the kernel, answered by
  Catalog per picture and per language, in a stable order.
- **A-4** built 2026-10-06: the job, `src/Assist/AltText` — beside Steward and
  Tino, not under `Features/`, because it sends `ProposeActionsCommand` and a slice
  may not name another. `POST /api/steward/alt-text/propose` (a shopkeeper's), at
  most five pictures, the profile `AltText:Model` (`local`) only if it says it
  reads pictures. A picture with a proposal waiting is left alone; what the model
  wrote that cannot be proposed — not the shape asked, a language missing, a
  sentence in the wrong language, one the command refuses — is counted, and the
  rest of the batch goes on. Metric `tendero.alt_text.pictures`. **On the real
  model** (the 4B with its projector, two seed pictures): *Zapato de casa gris con
  forro blanco y suela con círculos* / *Grey house shoe with white lining and
  circular sole pattern*; *Mochila verde oliva con detalles naranjas y bolsillos
  laterales…* / *Green olive backpack with orange accents…*. Right about what is
  drawn, and a word a person would change in each (*zapatilla*, *olive green*):
  the case for ruling 1, made by the first two.
- **Ruling 1** built 2026-10-07: `ApproveEditedProposalCommand` and
  `POST /api/steward/proposals/{id}/approve-edited`. A change on a proposal's
  display may name the payload's field its words are (`ProposalChange.Key`); only
  those fields are a person's to correct, so what the proposal is about never is.
  The command's own validators judge the corrected words; refused, the proposal
  waits as it was (`DecisionOutcome.Invalid`). What the model wrote is kept
  (`ActionProposal.ProposedPayload`, a column: migration
  `ProposalEditsAndProposer`), and the view says `edited`. The same words back are
  a plain approval.
- **Ruling 3** built 2026-10-07: a proposal says what proposed it
  (`ProposalProvenance.Proposer`: `Steward`, `AltTextJob`), served on the view; one
  stored before the name existed was Steward's.
- **A-5** built 2026-10-07, all but one screen: on the Proposals list a quiet
  *Describe 5 pictures* asks for a batch and says what it came to; the pane shows
  the picture a proposal is about, the words in boxes where the proposal offers
  them (a Signal Form over a signal of the pane's, reset when another proposal
  opens), *Approve with corrections* when a word was changed, the server's reason
  when the words are refused, who proposed, and that a decided one was edited.
- **A-6** built 2026-10-07: `image_alt_proposal_decided` — approved, edited,
  rejected — emitted by the proposals store for this command alone, and no longer
  *planned* in the tracking plan.
- **Seen end to end on the real stack** (`Tendero__Vision=true`, the first time
  the AppHost's switch ran): three batches from the screen — 5, 5, and 3 with 2
  the model did not describe — the picture in the pane, a sentence corrected and
  approved, the catalogue written with the person's words. Captures 05 to 07 in
  the phase's report.
- **Still to build**: the alt text field on the product's own screen — the path
  with no model has its endpoint and no screen yet.
