# Spec: Images — pictures the shop can read, check and search by

**Status**: accepted (2026-10-06), building — I-1 under way
**Phase**: 13a · **Branch**: `feature/phase-13a-images`
**Slice(s)**: `tools/SeedImages` · `AI` (a model that reads a picture) · `Catalog` (alt text) · `Knowledge` (claims from a picture) · `Search` (by image)
**Article**: *"The catalogue had no eyes"* (working title)

## Problem

The shop sells by pictures and cannot see one. Eighteen of the hundred seed
products have a picture; none has alt text in either language; nothing checks
that a picture shows what its product's data says; and a shopper holding a
photograph of what they want has only words to search with. The evidence that
all of this is feasible on the stack already running is a spike
(`docs/analysis/image-models.md`); this spec is what is built from it.

## Decided by the author (2026-10-06)

| Question | Ruling |
|---|---|
| Which image work | **Both**: a model that reads pictures (alt text, picture against data) and visual search (SigLIP 2) |
| Weights no maker publishes in the format we run | **Our own conversion** from the pinned revision (ADR 0031), for the vision projector and for SigLIP 2's ONNX. Not a third party's copy |
| What a claim read from a picture rests on | **Option A**: the document is the model's description of the picture, the quote is taken from it, and the claim is marked as read from a picture |
| A picture a shopper uploads | Not kept, metadata stripped, written in `docs/privacy.md` |
| Voice | Not here: postponed to a phase of its own (14) |
| What the pictures may cost | **3 € in all** (2 € until 2026-10-07, raised by the author when $1.29 was spent with 47 pictures to go), and drawn **five at a time**, or ten at his word, not the 82 at once |
| What a batch is | The five drawn together: the author looks at their contact sheet and accepts or sends back, batch by batch |
| Alt text | In the same batches of five, so a mistake costs five and not a hundred |
| New packages in `src/` | Yes to `Microsoft.ML.OnnxRuntime`, `Microsoft.ML.Tokenizers` and SkiaSharp, each checked at its exact version when added |
| The kit solver | Its own phase, 13c, after this one |

## Behavior

Seven steps, each closed before the next. The order is forced: nothing can be
measured on pictures that do not exist, and the model that reads a picture is the
check the generated ones need.

### I-1 · The 82 missing pictures

`tools/SeedImages generate --take 5` with the `gemini` provider draws what
`seed/images/` lacks, **five at a time**. It is a paid call (about $0.03 a
picture) on the author's renewed key, **under a ceiling of 3 € for all of it**
(2 € at first): the first ceiling did not reach 82 pictures and their redraws, so the
spend is counted batch by batch in the table below and the run stops and asks
before the batch that would cross it.

| Batch | Date | Pictures | Calls | Spent (about) | Running total | Outcome |
|---|---|---|---|---|---|---|
| 1 | 2026-10-06 | `B10SHO0102` to `B10SHO0106` | 5 | $0.15 | $0.15 | **accepted by the author**, with `B10SHO0106` to be redrawn: an emblem on its side |
| 2 | 2026-10-06 | `B10SHO0107`, `B10SHO0108`, `B10SHI0201` to `B10SHI0203` | 5 or 6 (one picture took the time of two) | $0.15–0.18 | $0.39 at most | **the first batch through the shop model's five checks: 5 of 5 pass**; accepted by the author |
| 1, redraws | 2026-10-06 | `B10SHO0106`, twice | 2 | $0.06 | $0.21 | the first redraw carried the emblem again — the hand-wash symbol, drawn from *"Hand washable."* in the description; with that sentence kept out of the prompt (`WithoutCareInstructions`), the second is clean |
| 3 | 2026-10-07 | `B10SHI0204` to `B10SHI0208` | 5 | $0.15 | $0.54 at most | **the first batch through the seven questions of I-2b: 5 of 5 pass**, and nothing seen on the contact sheet; the cycling jersey is drawn from the back, where its three pockets are. **Accepted by the author** (2026-10-07) |
| 4 | 2026-10-07 | ten, none of them clothing, at the author's word — a batch is five: `B11COO0303`, `B11COO0310`, `B11COF0401`, `B11COF0407`, `B11TAB0501`, `B11TAB0504`, `B12LAM0603`, `B13BAG0702`, `B14ORG0901`, `B15SHO1101` | 10 | $0.30 | $0.84 at most | **5 of 10 pass, 5 to look at**, and by eye: **two are wrong** — the set of 3 knives has four, the set of 6 tumblers has seven — both doubted by the count; **two are false alarms** — the four plates counted as five, and the moka pot's safety valve, which its description names, read as a mark; **one is the catalogue's** — the toaster declares `piezas: 2` for its two slots. The count doubted every picture whose number was wrong, with the right number once (four knives) and a wrong one twice (eight for seven tumblers, five for four plates). The author's rulings: the knives and the tumblers rejected, the toaster's `piezas` removed from the seed, the boots wanted as a pair |
| 4, redraws | 2026-10-07 | `B11COO0310`, `B11TAB0504` | 2 | $0.06 | $0.90 at most | the two rejected pictures **kept first** (`reject`, the first two of `known-bad/`), and the cause repaired before the redraw: the prompt named the number and then said *One single object*. A product that declares more than one piece is now asked for *Exactly three of them, each one whole and none hidden behind another* (`PromptTemplate.SetOf`). Both came back with the right number, by eye — **the shop's model has not been asked**: the author's load tests were running. On the knives two of the three stand blade up in the block. The author: the six tumblers are right; **the knives are wrong both times**, blades up; the boots stay one |
| 4, the knives again | 2026-10-07 | `B11COO0310` | 1 | $0.03 | $0.93 at most | the second wrong picture kept too, under a kind of defect added for it — `Form`, the product itself drawn wrong, which no code can draw into a right picture. A redraw can now be told what the last one got wrong (`generate --only <id> --hint "…"`, said as what the object is and placed after the description): *its blade is inside the block and only its handle shows above it*. Three knives, three handles up. **Accepted by the author, with the rest of batch 4** (2026-10-07) |
| 5 | 2026-10-07 | ten more of mixed kinds: `B11TAB0502`, `B11TAB0503`, `B11COO0304`, `B11COO0308`, `B11COF0406`, `B12LAM0601`, `B14HOM0805`, `B14HOM0806`, `B15SHO1102`, `B13BAG0704` | 10 | $0.30 | $1.23 at most | **the three sets came back with their number** — four bowls, five utensils, two cushion covers — under the new set line, and the count agreed each time. **One real defect, caught by the mark question**: the desk lamp's *three temperatures* drawn as three icons on its shade. **Two false alarms, both the scene list naming the product's own parts or a set's own members** (the kettle's spout and handle; the five utensils), and the same on the redrawn knives. **Accepted by the author** (2026-10-07), the lamp's last redraw with it |
| 5, the lamp | 2026-10-07 | `B12LAM0601`, twice | 2 | $0.06 | $1.29 at most | rejected by the author and kept. **The first redraw was worse**: `350°` lettered beside the lamp and a dial drawn for it — *350 degrees of rotation* had reached the prompt, because an angle was not among the measurements code takes out. It is now (`WithoutMeasurements`, with what it measures), that picture is kept too, and the second redraw has nothing printed and nothing beside it. **It stands on a round base, and the description says a desk clamp**: said to the author, who accepted the batch |

A picture is accepted only when it is **right for its product's description** and
**in the register of the first eight**. `verify` today checks text, colour, scene
and placement; two checks are added:

- **Picture against description** — a vision model is shown the picture with the
  product's name and description and says what it shows, whether it matches, and
  what differs (count, shape, material, a part the description names). Measured
  first on a set of known-wrong pairs (pictures deliberately swapped between
  products), so how often it misses is a number and not a hope.
- **Same register** — the candidate beside a reference picture: line illustration,
  the tokens' flat background, the same framing.

**Each check proposes; a person accepts the batch.** A flagged picture is redrawn
or the catalogue entry is corrected, whichever is wrong, and each case is written
down with its reason. The whole run is recorded as it happens — prompt, spec,
batches with time and cost, the vision pass, every redraw and why — in the
notebook, because the article and the book have to tell it.

