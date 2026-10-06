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

## Rulings the author owes

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
