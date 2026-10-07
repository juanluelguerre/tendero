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

### I-5 · Image embeddings, behind a gate

**First the tokenizer fixture** (`P13-1`): the text tower's SentencePiece
tokenization must reproduce the reference outputs exactly, or text-to-image
degrades without a sign. Until that test is green, nothing else in this step is
written.

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