### I-2 · A model that reads a picture

The model the shop already serves (`Qwen3.5-4B`) has a vision tower; llama.cpp
needs it as a second file. That file is **produced by us** from the pinned
revision, as `prepare-extractor.sh` produces the text half, and recorded under
ADR 0031 with its hash.

**If the converter of the pinned llama.cpp does not support this architecture**,
the step stops there and the author is told what was found: a third party's file
is not a fallback this spec may take on its own.

*As built (2026-10-06), the first half*: it does. `src/AppHost/scripts/prepare-vision.sh`
converts the projector from the pinned revision with the pinned converter — 672 MB,
SHA-256 `1a039fd0…b7f5`, recorded in ADR 0031 — and the server loads it beside the
Q8 text half in 5.7 GB of the card. **The image tool asks it first**: `verify` and a
new `pairs` command reach a vision model through a port, `IVisionModel`, with two
adapters (Ollama, and llama.cpp's server — the shop's model), and carry I-1's two
checks. Measured on the 23 pictures there are: all 23 pass the five questions; the
description check refuses no right picture and **misses 3 to 4 wrong ones in 23**,
always a right kind of object with something wrong about it (one pan for a set of
three, a backpack for a duffel) — so a person's look at each batch stays
(`tools/SeedImages/README.md`).

*The second half, the shop's side (2026-10-06)*:

- **`Tendero__Vision=true`** starts the local model with its projector, after a
  `vision-projector` job has converted it (limited to 4 GB: the conversion's own
  memory peaked at 2.6). Only on a GPU, as Tino: it is where it was measured. The
  API is then told the `local` profile reads pictures.
- **`ModelProfile.ReadsPictures`**: declared, because nothing in a profile's
  address says whether its server has eyes. What needs a picture read asks first,
  and takes its path without a model when the answer is no.
- **`Pictures.ForModel`** (`src/AI/Chat`): the one place that turns a stored
  picture into what a model is sent — decoded and written again as PNG, in memory.
- **The scripted client** answers a step marked `"pictures": n` only when the
  message carries that many, so a pipeline that forgot the picture fails in CI.
  **The in-process ONNX client refuses** a conversation with a picture in it; it
  used to drop it and answer anyway.
- **Proved against the real model**: a contract test sends a WebP through the
  shop's own factory to the server started as the stack starts it; the model names
  its colour and the picture's tokens are in `Usage`. It skips where no such
  server runs. The whole chat contract passes on the server with the projector too.
- **Not needed after all**: `Catalog:Images:RootPath` in the AppHost. The spike
  wanted a container to read the store; the API reads it and sends the bytes.

One model serves text and pictures (5.5 GB of 8 with the projector; two do not
fit). What changes in code:

- The store's pictures are WebP and the server does not decode it: a reader
  converts in memory before sending.
- `InProcessOnnxChat` and `ScriptedChatClient` read only a message's text and
  would drop a picture. The scripted one learns to match on a picture's presence,
  so the pipeline runs in CI with no model.
- Every call is metered as today; the picture's tokens are already in `Usage`.

**Degraded path**: with no vision-capable profile, everything in I-3 and I-4
says so and proposes nothing; the catalogue and the shop are unchanged.

### I-2b · The checks, held to what goes wrong (added and built 2026-10-07)

The measurement above asks one thing: a picture shown with another product's
data. That is *a wrong thing*, and the model sees it. What a generator gets wrong
is *the right thing drawn wrong* — and the one defect of batch 1, the hand-wash
symbol, was seen by a person: no question was asked of it, because the model had
no eyes yet, and the picture was drawn over before it had. Whether any question
would have caught it is not known. Three changes, before the pictures still to
draw are judged by these checks:

1. **A set of known-bad pictures, by kind of defect.** The pictures a batch
   rejects are kept from now on instead of deleted — the kinds already seen are an
   emblem on a slipper, `4 mm` on a midsole, `24h` on a display, and where the
   rejected file is gone the defect is drawn again on purpose — each labelled with
   what is wrong: a count, a
   symbol or lettering, a colour, a scene, the register. They live with the tool,
   outside `seed/images/`, and never reach the catalogue. Every one of the five
   questions is measured against the set, so there is a table of what each one
   catches and of what none does.
2. **The model reads, code compares** (invariant 12). *Does it match the
   description?* leaves the verdict to the model, and that is the question that
   passed one pan as a set of three. A question is asked for each structured
   attribute the product has, closed, and answered with a value: *how many do you
   see?* against the attribute's count, the colour against the catalogue's nine,
   the material against its options. Code compares the answer with the attribute.
   And a question of its own for what is not text: *is there a symbol, an icon or
   a label on the product?* The open question stays, as one more voice.
3. **A miss rate that may not rise.** The set's result is recorded per question,
   as the search gate's NDCG and Stryker's `of covered` are. Changing the model,
   the projector, a question or its wording means running the set again, and a
   question that catches less than its recorded number is a regression to explain.

What stays as it is: **a check proposes and a person accepts the batch**, because
the next defect is one nobody has labelled yet; and **what cannot be drawn is kept
out of the prompt by code**, which repaired the emblem when no check had seen it.

Not known until the set says it: whether a 4B model counts small or overlapping
objects. If the count question misses too, its number says so and the person's
look carries that kind of defect.

*As built (2026-10-07)* — `tools/SeedImages`: `defects`, `reject`, and `verify`
asking through one `PictureExaminer`.

- **The set is drawn by code, not by the generator.** The rejected pictures were
  gone and each redraw is a paid call, so `DefectForge` puts one defect of each
  kind into every picture there is: lettering (`4 mm`, in strokes), an emblem, a
  surface with a second object, the hue turned, the product twice, a graded
  background. Free, the same on every run, and **the easy case**: a stamped
  emblem is cleaner than a drawn one. `reject` keeps a picture a person rejects,
  with its defect, in `tools/SeedImages/known-bad/` — the hard half, empty today.
- **A defect counts as caught by a question only when that question did not
  already doubt the picture before the defect went in.**
- **Measured, the shop's model, 28 pictures and 126 with a defect drawn in:**

  | Defect drawn in | Pictures | Caught by | By no question |
  |---|---|---|---|
  | Lettering | 28 | Text **28** · Scene 22 · Mark 4 | 0 |
  | Emblem | 28 | Mark **27** · Text 11 · Scene 8 | 0 |
  | Scene | 28 | Register 25 · Scene **23** (and the other 5 it doubted before) | 0 |
  | Colour | 11 | Colour **9** | 2 |
  | Count | 3 | Count **3** | 0 |
  | Background | 28 | Register **27** | 1 |

  Right pictures doubted: Scene 5 of 28, Colour 4 of 28, every other question 0.
  The board is `known-bad/baseline.json`; `defects --ci` fails below it.
- **What the numbers changed:**
  - *A symbol does need its own question.* The question about text names symbols
    too and caught 11 emblems of 28; the one that asks only that caught 27.
  - *"Does it match the description?" caught none of the 126.* It tells one
    product from another (`pairs`) and nothing drawn wrong.
  - *The scene question was blind.* Asked yes or no, the model said no to 28 of
    28 — and, asked what it saw, named the surface and the ball in every one. It
    now **lists** what is drawn besides the product and code says whether the
    list is a scene, leaving out the shadow and the background. 0 caught became
    all 28 doubted, at the price of 5 right pictures doubted, all bags: three
    are drawn with things that are not the product (a toothbrush and a tube in
    the wash bag, a card and a charger by the laptop sleeve, a bottle in the
    backpack) and two list the bag's own handles and zips. **Whether the first
    three are scenes is the author's to say.**
- **Not built, and why:** the material question. A flat illustration does not
  tell steel from aluminium; it would be asked and answered at random.
- **Small numbers:** three pictured products declare `piezas`, so the count is
  measured on three; eleven have a hue to turn.
- **Seen in the server's log, tried, and it changes nothing here:** llama.cpp
  warns that Qwen-VL wants at least 1,024 image tokens (`--image-min-tokens
  1024`). The whole board measured again with it came out the same, cell for
  cell: a 1,024 px picture already takes that many. It is a floor for a small
  picture, so it matters where one can arrive — a shopper's upload (I-6) — and
  is decided there, not here. The server and the stack run without it.

### I-3 · Alt text, proposed and approved

An image has no alt text in either language. A job reads each picture that lacks
one and **proposes** `SetImageAltText` — a registered command (ADR 0052) with the
text in es and en — which lands in the Proposals queue with the picture beside
it. The shopkeeper approves, edits or rejects; only an approval writes to
`Catalog`, as the person, with an audit row. Steward gains the same command, so
*"write the alt text of this product's pictures"* ends in the same queue (it says
today that it cannot).

The storefront renders the approved text as the picture's `alt`. **The non-AI
path** is the shopkeeper typing it on the product's screen, which must exist and
work with every model stopped.

*As planned and begun (2026-10-06)*: the code had less than this step assumed —
alt text could be stored and served but nothing could change it, a re-import
erased it, a proposal could carry no picture and no edit, and no job had ever
proposed anything. The step is redrawn in six slices in
`docs/plans/2026-10-06-images-i3-alt-text.md`, with three rulings it asks of the
author. **A-1 is built**: `Product.DescribeImage`, the command `SetImageAltText`
(both languages at once) and `PUT /api/catalog/products/{id}/images/{imageId}/alt`
— the path with no model, first — and an import that carries no alt text now keeps
the one the product has.

*As built (2026-10-07)*: all six slices. A shopkeeper asks for a batch of five
from the Proposals list; the job reads each picture with the profile that says it
reads pictures and stores a proposal carrying the picture; the review shows the
picture beside two boxes of words, and approving sends the person's corrections
when there are any — a third decision, for every proposal whose change is words,
with what the model wrote kept beside what was applied. Steward is not offered the
command: it cannot see a picture. A proposal says who proposed it. The product's
own screen has the same boxes with no model behind them. Seen end to end on the
real stack with `Tendero__Vision=true`. **Rulings the author took on the way**: an
edit is a decision of its own; the command is a job's and not Steward's; the
review names the proposer (`docs/plans/2026-10-06-images-i3-alt-text.md`).

### I-4 · What a picture says, as claims — and against the data

- **Claims.** `SourceKind.ProductImage` joins `ProductDescription`. Its document
  is the model's description of the picture, stored with the image's id, the
  model and the prompt version; a claim's quote is taken from that description
  and held to it by the support scorer, as a claim from text is. In the review
  queue such a claim is **marked as read from a picture**, with the picture
  shown, because the scorer here checks a model against a model's own words and
  the person is the only independent check. Nothing is approved without one
  (invariant 12).
- **Picture against data.** Where the reading contradicts a catalogue attribute
  (the picture is black, the colour says navy), the review queue gets a flag that
  names both. It never corrects either: which one is wrong is a person's call,
  as it was for `B13BAG0710`.
- **What is printed on the product** (added 2026-10-07, with the author; not
  built). *Nothing printed* is the seed's rule and not the shop's: its brands are
  invented and its pictures generated, so any lettering there was made up by the
  generator (I-2b's `Text` and `Mark` questions). A real product carries print
  that belongs to it, and a picture without it would be the wrong one:
  - **identity** — the maker's brand, the model's name: trainers with their logo,
    a pan with its stamp on the handle, an appliance with its badge;
  - **function** — what a button says, a dial's numbers, a jug's scale, a display;
  - **regulation** — a CE mark, a care label, *induction* on a pan's base;
  - **decoration** — a T-shirt's print, which is the thing sold.

  So, for a picture a person uploads or a supplier sends, the question is never
  *is something printed?* It is **what does it say**, and the rule is that what is
  printed is accounted for by the product's data. As everywhere in I-2b, the
  model reads and code compares:
  - **Identity is compared by code**: the brand and model the picture is read to
    carry, against the product's own. Another maker's name is a flag that names
    both; none where the data has one is a flag too.
  - **Function, regulation and decoration are listed, not judged**: the data does
    not say what a button reads or what a print looks like. What the model read
    is shown beside the picture, and a person decides.

  **Before any of it is trusted, it is measured** as `defects` measures: the
  shop's 4B read `4 mm` in large strokes 28 times of 28, and nothing says it
  reads a maker's stamp a few millimetres high. The seed has no real brand to
  measure with — that needs pictures of real products, which is its own
  question of rights and is the author's to open.

*As built (2026-10-07), the five steps* — a picture is read, claims are proposed
from its reading, the review queue shows them as read from a picture, with the
picture, and a claim that says another value than the catalogue holds names both.

- **A reading is a `SourceDocument` of kind `ProductImage`**, made by
  `SourceDocument.Depict`: the text is the model's description, and it says which
  picture, which model and which prompt version (`PictureReading`). Its identity
  has the picture in it, so the same sentence from two pictures, or from a picture
  and the description, is three documents. A description's identity did not move —
  a test pins the hash — because stored claims point at it.
- **A reading is never the description.** "The newest document of this product"
  had no kind in it: a reading recorded after the description would have passed
  for it, been stored again as one and taken the description's claims down.
  `CurrentDocumentsAsync` and the queue are descriptions only, a picture has
  `CurrentReadingsAsync`, and both rules are held in SQL by an integration test
  with two mutations seen red.
- **The job** (`Knowledge/Features/ReadPictures`): `POST /api/knowledge/pictures/read`
  `{ "count": 5 }`, a shopkeeper's, in batches of five at most. It takes the next
  pictures with no reading in both languages by this model under this prompt
  version (`picture-reading-2026-10-07`), asks the profile `PictureReading:Model`
  (`local`) for a description in es and en held to a schema, checks by code that
  each is in its language, and records it (`RecordPictureReadingCommand`: the fast
  half, which supersedes the claims resting on the words it replaces). A picture
  read under older instructions is read again. It proposes nothing.
- **Degraded** (invariant 8, tested): no profile that reads pictures → `NoModel`,
  nothing opened, nothing stored, and claims go on from descriptions alone. A
  picture the store lacks is skipped; prose, a missing language or English filed
  as Spanish is a failed picture and never a document.
- **Every picture comes from Catalog as values**: `IProductPictures` in the
  kernel, as `IUndescribedPictures` is. The image store and that reader are now
  registered with Catalog's readers, which both hosts call: the worker registers
  Knowledge's handlers whole, and its container did not build with one it could
  not construct — found by `WorkerContainerTests`, not by a reader.
- **Its evidence of use**: `tendero.picture_reading.pictures` by outcome, in the
  tracking plan with its question. Whether claims read from pictures are worth
  reviewing is measured where they are decided, in the steps to come.
- **Claims from a reading** (step 3): the queue and `ProposeClaims` read **a
  source at a time** — the description, then each picture's reading — and never
  two as one text, so a claim rests on the documents it was taken from: a
  reading's claim on the reading, in both languages, scored against the reading's
  Spanish. The same extractor, vetting and scorer as a description's; with no
  model the rules read a reading too (*Tres sartenes apiladas* → `PIECES` 3).
  Each source is saved when it is done: one that brings the extractor down
  leaves the ones before it proposed and is the only one retried, up to the
  limit, with its reason on the document. A product is in the queue once however
  many of its sources wait; a reading replaced by a newer one of the same picture
  is history, and another picture's reading never makes it so — in SQL, with two
  mutations seen red.
- **In front of the reviewer** (step 4): `ClaimForReview` carries the picture a
  claim was read from and who read it (`PictureForReview`), and why a claim is
  decided on its own (`DecidedAlone`: weak support, or read from a picture) — the
  server's rule, sent, so no screen keeps a copy. The pane shows the picture
  above the quotes and says whose words they are; the list marks the row and
  **does not approve it from the row**; the bulk review leaves it unticked with
  its own words. **And the server refuses it in bulk whatever a screen sent**
  (`ReviewOutcome.ReadFromAPicture`, a denial in the audit): its support is a
  model's sentence held to a model's description, so the person who approves it
  has it open. Rejecting needs no look and goes with the others. Captures:
  `docs/blog/reports/2026-10-06-phase-13a-images/screens/09` to `11`.
- **Run on the real stack** (2026-10-07, vision, the extractor and the scorer):
  eight pictures read, none failed; two skipped — the store had no such bytes.
  - **With the rules alone a reading gives nothing.** The first five readings
    were proposed from and left no claim: the rules read patterns a description
    has, not a model's prose. Claims from pictures need the extractor.
  - **With the extractor, the first product read gave six claims, and five are
    typed wrong.** A hip pack: *the strap is gray* as `COLOUR_TEMPERATURE = gray`,
    and *green* and *orange* the same way — a lamp's attribute; *two visible
    zippers* as `PIECES = 2`; *a beige plastic buckle* as `MATERIAL =
    SYNTHETIC_MESH`. Each quote is in the reading and each scored 0.97: the
    scorer holds the sentence to the words, not the value to the attribute. **The
    extractor fits what it reads into the vocabulary it is given**, and a picture
    has far more in it that the vocabulary has no place for than a description
    does. Not repaired here; it is the reason the person looks, and the first
    thing to measure before these are proposed in number.
  - **The claims are in the development database**, proposed by the pipeline and
    decided by nobody.
- **Against the catalogue** (step 5; for a claim from any source, by the
  author's ruling): where a typed claim says one value and its product holds
  another for the same attribute, `ClaimForReview.Contradicts` names both — the
  claim's value as the claim writes it, the catalogue's as a person reads it in
  the reviewer's language (the queue now takes `?culture=`, ADR 0013). A claim
  that adds a value, agrees with it or types nothing contradicts nothing; numbers
  are compared as numbers (`4` and `4.0`), everything else without regard to
  case. It corrects neither. Approving replaces what the catalogue holds, so it
  is a third reason a claim is decided on its own: the row does not approve it,
  the bulk review leaves it out, and the server refuses it in bulk
  (`ReviewOutcome.ContradictsTheCatalogue`). One reason is sent, the first that
  applies: weak support, then the catalogue, then the picture — the picture and
  the two values are still shown. Catalog's values reach Knowledge as values
  (`ICatalogueValues`, a draft included, asked once for a page's products).
  Captures `12` and `13`.
- **On the development database, the first time it was asked: 10 of 87 waiting
  claims say another value than the catalogue holds, and 9 of them were read
  from a description**, not a picture — a material read as glass where the
  product holds stainless steel, bamboo where it holds glass, 10 W where it
  holds 15. They had been waiting with nothing to say so; approving one would
  have replaced the value unseen. Which side is wrong in each is not judged
  here: the author's to read.
- **A reading is offered only what a picture can show** (2026-10-07, after the
  hip pack): `IAttributeVocabulary.CurrentAsync(SourceKind)`. For a picture that
  is `PIECES` and nothing else today — the author's ruling; its colour is a
  variant axis, offered to no source, which is why the extractor had reached for
  a lamp's colour temperature. What the model types beyond the list, the vetting
  refuses and keeps as a sentence.
- **Measured before and after, on twelve seed pictures**
  (`ClaimsFromPicturesOnTheLocalModelTests`: the model reads, the same weights
  extract, against a real Postgres; 4.5 minutes for both runs):

  | | Whole vocabulary | A picture's |
  |---|---|---|
  | Claims proposed | 64 | 71 |
  | Typed | 17 | 4 |
  | Typed, and wrong | **12** | **2** |
  | Typed, and right | 5 | 2 |
  | Sentences with no attribute | 47 | 67 |

  - **The filter does what it was for**: seven colours typed as colour
    temperature, four materials guessed from a part (a knife block's wood as
    `COTTON`, a lamp's arms as `SYNTHETIC_MESH`), a toaster's slots as `CUPS` —
    gone.
  - **What is left is half wrong, and it lost half of what was right.** The
    toaster's *two slots* and its lever's *five positions* are both `PIECES`
    now; and of six sets, whose number the reading states correctly every time
    (*three frying pans*, *three knives*, *four bowls*, *six glasses*, *three
    travel bags*, *two cushions*), the extractor types two.
  - **The reading is right and the extraction is the weak step**, for the one
    attribute a picture has. And it leaves five to nine sentences a picture —
    *the product has small feet*, *the pans are arranged by size* — that are
    true and that nobody would filter or compare by: about 550 for a hundred
    pictures, each to be opened on its own.
  - **So, proposed and not built**: for a picture, no extraction from prose. The
    reading itself answers the few closed questions a picture can (how many;
    which of the catalogue's colours), code builds the typed claim or holds it
    against the catalogue, and no untyped sentence is proposed from a picture.
    The prose stays as the document a reviewer reads. The author's to decide.
- **Built, on the author's yes (2026-10-07): the picture answers, code builds the
  claim.** Prompt `picture-reading-2026-10-07c`.
  - **Two questions a picture.** What it shows, in prose, as before; and *how
    many separate items are drawn*, asked by itself and held to a schema of one
    integer. The count's wording is the one `defects` measures: the question
    lives in one file (`PictureQuestions.cs`) that `tools/SeedImages` compiles
    by link, with no project reference, so the board that may not fall measures
    the question the shop asks.
  - **The answer is a line of the reading**, written by code under the prose in
    each language (*Elementos contados en la imagen: 3.* / *Items counted in the
    picture: 3.*). So it is in the document's identity — the same words with
    another count are another reading, and what rested on the old one falls —
    a claim quotes it as it quotes any text, and the reviewer reads it beside
    the picture. No migration: a reading is still its text.
  - **No model reads a reading.** `PictureAnswersExtractor` (keyed `picture`)
    reads the line back as `PIECES`; the vetting states it from the attribute's
    labels, as it does a rule's; its support is 1 by construction, and its
    provenance is the model and the prompt that read the picture. Neither the
    chat extractor nor the rules are handed a reading: no sentence is proposed
    from its prose.
  - **Held against the catalogue by code.** A count of one says nothing of a
    product that does not say how many it is — every picture of one thing would
    propose *pieces: 1* — and is a claim only when the catalogue holds a number
    of pieces, which the review then shows beside it. More than one is always
    proposed: that is how a set nobody typed gets its pieces.
  - **The readings first.** They ask no model, so the extractor being down for
    the description neither holds a picture's claim back nor takes it down.
  - **A reading is its words and its answer, or nothing**: a picture described
    and not counted is a failed picture, read again whole.
- **Measured again, the same twelve pictures and four shoes**
  (`ClaimsFromPicturesOnTheLocalModelTests`, 63 s for sixteen pictures, two
  calls each):

  | Twelve pictures | Whole vocabulary | A picture's | The picture answers |
  |---|---|---|---|
  | Claims proposed | 64 | 71 | **6** |
  | Typed | 17 | 4 | 6 |
  | Typed, and wrong | 12 | 2 | **0** |
  | Typed, and right | 5 | 2 | **6** |
  | Sentences with no attribute | 47 | 67 | **0** |

  - Six sets of six counted as the seed declares (3, 3, 4, 6, 3, 2); the six
    single products and the four shoes counted one, and proposed nothing.
  - **The first run read eight pictures of sixteen.** The count's answer was
    capped at twenty tokens — seven are enough for `{"count": 3}` — and the
    shop's 4B spent twenty-five to twenty-nine on the sets, padding the object
    with the white space a JSON schema allows; the answer came back cut in the
    middle of the word *count*. The cap is a hundred. And the failures did what
    *known and not solved* below says: four of them held the head of every
    batch after.
  - **Not measured: a pair.** The question counts the two shoes of a pair as
    two, and the catalogue does not call a pair two pieces; such a claim would
    be proposed and a person would reject it. The seed draws every shoe alone,
    so none of its pictures says.
  - **Still to do**: the colour, which the tool already asks from the
    catalogue's own options — a variant's axis, so there is no product value to
    hold it against until a picture belongs to a variant; and the questions by
    kind of product, when the catalogue's new families bring attributes a
    picture shows.
  - The dev database's eight readings were made under the older prompt: the
    next batch reads them again, and their claims are superseded.
- **Not measured**: how often a picture proposes what the description already
  did (*three pans*, twice). Nothing merges the two; the reviewer would see both.
- **Known and not solved**: a picture the model fails on stays at the head of the
  next batch, as in the alt-text job. Five such pictures would hold the queue.
- **Run against the shop's model on five seed pictures** (2026-10-07,
  `ReadPicturesOnTheLocalModelTests`, 12 s): five of five read, in both languages.
  What it wrote is mostly what is drawn — *three stacked frying pans, teal with
  black interiors*; *a black desk lamp with a round base*, which is the picture
  and not the description's desk clamp. **And twice it wrote the product's name
  instead of the picture**: one slipper read as *a pair of grey indoor slippers*,
  and the moka pot as *a six-cup stovetop coffee maker* — a capacity no picture
  shows. Both words were in the name the job hands the model. A claim taken from
  that sentence would be the shop's own data returning as evidence from a
  picture. The Spanish is rougher than the English (*un cafetera*, *copas* for
  tumblers).
- **So the model is told nothing of the product** (the author's ruling, the same
  day; prompt `picture-reading-2026-10-07b`, and `ProductPicture` no longer
  carries a name). Run again on the same five: the slipper is *a grey indoor
  shoe*, one; no capacity anywhere; the pans, the tumblers and the lamp as before.
  **The price, as said beforehand and larger**: the moka pot is now *a metallic
  object in silver colour … with multiple facets* — it is no longer called a
  coffee maker at all. A reading that names less and invents nothing is the one
  a claim may rest on; what the object is, the catalogue already says. A test
  holds it: the script answers a product's code with prose, so a job that sends
  it fails.
- **Decided with the author for the steps to come**: a reading is kept in both
  languages; the contradiction flag is for any claim, not only a picture's.

### I-4b · The shop's model against somebody else's exam (measured 2026-10-08)

Our pictures are a hundred drawings we made, and one attribute. **ImplicitAVE**
(Zou et al., ACL 2024 Findings) is 1,610 test examples in five domains — a real
product photograph, its title, an attribute and the value a person annotated,
which the title does not state. `tools/SearchEval --suite implicit-ave` asks
the shop's 4B each one as the shop asks its own: a closed question, the answer
held by a schema to the attribute's options, compared by code.

- **The data is not in the repository and must not be.** Its repository
  declares no licence, and it derives from MAVE and Amazon's product data. It is
  downloaded outside the tree and named with `--dataset`. Good for measuring;
  **not for training** until the rights are clear. A measurement, not a gate.
- **The base, three ways** (Qwen3.5-4B Q8 with its projector, the 8 GB card,
  0.54 s an example; the same answers on a second run, 1,610 of 1,610):

  | | Examples | Picture + title | Picture | Title |
  |---|---|---|---|---|
  | **All** | 1,610 | 67.1 % | **68.0 %** | 33.2 % |
  | Clothing | 226 | 70.8 % | 71.7 % | 18.6 % |
  | Food | 390 | 76.9 % | 76.4 % | 30.3 % |
  | Footwear | 317 | 43.2 % | 48.3 % | 27.1 % |
  | Home | 457 | 65.9 % | 67.8 % | 46.2 % |
  | Jewellery and general apparel | 220 | 82.7 % | 77.7 % | 35.0 % |

  - The value is in the picture: without it, half is lost. **The title adds
    nothing overall, and costs five points on footwear.**
  - Above 90 %: animal theme, a home object's shape, toe style, flavour.
    Under 50 % with the picture: athletic shoe style (34 %), shaft height
    (47 %), bed size (47 % — *Queen* 27 times of 30: a photograph does not say).
  - The paper's table, for scale and still to be checked against the PDF:
    GPT-4V 86.8 %, the open 7–13B models of early 2024 between 48 % and 72 %.
- **Where the wrong answers went** (picture alone): a default the model falls
  back on (*Running Shoe* 63 times of 119); neighbours on a scale (bootie, ankle
  boot, mid calf; mid heel, high heel); what a picture cannot show (bed size,
  *all seasons*, the room); and the exam's own noise (*Mint* and *Mints* are two
  options).
- **The target, set by the author: precision on what goes to bulk approval, not
  accuracy on everything.** First ≥ 95 %, lowered the same night to **≥ 90 %**.
  What clears it goes to a tray approved in bulk — a person still approves
  (invariant 12); applying it with nobody is a later ADR, once weeks of bulk
  approvals show the number holds. No paid arbiter for now.
- **Three ways to get there, measured as a script before anything was built**
  (picture alone; the model answers an option's letter, and the letter's token
  carries a probability):
  - **Definitions beside each option: refuted.** Footwear, 317 examples: 64.4 %
    plain, **55.2 %** with a definition written for every option.
  - **Two runs agreeing is not confidence.** Picture and picture-plus-title give
    the same answer 86 % of the time and are right 73 % of those: the model is
    wrong the same way twice.
  - **The probability of the chosen option is.** All 1,610:

    | Accepted at confidence ≥ | Covers | Right |
    |---|---|---|
    | 0.99 | 23.7 % | 95.8 % |
    | 0.95 | 43.4 % | 92.6 % |
    | 0.90 | 53.9 % | 87.6 % |
    | 0.80 | 62.9 % | 82.8 % |

    With the threshold chosen on half the examples and measured on the other
    half: aimed at 95 %, it covers 37.9 % and is right 93.4 %; aimed at 90 %,
    52.4 % and 86.7 %. **A threshold tuned to a target lands under it**: it needs
    a margin. A threshold per attribute would need more than the 20 to 150
    examples each has here.
  - **The answer's format moves the score, and not in one direction.** A letter
    instead of a value in JSON: footwear 48.3 % → 64.4 %, food 76.4 % → 58.2 %,
    home 67.8 % → 51.4 %; overall 68.0 % → 62.6 %. Not yet separated: the
    letter, or the grammar that holds the JSON.
- **The format, separated the same night: it was the grammar.** Four ways of
  asking the same question of the same 1,610 photographs, picture alone:

  | The answer is… | Right | Tokens an answer |
  |---|---|---|
  | **one of the options, as plain text** | **80.5 %** | 3.2 |
  | a value in a JSON object, held by a schema | 68.0 % | 25–29 on some |
  | an option's letter | 62.6 % | 1 |
  | a bare JSON string, held by an enum | 59.3 % | — |

  - Looking at the tokens under the schema says why: where the grammar forces
    an opening brace the model wanted to write the answer (*`3`* at 87 %), and
    it fills the space the grammar allows with tabs before it gets back to it.
    That padding is also what cut the count at twenty tokens the day before.
  - By domain, plain text: clothing 75.2 %, food 84.9 %, footwear **71.3 %**
    (from 48.3 %), home 80.1 %, jewellery and general apparel 92.3 %. Above
    every open model in the paper's table, under GPT-4V's 86.8 %. Three answers
    of 1,610 were none of the options.
  - **Built**: `ClosedQuestion` in the model port (`src/AI/Chat`) — the options
    listed in the question, one short plain answer at temperature zero, read by
    code (what is none of the options is no answer), and the probability of the
    tokens that carry it. `--suite implicit-ave` asks with it by default
    (`--format json` reproduces the 68.0 %) and prints precision against
    coverage. From the suite itself, 0.25 s an example:

    | Accepted at confidence ≥ | Covers | Right |
    |---|---|---|
    | 0.99 | 23.7 % | 98.7 % |
    | 0.97 | 38.3 % | 96.4 % |
    | **0.95** | **45.7 %** | **95.9 %** |
    | 0.90 | 56.0 % | 93.2 % |
    | 0.80 | 69.4 % | 89.8 % |

    Chosen on half and measured on the other half: aimed at 95 %, 51.1 % at
    94.6 %; aimed at 90 %, 73.4 % at 87.8 %. So the line is set with a margin.
- **The tray, built** (the author's target: at least 90 % right among what is
  approved in bulk).
  - **The shop's own question is asked the same way.** How many is now plain
    text: one token, with its probability. The reading carries it —
    *Items counted in the picture: 3 (confidence 97 %).*, the percentage rounded
    down — and it is the claim's support, in place of the 1 a line of the
    document had by construction. An answer with no probability is neither weak
    nor sure. Prompt `picture-reading-2026-10-08`.
  - **A sure answer may go with others**: a picture's own answer (built by code
    from what the picture answered, not extracted from prose about it) with a
    support at or above `ProductClaim.SureAnswer`, **0.95**. It is still shown
    with its picture and a person still approves it (invariant 12) — from its
    row or in the bulk dialog, where every other claim read from a picture is
    still opened alone. Applying it with nobody is a later ADR.
  - **On the sixteen seed pictures**: six sets, six counted as the seed
    declares, confidences 82 % to 99 %. Four are sure (bowls 99, tumblers 99,
    packing cubes 97, cushion covers 99); the pans (82) and the knives (87) are
    right and decided alone.
  - The evidence it is worth having: `tendero.knowledge.claims.reviewed` says
    whether each decision was on a sure answer (`sure_answer`). More than one in
    ten of them rejected, and the line goes up.
- **Still open**: a closed question of options asked by the shop itself — the
  first will be a variant's colour, when a picture belongs to a variant; a
  threshold per attribute, which needs more than the 20 to 150 examples each has
  here; and whether Tino's composed answers, which are held by a schema on
  purpose (ADR 0055), pay any of this price — a different task, not assumed.

### I-4c · Attributes by category (ADR 0056, built 2026-10-08)

- A category declares which attributes its products have; a category below adds
  its own; a product has its whole branch's. Declared in
  `seed/categories.sample.json`, and held by a test to the seed's own products:
  every attribute a product carries is one its branch declares.
- **Offered, never enforced**: it narrows what an extractor is offered and what
  a picture is asked. An import carrying something else keeps it.
- What a picture shows is the attribute's own flag (`isShownInPictures`), in
  `seed/attributes.sample.json`: `PIECES`, and nothing else today. The list that
  lived in an adapter is gone.
- **So a shoe's picture is described and not counted** — a pair is not two
  pieces — and a description of cookware is no longer offered a lamp's colour
  temperature or a coffee maker's cups.
- **Measured on the claims suite** (`--suite claims --extractor chat
  --vocabulary category`; twenty seed products, the 4B Q8, the same prompt):

  | | Whole vocabulary | By category |
  |---|---|---|
  | Precision | 0.91 | **0.94** |
  | Recall | 0.82 | **0.85** |
  | Recall of what no attribute held | 0.78 | 0.81 |
  | Typed right | 0.88 | **0.94** |
  | Quoted verbatim | 0.984 | 1.000 |
  | Spurious | 14 | 15 |
  | Traps | 1 | 1 |

  Better or level on every line but one more spurious claim; twenty products,
  so each is one or two claims' worth. `whole` stays the default and the gate —
  the floors in `gates.json` were set with it — and by category is measured, not
  gated, until its own floors are written.
- **The floors are set** (2026-10-08): `chat/category` and `rules/category` in
  `knowledge/gates.json`, from four identical runs; `--vocabulary category` is
  held to them and CI runs the rules both ways.
- **Owed**: facets by category; a screen to edit the declarations.

**Amended the same day: required.** A declaration now says whether a product
*must* have the attribute (ADR 0056's amendment;
`docs/analysis/catalogue-model.md`, proposal 2). Nothing a picture is asked
changes — it is still asked what its category declares and a picture shows —
but a product can now be *incomplete*: a kind of catalogue gap, a note on the
review, never a refusal. Five attributes are required in the seed, the ones
every product of the category already holds.

### I-4d · What a picture is of — a variant's colour, and a doubt that is not a claim (spec and built, 2026-10-08)

**The question.** A product's colour is its variants' (`COLOR` is an axis), and
a picture shows one colour. So "is this picture the colour the catalogue says"
can only be asked of a picture that is known to be *of* some variants.

**What it stands on — and it moved the same day** (ADR 0057). As first built,
this read a model that was there and that nothing read: `Variant.Image`, a
variant's own picture, with null meaning *the product's* — so a picture showed
the variants that named it, and the cover those that named none. That answered
the question backwards and guessed for the cover. **Now a picture says what it
shows** (`ProductImage.Shows`, `COLOR: BLACK`):

- a picture that says a colour is held to that colour;
- a picture that says nothing shows the product and no variant in particular (a
  detail, the back, the box) and is held to none — the cover included, when the
  product varies by colour;
- the cover of a product that does not vary by colour and says its own is held
  to it: that is every product an import brought in one colour.

A picture **has a colour to be held to** when the variants it shows all hold
the same `COLOR`. The seed today: 65 products declare one colour and have one
picture, so 40 of the 53 pictures drawn so far have one. No migration and no
new field on the picture — a reader over what the catalogue holds.

**Measured before designing** (the closed question in plain text, the nine
options the catalogue defines, 40 seed pictures, the 4B):

- The picture shows the declared colour in **37 of 40** (92 %); every answer
  was one of the options.
- The three that differ are three different things, and that is the design:
  - *stainless steel* declared, **grey** seen (0.96) — the same thing by two
    names: one is what it is made of;
  - *forest green* declared, **olive** seen (0.63) — neighbours, and the model
    unsure;
  - *black* declared, **grey** seen (0.94) — a laptop sleeve drawn in charcoal
    felt. The model is right about the drawing; whether charcoal is the shop's
    *black* is a person's call.
- At a confidence of 0.99 and above, 21 answers and none differs; at 0.95, 26
  and one.

**Decided here.**

1. **A doubt, not a claim.** A claim says something of the product and, once
   approved, is written to it. A colour that differs says something of the
   *picture*: it is the wrong picture, or drawn wrong, or filed under the wrong
   variant — the catalogue's colour was chosen by a person and is not the
   picture's to overwrite. Approving a claim *COLOR = grey* would be exactly the
   wrong repair. So it is its own record: the picture, the question, what was
   answered and how sure, what the catalogue holds, and what a person decided
   (*the picture is fine* · *replace the picture* · *it belongs to another
   variant*).
2. **Asked with the closed question** (`ClosedQuestion`), of the options the
   catalogue defines for `COLOR`, when the picture has a colour to be held to.
   The reading keeps the answer under its prose as it keeps the count.
3. **Neighbours are the catalogue's to declare, not code's to guess.** Whether
   *grey* answers for *stainless steel* is said where colours are defined (an
   option names the options it may be mistaken for), and a doubt between
   declared neighbours is not raised. With none declared, the three above are
   all raised — which is the honest default.
4. **Only a sure difference is a doubt.** Under a line (0.90, to be set from a
   measurement as `SureAnswer` was) the answer is kept in the reading and no
   doubt is raised: *olive* at 0.63 is the model not knowing.
5. **Where a person meets it**: beside the picture, in the same review that
   already shows a picture with its alt text (I-3) — one place where a picture
   is looked at, with everything said about it.

**Ruled by the author (2026-10-08).** A doubt **informs and blocks nothing**: a
product with an open doubt is published and sold as any other. The three
decisions are the ones for a drawn picture; an uploaded one (I-6) gains a
fourth, *the catalogue is wrong*, because a photograph of the real product is
better evidence than the colour somebody typed. And the seed gains a product
with two colours and a picture of each.

**As built.**

- `ProductPicture.Colour`: the colour a picture is held to, read by Catalog's
  pictures reader from what the catalogue already holds — no field was added.
  The colours a picture chooses among cross with it, and with what the
  catalogue declares may be taken for what (`mistakenFor` on an option, in the
  seed: stainless steel and grey), read in both directions.
- The reading asks the colour with `ClosedQuestion` and keeps the answer under
  its prose, in each language, with how sure the model was. Prompt
  `picture-reading-2026-10-08b`.
- `PictureDoubt`, in Knowledge: its own aggregate and table. Raised when the
  reading is recorded and the model is sure — `SureDifference`, **0.90** — of a
  colour the catalogue neither holds nor relates to the one it holds. Open →
  decided, or withdrawn when the next reading no longer says it; one already
  open is not raised twice, and one a person decided is not put to them again.
- `GET /api/knowledge/picture-doubts` and `POST …/{id}/decide`, the
  shopkeeper's, the decision audited. It records what the person said and
  changes nothing else.
- The backoffice's *Picture doubts* (`/doubts`): each row is the picture
  itself, what the model sees and what the catalogue says, and the three
  decisions — all quiet, because none is the one to be led to.
- Evidence of use: `tendero.knowledge.picture_doubts.decided`, by decision.
  Mostly *the picture is fine* means the line is too low, or two colours are
  one to the shop and should be declared so.
- **End to end on the forty seed pictures of one colour** (the local model,
  202 s with the sixteen of the count): 37 show the colour the catalogue
  holds; grey for stainless steel is related and raises nothing; olive for
  forest green came at 0.62 and raises nothing; **grey for the black felt
  sleeve at 0.94 is the one doubt**.

**The seed's product in two colours (built the same day).**

- **A source can tell its variants apart.** `ExternalProduct.Variants`: what
  tells each apart in the source's SKU, what it varies by in the source's own
  words, and which of the product's images is its own, by its place in the
  list. The mapper places each on the axes those words resolve to and declares
  the axes first; a product whose source tells none apart still gets its
  default variant. A default variant an earlier import left is retired when the
  source starts telling colours apart — the aggregate already did that for the
  shopkeeper's own matrix. A variant's picture is set only when every image
  arrived: with one missing, the list has no places to go by.
- **The seed file says it** (`variants`, in `products.sample.json`): the bike
  pannier `B13BAG0711`, olive and black, each with its picture. The colour is
  the variants' to say, so the product no longer says one of its own; its stock
  and both tariffs carry a row for each SKU. Chosen for being the least judged
  of the candidates in the search golden set — and the relevance gate still
  passes (lexical: en 0.748 against 0.730, es 0.692 against 0.670).
- **The catalogue is still a hundred products.** One of them now has two
  pictures; the picture tool reads a variant's picture as one more to draw,
  named for its file and in that variant's colour.
- **The black picture is the olive one in another colour, not a second
  drawing** (the author's ruling: *the two have to be the same and only the
  colour change*). Drawn from the description, the first was a flat silhouette
  and the second a detailed bag — another bag. So the tool gained what the
  hosted model is in fact for: it is shown the picture that exists and told to
  give it back with one change (`PromptTemplate.Recolour`, `IRecolourer`).
  - The model keeps the object and not what is around it: twice the black one
    came back with a grey haze above it, in the same place. `ReferenceBackground`
    restores the original's background — wherever the original has nothing
    drawn, the result gets background, and in a ten-pixel margin round the
    object only what is clearly drawn stays (the first version kept the margin
    whole, and the bag had a halo).
  - The result: the same pannier, rows 135–888 against the olive's 133–888,
    inside the safe area, on a clean background.
  - `generate` makes a variant's picture this way whenever the seed names one;
    `recolour --only <id> --colour <colour>` does it for any picture — tried on
    the same pannier in navy blue.
  - Not for the whole catalogue: for the products that test variants. Seven
    images in all, about twenty cents; some $1.50 of the three euros spent.
  - **Accepted by the author.**
- **Four things for variants, in the picture tool** (the author's ask, the same
  day; `tools/SeedImages/README.md`, *Another colour of the same product*):
  - **Still the same drawing?** `SameShape`: how much of the original's shape
    a recolour kept — where something is drawn, never what colour. On the
    finished files the black and the navy pannier share 94.0 % with the olive;
    the view from behind 86.7 %; two different bags 63.2 % and 42.4 %. The line
    is 90 %, set from those five numbers and no more. `same` asks it of any two
    files, and a recolour that falls under it fails.
  - **The colour that was asked for?** `recolour --check` asks a vision model
    in plain text. **And found the tool's own colour question wrong**: `Look`
    asks the colour inside a schema with two other answers, and called the
    terracotta pannier *black*; asked alone and in plain text the same model
    says terracotta. The grammar again (I-4b), in the place where it was first
    measured — the board's colour line, 9 of 11, was taken with it.
  - **The board, with the colour asked alone and in plain text** (the examiner
    now asks it so; the same twenty-eight pictures, re-recorded):

    | Colour | In the look's schema | Alone, in plain text |
    |---|---|---|
    | A recoloured picture caught | 9 of 11 | 8 of 11 |
    | A right picture doubted | 4 of 28 | **2 of 28** |
    | Doubts that were right | 9 of 13 | 8 of 10 |

    One catch fewer and half the false alarms. The two right pictures still
    doubted are the stainless steel coffee maker seen as grey — which the
    catalogue now says is the same thing, and the tool's examiner does not read
    yet — and the black felt sleeve seen as grey, which is the shop's one real
    doubt. Every other line of the board is as it was. `Look` still carries a
    colour in its schema and nothing reads it: the other two answers it gives
    were measured with it there.
  - **Every variant at once.** `generate --variants-of <id>`. The pannier
    gained a third colour for it: navy blue, with its SKU, stock and tariffs.
  - **Another view.** `view --only <id> --view back|side`. A redraw, not an
    edit that moves nothing: the pannier's back came back first with a
    backpack's shoulder straps, and with what it is said in a hint, with a
    rail and its hooks. The seed lists it as the pannier's fourth picture — one
    that is no variant's, so it is held to no colour, which the real import now
    proves. Every edit comes back with a pale smear above the object;
    `FloatingHaze` clears it for a view, which has no original to be held to.
  - The vision pass on the four: black and navy read as their colours; the
    cover and the back are flagged by the scene question, which lists a bag's
    own handle and buckle as other things — the false alarm it already had for
    bags (5 of 28).
  - Eleven images in all for the pannier, about thirty-three cents; some
    $1.65 of the three euros spent. **The navy and the back were accepted by
    the author.**
- **Imported for real** (`VariantPicturesFromTheSeedTests`: the committed seed,
  its pictures stored, Postgres, twice): two variants on the colour axis, each
  with its own picture, and the reader holds the cover to olive and the other
  picture to black. Asked directly, the model says *olive* at 0.996 and *black*
  at 0.977 — so the two pictures as filed raise nothing, and either of them
  filed under the other variant would raise a doubt.

**A doubt says when the colour seen is another variant's.** The reader gives
each picture the colours the product's other variants hold; a doubt whose colour
seen is one of them says so (`SeenOnAnotherVariant`), and the screen tells the
person the picture may be that variant's. It informs the third decision and
makes none.

**The model under it, changed** (2026-10-08, ADR 0057;
`docs/analysis/catalogue-model.md`, proposal 1). A variant no longer has a
picture; a picture says the colour it shows, and a colour's gallery is derived
— its own pictures, then the ones that say nothing.

- The seed says it on the image: `{ "file": "images/B13BAG0711-BLACK.webp",
  "shows": { "color": "negro" } }`, in the file's own words, and a plain string
  is still an image that says nothing. `variants[].image` is gone.
- The pannier, imported for real: olive, black and navy each say their colour,
  the back says nothing, and black's gallery is its own picture and then the
  back.
- A database from before is migrated, not reset: each picture a variant named
  now says that variant's colour — except a colour no longer sold and a picture
  two colours both named, which say nothing (`VariantImageMigrationTests`).
- The product's response keeps a variant's `imageId` and gains `imageIds`.
- Thirteen hand mutations, all red — after one test was strengthened (a tag
  given only when the gallery arrived whole had survived) and one written (a
  cover held to a colour the product once said of itself).

- **The storefront shows a colour's gallery.** Choosing a colour puts that
  colour's pictures on the page, then the ones that are every colour's; another
  colour's are not there at all. A variant with none to show has the product's
  own gallery, never an empty one.
- **The backoffice says it, with no model** (`SetImageShows`,
  `PUT /api/catalog/products/{id}/images/{imageId}/shows`). On a product's
  review, beside each picture and its alt text, one control an axis the product
  varies by, offering the values a variant still sold holds and *none in
  particular*. Chosen, it is written; refused — the last variant of that colour
  was discontinued meanwhile — the picture is named and the control goes back
  to what is stored. The review read gains each picture's `shows` and the
  product's `axes`.
- **Evidence of use** (ADR 0036): no event of its own. The change is an audited
  command — a row naming who said which picture shows what — and that row is
  the count; a metric beside it would say the same thing twice. What would be
  worth an event is the *doubt* this removes, and that one exists
  (`tendero.knowledge.picture_doubts.decided`).
- Twenty-one more hand mutations, all red: eleven on the slice and the review
  read, seven on the backoffice's store and three on the storefront's.

- **It belongs to another variant, applied** (the author's ruling,
  2026-10-08). Decided on a doubt, the picture is filed under the colour the
  model saw: the decision leaves Knowledge through the outbox with the picture
  and that colour as values, the worker sends Catalog its own command, and
  Catalog refuses a colour no variant is sold in — counted
  (`tendero.knowledge.picture_doubts.retagged`, by outcome), logged, not
  retried. What the picture shows on its other axes stays. The other two
  decisions change nothing, as before, and the screen says which is which.
  **Not built**: telling the person, on the screen, that Catalog refused.
- **The picture tool reads `mistakenFor`** as the shop does: a stainless steel
  coffee maker read as grey raises nothing. The board's colour line still
  records the two false alarms it had; it is owed a re-recording, which should
  leave one.
The fourth decision, for uploads (I-6). And captures of the screen, parked
until the page has stopped moving.

**Not in scope**: merging what several pictures of one product answer (three
pictures of the same set propose *pieces: 3* three times); it is the same
reader and comes with it.

### I-5 · Image embeddings, behind a gate

**First the tokenizer fixture** (`P13-1`): the text tower's SentencePiece
tokenization must reproduce the reference outputs exactly, or text-to-image
degrades without a sign. Until that test is green, nothing else in this step is
written.

**Green (2026-10-08).** `SiglipTextTokenizer` (`src/Search/Images`), over
`Microsoft.ML.Tokenizers` 2.0.0 (MIT at that version; it was already in the
graph), against what the maker's own tokenizer gives at the pinned revision
`75de2d5`: twenty-three texts — shop queries in both languages, accents,
digits, capitals, runs of spaces and a tab, an emoji, the empty text, one
longer than the window — **id for id, first run**. The reference is written by
a script in a throwaway Python container (the evaluation side of the
repository), committed beside the test, and never typed.

- The vocabulary's file is the maker's, 4 MB, and stays out of git
  (`models/siglip2/`); without it the test says how to fetch it and skips, as
  the other model-held tests do. **So CI does not run this gate**: it is run
  where the model is, and again whenever the revision moves.
- **Open, and found by the fixture**: the model's configuration says
  `do_lower_case: true` and the maker's tokenizer at this revision does not
  lower-case — *ZAPATILLAS* and *zapatillas* get different ids. Ours is
  faithful to the tokenizer. Whether a text is lower-cased before it is
  tokenized is to be settled by measuring retrieval both ways once the towers
  are exported (`P13-2`), not assumed from a flag.
- Five hand mutations red. The first, a cut one piece too short, was caught
  only by a shape test of ours and not by the reference — whose *long* text
  turned out to fit the window. It was lengthened, the script now refuses a
  long text that is not, and the mutation fails against the maker's ids.
- The open fork *SigLIP 2 in-process or sidecar* (roadmap, fork 6) had this
  test as its gate: **in process** stands.

Then `IImageEmbeddingGenerator`, a port with an in-process adapter: SigLIP 2
(`google/siglip2-base-patch16-224`, Apache-2.0, pinned revision) on ONNX Runtime,
**exported by us** from the maker's weights. No Python sidecar. A collection
keyed on `ImageId` — a content hash, so identical pictures are embedded once —
and named for the model, so SigLIP and SigLIP 2 vectors can never be mixed. Fed
by the outbox when a product is published or its picture changes; never inline in
a request.

### I-6 · Searching by a picture

`POST /api/search/by-image`, `AllowAnonymous` by explicit decision, rate-limited
as Tino's turns are: a picture in, the products whose pictures are nearest out,
as the shop's own cards. Then text against pictures, as a separate measured
endpoint — **not** a leg of the hybrid search until its effect on NDCG@10 is known.

In the storefront, the search box gains a way to give it a picture (a mock-up
first, in English). **Degraded path, tested**: with the embedding model or the
vector store down the control says it is not available and the lexical search is
untouched (invariant 8).

**The uploaded picture**: decoded and re-encoded in memory, which drops its EXIF;
bounded in size and type; embedded; discarded. It is never written to the image
store, a log or a trace — a span carries its dimensions and nothing else. `docs/privacy.md` says so.

### I-7 · Close

The phase's ADRs, Stryker over every project it touched with its survivors
classified, the suite's numbers, before and after captures, the notebook and the
book's map.

## Non-goals

- **Voice** — phase 14.
- **The kit solver** — phase 13c.
- A picture in a return request, or any picture of a customer's that is kept.
- Generating pictures as a feature of the shop: `SeedImages` stays an authoring tool.
- Image derivatives, background removal, a CDN: scale work, deferred with its number.
- A second model on the GPU. One serves text and pictures.

## Measures

- **The two new checks** on known-wrong pairs: misses and false alarms, before any
  generated picture is judged by them.
- **Each question on the known-bad set** (I-2b): caught and missed per kind of
  defect, recorded, and not allowed to fall when the model or a question changes.
- **Alt text**: every approved, edited and rejected proposal is the measure —
  the share approved unedited, per language.
- **Visual search**: an `images` suite in `tools/SearchEval`. Queries are pictures
  that are not in the index but have a known answer (the same product drawn by the
  other generator, a crop, a rotation); recall@1 and recall@5. Text against
  pictures is scored with the existing golden set and reported beside it, not
  gated.
- **The search gate does not move**: NDCG@10 stays at or above its threshold,
  because nothing here changes the hybrid search.
- Contract tests for the new ports; the degradations as tests, not comments.

## Evidence of use

| Event | The question it answers | The decision its value would change |
|---|---|---|
| `image_alt_proposal_decided` (approved, edited, rejected) | Is the proposed alt text good enough to approve as written? | Below a share approved unedited, the job stops proposing in bulk and proposes on request |
| `search_by_image_submitted` | Does anyone search with a picture? | If almost nobody does, the control leaves the search box for the search page |
| `search_by_image_result_followed` | Did the picture find what they wanted? | Whether text against pictures is worth making a leg of the main search |

## Licensing and dependencies (each needs the author's yes before it is added)

- **Weights, tier 1**: SigLIP 2 base (Apache-2.0) and Qwen3.5's vision projector,
  both at pinned revisions, both converted by us (ADR 0031).
- **Packages, new to `src/`**: `Microsoft.ML.OnnxRuntime` (MIT; `AI` references
  only `OnnxRuntimeGenAI` today), `Microsoft.ML.Tokenizers` (MIT), and SkiaSharp
  (MIT) to decode a picture — `tools/SeedImages` already uses it; not ImageSharp,
  whose licence is not permissive. Each checked at its exact version when added.
- **Paid**: Gemini for I-1 only.

## Open questions

1. **Where the picture control sits** in the search box — a mock-up settles it.
2. **The ceiling against the count.** *Settled 2026-10-07*: raised from 2 € to
   3 €. The 47 pictures left are about $1.41 with no redraw, and $1.29 is spent.
   The twenty products the catalogue is to gain have their own count when they
   have their own spec.
3. **`verify` has no model to ask on this machine.** It wants Ollama with
   `qwen3.5:9b`, and the only model installed there is a 27B that does not fit the
   card. Until I-2 gives the shop's own model eyes, a batch is checked by `probe`
   (pixels) and by a person's look at the contact sheet.

Settled on 2026-10-06, in the table at the top: the ceiling, what a batch is,
alt text by batches, and the packages.
