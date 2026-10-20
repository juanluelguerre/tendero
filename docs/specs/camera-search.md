# Spec: Searching with the camera — in the search box and in Tino

**Status**: **stage 1 accepted** (2026-10-19): the scope, the flow and every decision stage 1 needs were taken by the author in conversation (*Decided by the author*, *Decisions*); stage 2's three decisions stay proposed until stage 2 is decided
**Follows**: phase 12b (Tino, `docs/specs/tino.md`) and phase 13a (images, I-6, `docs/specs/images.md`); before phase 15
**Branch**: `feature/camera-search`, cut from `develop`
**Slice(s)**: Search (an object detector behind a port, beside SigLIP; objects in a picture; `find_by_picture`; the by-image endpoint narrowed), `AI` (`read_picture`, stage 2), `Assist` (Tino's turn takes a picture), the storefront (one picture-search component, shared by the search box and Tino; the camera)
**Article**: *"Show the shop a photo"* (working title) — what a shop may make of a stranger's picture, and why the model that answers never sees it

## Problem

A shopper with something in front of them — a lamp at a friend's, the pan that
just broke, their own desk — has a picture and no words for it. Today:

- **The search box** takes a picture (I-6) and compares **the whole of it** with
  the catalogue's pictures. A photograph of a desk is a desk: the laptop, the
  keyboard and the bottle on it are one blur of likeness, and nothing lets the
  shopper say *those*, or *only that one*.
- **Tino** takes words and not pictures, so *algo como esto, pero por menos de
  40 €* has to be split in two by the person asking it.
- **The phone's camera** is reached only through a file picker.

What the shopper expects is what the large shops have taught them (the author,
2026-10-19, pointing at a *Lens*-style search): **send the picture as it is, or
mark the part that matters, and the shop finds what is in it** — every object it
recognises, with results for each at once, and a plain word for what it does not
sell. Words only add to it: *si vas a escribir, ¿para qué envías la foto? Como
mucho añadir algo, pero no describir lo que ya envías.*

## Decided by the author (2026-10-19)

| # | Question | Ruled |
|---|---|---|
| A | Which doors, and in what order | **The search box first, and it is needed whatever else happens**; Tino second, on the same component, **optional**: it may be postponed, or another phase brought forward before it, once the search box's evidence is in (*Two stages*). Asked first as *both doors*; reordered by the author the same day — the search is the more important door, and a large shop's assistant does not take pictures either |
| B | The flow | **Send the picture as it is, or first mark the part to search** (a person's glasses, not the person) — then only that part is read. **Unmarked, every object the shop recognises is searched**, and the results appear at once, **grouped by object**; **what the shop does not have is said** (*Algunos objetos de tu foto no están en la tienda: botella*). The text box stays, to narrow further, never to describe the picture |
| C | The camera | **The phone's camera from the page**, not only uploading a file |
| D | Text in a picture | **The brand, yes, as a closed question over the shop's brands** (*another brand* and *none visible* among the options); transcribing any other text, no. Asked about a pair of shoes with the maker's name printed on them |
| E | Barcodes, and Steward | **Later, and useful to Steward too** — Steward will scan, not take pictures (*Later: barcodes*) |

## Two stages

| Stage | What | Needs | When |
|---|---|---|---|
| **1 · The search box** | CS-1 to CS-4 and CS-6 to CS-9 for the search box: the detector, marking before sending, the blocks by object, the line of what is not sold, narrowing with words, the camera and its fallback, the picture suite and its floors | the CPU only: no GPU, no generative model | now; it is the spec's deliverable, released on its own |
| **2 · Tino** | CS-5, and the rows of CS-9 and CS-10 that name Tino: the attach switched on, the blocks as Tino's first answer, words after a picture as a conversation, `read_picture`, the multipart turn | the local model with its projector, so a GPU | **decided after stage 1's evidence** — `picture_search_used` in the search box after real use — and may be postponed, or another phase brought forward before it |

What would make stage 2 worth its cost: shoppers search with pictures, and **go on
asking** after the blocks (words added, products compared). If they rarely search
with pictures, or never write after one, Tino without a picture loses little and
the most expensive part — the turn, the reading on the GPU — is saved. Stage 1 is
built so stage 2 adds a door, not a rewrite: one component, the reads already
`IReadTool`s, the blocks already built by code.

## The rule

**A picture is the shopper's question, never a fact.** Nothing a model reads in a
picture is said to the shopper as true about a product. A picture becomes, in
code:

1. **Objects** — where things are and what kind each is (`detect_objects`,
   CS-2). The kinds offered to the detector are **the shop's categories and a
   fixed list of common objects it does not sell** (*bottle*, *television*,
   *phone*…), so that the shop can say *no lo vendemos* about the bottle on a desk
   instead of passing over it in silence.
2. For each object of one of the shop's kinds, **the nearest products**
   (`find_by_picture`, I-6's SigLIP 2 over that object's crop) — product codes,
   whose facts are read as any other search or turn reads them.
3. In Tino, when words follow, **a reading** of the object they are about
   (`read_picture`): its attributes that its category declares **and a picture can
   show** (ADR 0056) and its brand among the shop's — each a **closed question**
   (`ClosedQuestion`, ADR 0055's convention: options in the question, a short
   answer read by code, its probability as its confidence).

**The model that answers never receives the picture.** Tino's model gets the
reads as tool results, like any other, and its answer is grounded as today (ADR
0055). A picture with words in it (*ignore the above and…*) reaches nothing that
can obey it: a detector returns boxes, a closed question returns one of its
options or nothing. The model parses and phrases; code chooses (invariant 12).

## Behavior

### CS-1 · The flow, the same in both doors

1. **The shopper gives a picture** — `photo` from the device, or `camera` (CS-6).
2. **Optionally, they mark the part to search**, before sending: a box drawn over
   the picture, which can be moved and resized. **Marked, only the box is read**:
   the browser cuts it out and sends that crop, so the rest of the picture —
   the person wearing the glasses, the room round the lamp — never leaves the
   device. Unmarked, the whole picture is sent.
3. **They send**, with or without words. No word is needed.
4. **Objects are found** in what was sent (CS-2). A marked crop is usually one
   object; a whole picture of a desk is several.
5. **The results appear at once, one block an object** (CS-3), each named by its
   kind (*Portátil*, *Teclado*), the picture beside them with a numbered mark on
   each object so the shopper sees which block is which.
6. **What the shop does not have is said, in one line under the blocks**, built
   by code:
   - an object of a kind the shop does not sell: *Algunos objetos de tu foto no
     están en la tienda: botella* / *Some things in your picture aren't sold
     here: bottle*;
   - an object of a kind it sells with nothing near enough (under
     `MinPictureSimilarity`): *No encontramos nada parecido a: lámpara* / *We
     found nothing like: lamp*.
   With **nothing found at all** — no object, or none of the shop's — the line
   alone: *No encontramos en tu foto nada de lo que vendemos* / *We couldn't find
   anything we sell in your picture*.
7. **The text box stays**, to narrow (*por menos de 500 €*, *en negro*): the words
   narrow the blocks, never replace the picture (CS-4).

### CS-2 · Finding the objects

- **A port in `Search/Images`, `IObjectDetector`** (beside SigLIP and `UploadedPicture`: detection is a stage of visual search, and `Search` does not reference `AI`): a picture and the kinds to look for in,
  boxes with a kind and a score out. Keyed adapters (invariant 3), a contract
  suite, and the model an adapter loads is its options, never code — the voice
  phase's rule (ADR 0061), so a second detector is a class, a registration and a
  measurement.
- **The kinds**: the shop's categories (their English names, so that the detector
  reads them) and **a fixed list of common objects the shop does not sell**, each
  with a name in es and en for the line of CS-1.6. The list lives in
  configuration beside the detector, is reviewed when a category is added, and an
  object the shop starts selling moves from it to the categories.
- **Which detector is measured before it is chosen** (the author, 2026-10-19,
  asking why not a model the shop already runs). Three candidates, each an
  adapter of the port, on the author's photographs:

  | Candidate | What it is | For it | Against it |
  |---|---|---|---|
  | **SigLIP 2 over a grid** | the visual search's own model, scoring crops of a grid against the kinds' names | already in process, on the CPU; no new dependency | it places nothing — boxes are grid cells — and it costs tens of passes a picture; the cheap reference |
  | **Qwen3.5-4B's boxes** | the local model with its projector, asked for the objects' coordinates | already converted and measured; no new dependency; understands far more than a detector | **a GPU**, so no detection on a machine without one; **one slot shared with Steward and Tino**, so a shopper's search queues behind a chat; it writes coordinates as text and can invent an object; slower by how it works, by how much unmeasured, nor whether its boxes survive the projector's resizing in our llama.cpp |
  | **OWLv2** | an open-vocabulary detector — it looks for whatever kinds it is given as words — built on a CLIP-like backbone with a head trained to place boxes; **in process on ONNX** as SigLIP 2 and Parakeet are, converted by us from a pinned revision (ADR 0031) | the CPU, so the search box needs no GPU; queues behind nothing; scores boxes rather than writing them | **a new dependency**: Apache-2.0 weights, tier 1, checked at the exact revision — tried outside `src/` and its download deleted if not chosen; added only with the author's yes, with the numbers in front of them |

  Measured, per candidate: objects found and missed, kinds right, objects
  invented, time per picture on the CPU and on the GPU, in
  `docs/analysis/object-detectors.md`. **Chosen: OWLv2** (the author, 2026-10-19,
  ADR 0062): the only one whose score can trade what is found against what is
  invented, on the CPU in 2.4 s, queued behind no agent. Its weights are tier 1,
  converted by `src/AppHost/scripts/prepare-owlv2.py` into `models/owlv2`; its
  text tower is exported too and CLIP's tokenizer is ours, so the kinds stay
  configuration. SigLIP's grid and the Qwen's boxes stay as the suite's adapters,
  not registered, as Parakeet stayed beside Whisper. OWLv2 replaces neither model
  for anything else: its text side is English only, and it answers where, never
  what colour or which brand.
- **At most one box a kind and eight in all**, by score; under the score floor
  (chosen from the measurement, CS-10) a box is not counted. Overlapping boxes of
  one kind are one object. **With no box found**, the whole of what was sent is
  one object of no known kind, searched by likeness as I-6 does today.
- **The endpoint**: `POST /api/search/by-image/objects` (multipart, `picture`),
  `AllowAnonymous` by explicit decision, on visual search's own budget (twelve a
  minute). It answers the boxes in the picture's own coordinates after its
  orientation, each with its kind and whether the shop sells that kind. It keeps
  nothing (CS-7).
  **Built 2026-10-19** (`Search/Features/FindObjectsInPicture`): the objects
  numbered in reading order (by thirds of the height, then left to right), each
  with its kind, `sold`, its name in the culture asked (the category's, or the
  object's from `object-kinds.json`) and its score; the picture's upright width
  and height. On a budget of its own rather than visual search's — twelve
  photographs a minute — because a detection costs two to four seconds and a
  crop's search 121 ms. Refusals with codes (ADR 0059): `PictureNotTaken`,
  `ObjectsNotAvailable`, `ObjectsNotAnswering`. The floor is
  `Search:Objects:MinScore`, 0.4 provisional. The status gained `objects`.
  **Open, to measure with the sheet**: the crops are searched one request each on
  visual search's twelve a minute, so a photograph with four objects of the shop's
  spends five of them; whether that budget moves is for when the sheet exists.

### CS-3 · The results, grouped by object

- **The browser crops and asks.** It holds the picture; for each object of a kind
  the shop sells it cuts the box out and posts the crop to
  `POST /api/search/by-image`, with the object's category (CS-4), in parallel. The
  server stays stateless and the picture is never held between calls.
- **A block an object**, in the picture's reading order (left to right, top to
  bottom): the kind as a heading with its number, then up to four of the shop's
  cards as *parecidos* — the word the search notice uses when nothing matched
  exactly — never *el mismo*, and a *Ver todos* that opens the full list for that
  object. On a phone the blocks stack; on a desktop the picture sits beside them.
- **The picture's marks and the blocks point at each other**: tapping a mark
  scrolls to its block, and a block's heading outlines its object.
- **Correcting**: a mark's box can be moved or resized, and a new one drawn for
  something the detector missed; each change re-asks for that block alone.
  **Not built in stage 1's first run**: the approved mock-ups draw marking a
  part before sending (and *Mark a part* after the answer, which goes back to
  it), not dragging a box the detector drew. Whether it is worth building waits
  for `marked` in the tracking plan.
- **Built 2026-10-19** (`storefront/…/visual-search/picture-sheet/`): the sheet
  a native modal `<dialog>`, full screen under `md`; the browser asks for the
  objects, cuts each one out and asks for its crop within its category, all at
  once, and abandons what is still out when the picture is replaced or the words
  change (each crop spends from the minute's twelve). An object answered with
  nothing near enough has no block — the line *We found nothing like* names it.
  The tracking plan's existing `search_by_image_submitted` grew `marked`,
  `objects`, `not_sold`, `words` and a `viewfinder` source rather than a second
  event (*Signals*), counted once a picture, when the sheet is put away.

### CS-4 · Narrowing with words

- Words typed after the results narrow **every block**: the nearest products
  **within** the object's category that the lexical search also matches for the
  words (*negro*, and *por menos de 500 €* through the price filter the search
  already parses) — the by-image endpoint takes `q` and `category` for it
  (proposed, decision 5). The picture leads; the words narrow.
  **Built 2026-10-19**: `VisualSearch.AmongAsync` asks the lexical search for the
  words in the category (its branch), up to 500 products, and the picture store
  is queried among them by the payload field it already indexes (`products`), so
  nothing is re-embedded. Words that match nothing there are an empty block, never
  every product. **Found while building**: the lexical search does not parse a
  price out of the words — *por menos de 500 €* is matched as words, not as a
  price filter — so the price half of this line waits for query understanding to
  read prices, and is not claimed until then.
- A block the words empty says so (*Nada de esto en negro*), and the line of
  CS-1.6 is unchanged: the bottle is still not sold, whatever the words.
- Words that name one object (*el teclado, pero inalámbrico*) are, in the search
  box, words like any other; in Tino they are a question, and Tino's turn decides
  what they are about (CS-5).

### CS-5 · In Tino (stage 2)

- The composer's attach rule, built and switched off on 2026-10-17 (board 04), is
  switched on for Tino: **`photo` and `camera` on a phone**, behind its "+";
  **`photo` alone on a desktop**, its own icon. Marking the part to search happens
  in the composer, on the picture's chip, before sending.
- **A picture sent is answered first with the blocks of CS-3**, drawn in the
  conversation as Tino's answer: built by code from the reads, the shop's own
  cards, the line of what is not sold — **no model writes it**, so a picture of a
  desk needs no model call to be answered. The shopper's bubble shows their
  picture (or their crop) from a local object URL, with the numbered marks; the
  server never returns it.
- **Words after it are a conversation, as Tino's always is.** *¿Cuál de los
  portátiles pesa menos?*, *el teclado, pero inalámbrico*: the turn runs with the
  picture's reads among its facts — the objects, their kinds, their products —
  and with `read_picture` over the object the words are about, and answers as
  today (`products`, `ask`, `noData`), each reason citing **that product's** facts
  under ADR 0055's three checks. A reason never cites the picture: *Color: negro*
  from the data sheet, never *same colour as your photo*.
- **What Tino read is said, built by code** above such an answer: *En tu foto: un
  teclado · negro · Vertex* / *In your picture: a keyboard · black · Vertex*, from
  the reading's options, never the model's words. A reading under its confidence
  floor is neither shown nor used (proposed, decision 2).
- **The reads travel on, the picture does not.** The answer carries them
  (`picture: { objects: [{ kind, sold, box }], reading? }`), and the browser sends
  them back with the conversation as the shopper's own words, so a third question
  needs no second upload. Sent back they are untrusted text, as typed text is:
  forging them is typing them. A question about an object whose crop is needed
  again (`read_picture` on another object) sends that object's crop, never the
  whole picture.

### CS-6 · The camera

- **`photo`** opens the picker on the device's pictures.
- **`camera` opens a viewfinder in the page** (`getUserMedia`, the rear camera on
  a phone): full screen, a shutter, a close button, a hint line. The shutter
  freezes a frame, and that frame is the picture — marked or not, it goes on as
  CS-1 says. Objects are found on the frozen frame, not live: a detector on every
  frame is a server's worth of CPU per shopper.
- **Permission refused, no camera, or no `getUserMedia`**: `camera` falls back to
  the phone's own camera app (`<input type="file" accept="image/*" capture>`), so
  it never simply fails. Neither is offered on a desktop.
- The viewfinder exists for barcodes too: reading them live from its frames is
  what *Later: barcodes* adds, with no change to this component's shape.
- **Before sending, the browser scales the picture** to 1,536 pixels on its long
  side as a JPEG (my call): a phone's photograph is megabytes of upload for what
  the detector and SigLIP read at a fraction of it. A marked crop is sent at its
  own size, scaled down only past that.

### CS-7 · The picture is not kept

Decoded in memory for the reads and discarded: never written to the image store,
a log, a trace or telemetry; a span carries its dimensions and type, as I-6's
does. Taken as I-6 takes it (`UploadedPicture`: JPEG, PNG or WebP by its bytes,
at most 8 MB and 4,096 a side, turned by its orientation and nothing else of its
metadata). A marked crop is the only part of the picture that leaves the device.
**With the local models nothing leaves the machine.** No hosted profile reads a
shopper's picture (proposed, decision 6). `docs/privacy.md` gains the paragraph,
the camera's permission included.

### CS-8 · Where it lives in the search box

- The camera control the search box already has (I-6) opens **the picture
  sheet**: full screen on a phone, a dialog on a desktop — the picture, the
  marking, the blocks, the line of what is not sold, the field to narrow.
- **Marks, boxes and blocks are reachable without a pointer**: each object is a
  button named by its number and kind (*1 · Portátil*), arrows move between them,
  a box can be drawn, moved and resized with the keyboard. Labels in es and en.
  Neutral controls; the page's one clay action stays *Añadir al carrito*.

### CS-9 · The degraded path (invariant 8), tested

| What is missing | Search box | Tino |
|---|---|---|
| the detector | what was sent is one object, searched by likeness (I-6 today); the shopper can still mark the part | the same |
| visual search (model or store) | no camera, as today | no attach |
| a profile that reads pictures | unchanged: it never needed one | the blocks still answer a picture; words after it are answered without `read_picture` and without the *En tu foto* line |
| Tino | unchanged | no Tino |
| the camera | `camera` opens the phone's camera app | the same |
| a block's search fails | that block says so; the others stand | the same |

Each row is a unit test; all but the last are also browser specs, against the
scripted profile where a model is involved (it already answers a step only when
the message carries a picture, I-2).

### CS-10 · The models and their cost

- **The detector and SigLIP run in process, on the CPU**; their latency on the
  API's CPU, for one object and for eight, is a number this spec owes before the
  sheet waits on it (CS-11).
- **The reading** runs on Tino's profile when it reads pictures — today the local
  Qwen3.5-4B with its projector, the one slot shared with Steward — and only when
  words follow a picture. Metered under `tino.turn` with the picture's tokens in
  `Usage`, inside the shop-wide daily budget.
- A Tino turn with a picture spends from **both** budgets it touches: Tino's and
  visual search's. The turn's 20-second cap holds; if the reading pushes past it,
  its questions are cut — brand, then attributes — before the cap moves.

## Non-goals

- **Questions about the picture itself**: *¿me pega en el salón?*, *¿qué le pasa a
  mi sartén?*. Opinions and diagnoses no fact the shop holds can carry.
- **Transcribing text in a picture** — a model number, a label, a receipt
  (decision D).
- **Naming what the detector was not given**: an object of no kind on either list
  is not reported, and the line of CS-1.6 names only what it recognised.
- **Detecting objects live** in the viewfinder; several pictures in a turn; a
  picture kept across the conversation.
- **A hosted profile** reading pictures (decision 6).
- **Pictures over MCP or UCP**: an agent has its own eyes and the catalogue's
  pictures.

## Later: barcodes (the author, 2026-10-19)

Not built here, written down so this spec does not close the door. A barcode is
the opposite of a model: it names **one variant exactly**, with no *parecidos*
and no confidence, so where a picture carries one it wins over everything above.

- **The catalogue has no code today** — no GTIN or EAN in the code or the seed.
  First, a **GTIN per variant** in Catalog, through the connectors' contract and
  the seed (valid check digits).
- **Reading it needs no model**: the browser's `BarcodeDetector`, feature-detected
  (Chrome on Android has it; Firefox and desktop Safari do not), live in this
  spec's viewfinder. A fallback library (ZXing, Apache-2.0) is an npm dependency,
  the author's to approve.
- **Steward is where it pays most**: receiving stock (scan, Steward proposes the
  movement, a person approves — invariant 12), counting a shelf against Inventory,
  matching a returned item to its open return, finding a product without its name.
  For the shopper: *¿lo tenéis, y a cuánto?* from inside another shop. And an
  agent comparing shops asks by GTIN.
- **Steward scans and does not take pictures** (the author, 2026-10-19). The
  composer is the same one (`libs/shared/composer`), and each surface says what it
  offers through `attach`: barcodes add a kind, `scan`, beside `photo` and
  `camera`. The search box and Tino offer all three; **Steward offers `scan`
  alone**, through the same viewfinder, and a scan leads to the exact variant and
  from there to an action proposed for approval. A shopkeeper does not search by
  likeness — they know their catalogue and want *that* item, which a code names
  better than any picture — and a product's own pictures are uploaded on its
  screen, where I-3 and I-4 already read them.
- **Two cases that could one day give Steward a picture**, each a decision of its
  own and not this spec's: *which product is this?* for an item with no label (a
  return that came back unwrapped), which the picture search answers by likeness
  where no code exists; and **reading a supplier's delivery note** so Steward
  proposes the stock it lists — valuable in a real shop, and transcription, which
  this spec leaves out (decision D). Since `attach` is configuration, either is a
  change of setting plus its own spec, not new machinery.

## Measures

- **A picture suite in `tools/SearchEval`**, no judge. Each case is a photograph,
  optionally a marked box, and what it must give: the objects (kind, sold or not,
  roughly where), for each sold one the products that may appear in its first
  four, and the line of CS-1.6. Tino cases add words, the reading's attributes
  and brand, and the facts the answer must not state. Reported: objects found and
  missed, kinds right, *not sold* right, products right per block, brand right,
  forbidden facts shown (must be zero), latency per step and for the whole
  picture, the picture's tokens.
- **The photographs come from two sources, reported apart** (decision 3, the
  author, 2026-10-19). None is the catalogue's own: a catalogue picture finds
  itself and proves nothing — and the seed's are illustrations drawn for the
  repository (`seed/IMAGES.md`), so a photograph of the same kind of thing is a
  fair question.
  - **Amazon Berkeley Objects** (CC BY 4.0, the licence article 02 checked;
    attributed in `THIRD-PARTY-NOTICES.md`): **sixty to a hundred single objects**
    across the catalogue's families, and objects of kinds the shop does not sell.
    Their expected kind comes from ABO's own product type through a table from
    its types to the shop's categories, so no case is labelled by hand. Taken at
    their original size (the small set is too small to stand for a 1,536-pixel
    upload), only the ones used are downloaded where the bucket allows it,
    outside `src/`, and nothing is left in `artifacts/` afterwards. Public, so
    the suite runs on a public clone. **Studio photographs flatter a detector** —
    one product, lit, on white — the reason the voice suite used the author's
    voice and not a synthetic one; so they are never the only number.
  - **The author's own, eight to ten**, for what ABO cannot give: **five or six
    real scenes with several objects** — a desk, a kitchen counter, a shelf —
    mixing what the shop sells and what it does not; **two or three of the author
    wearing an accessory**, to be marked; optionally one or two single objects at
    home, to compare with their studio counterpart. The originals go to
    `artifacts/camera-photos/` (ignored by git: their EXIF holds a location);
    the copies the suite reads are redrawn in Chromium as the voice recordings
    were — orientation applied, **no metadata**, scaled to 1,536 pixels as the
    shopper's browser will — into `design/unpublished/camera-photos/`, private
    until an article publishes them. JPEG (HEIC is refused by `UploadedPicture`),
    no face but the author's, no paper with an address on it.
  - **A brand of the shop's cannot be photographed**: the seed's brands are
    invented. Stage 2's reading of a brand is held by real brands (*another
    brand*) and none visible; the shop's own brands, by unit tests — or a
    catalogue picture photographed on a screen, a weak case, if the author wants
    one.
- **The floors are chosen here**: the detector's score floor, the reading's
  confidence floor, and `Search:Images:MinPictureSimilarity`, unset since I-6 and
  waiting for exactly this.
- Unit: the detector's port and its options, the two lists of kinds, one object
  a kind, overlapping boxes merged, the whole picture when none is found, the
  crop narrowed by `q` and `category`, the line of CS-1.6 in each of its three
  forms, the multipart turn, the blocks built with no model call, a reading under
  its floor, the reads sent back as text, a reason citing the picture dropped,
  every row of CS-9.
- Contract: `IObjectDetector` against a fake and the ONNX adapter on real
  photographs; the vision profile answering a closed question about one (skips
  where no such server runs).
- Architecture: `Assist` still references only the kernel and `AI`;
  `find_by_picture` and `detect_objects` are `IReadTool`s with an owner.
- Browser: in both doors, a marked crop and an unmarked desk; blocks and marks
  pointing at each other; a box moved and one drawn; words narrowing; the line of
  what is not sold; the viewfinder with a camera faked by the browser and its
  fallback; the degraded rows; keyboard only, once through.
- Mutation: a hand mutation per test written for code that exists; Stryker over
  `AI`, `Search` and `Assist` scoped to the files touched, at the close.

## Evidence of use

| Event | The question it answers | The decision its value would change |
|---|---|---|
| `picture_search_used` (where: search or tino; source: photo, viewfinder or camera app; marked: yes or no; objects found; objects not sold; boxes corrected; words added; outcome: products, partly, nothing). **Built as the existing `search_by_image_submitted` grown** (2026-10-19): `how` gained `viewfinder`, and `marked`, `objects`, `not_sold`, `words` were added — one event a submission, not two; `where` arrives with stage 2, and *boxes corrected* with correcting | Do shoppers search with pictures, from where, do they mark, and how much of what they photograph does the shop have? | Almost nobody in a door after a month: it leaves that door. Mostly marked: the detector matters less than the box, and its cost is questioned. Many objects not sold: the catalogue's gap, measured — a list of what shoppers photograph and the shop lacks, by kind |
| `search_result_followed` and `tino_result_followed` gain `from: picture` and the block's position. **Built as `search_by_image_result_followed` gaining `block`** (2026-10-19): a product opened from a picture already had its own event | Does a picture lead to a product more than words do, and from the first block only? | If not, the detector and the reading cost CPU and GPU for nothing words did not do |

None carries the picture, its boxes, the reading or the words; the kinds counted
as not sold are category or list names, never free text. All follow the
storefront's consent.

## Decisions (stage 1's taken by the author on 2026-10-19; stage 2's proposed)

| # | Question | Ruled or proposed |
|---|---|---|
| 1 | What a picture is | **Ruled: the shopper's question, never a fact**: reads made by code, and the model that answers never sees the pixels (*The rule*) |
| 2 | *(stage 2)* Is what Tino read said? | **Yes**, one line built by code above an answer to words about a picture (*En tu foto: …*), read only; removable chips later if the evidence shows shoppers correct it |
| 3 | The photographs the suite uses | **Ruled: two sources, reported apart** — sixty to a hundred single objects and kinds not sold from Amazon Berkeley Objects, labelled from its product types; and eight to ten of the author's, for real scenes and an accessory to mark (*Measures*). Asked first as twenty-five of the author's; ABO proposed by the author |
| 4 | The detector | **Ruled: measured before it is chosen** — SigLIP 2 over a grid, the Qwen3.5-4B's boxes and OWLv2, on the suite's photographs (CS-2); the author picks with the numbers, and OWLv2's dependency is asked for only if it is the one picked. **Picked: OWLv2, its weights added as tier 1** (the author, 2026-10-19; ADR 0062) |
| 5 | How words narrow a picture's results | **Ruled: within each object's category, filtered by the lexical search for the words**; the picture leads |
| 6 | *(stage 2)* A hosted profile reading shoppers' pictures | **No**, until it is a decision of its own with `docs/privacy.md`; Tino's answering model could be hosted one day without it, since it never sees the picture |
| 7 | *(stage 2)* Between Tino's turns | **The reads travel in the browser's conversation as the shopper's words; the picture is sent once and never kept**, and only a crop is ever sent again |

My calls, which a builder may revisit without changing what is built: the
1,536-pixel scale-down; one object a kind and eight in all; four cards a block;
the browser cropping rather than the server holding the picture; a picture's
first answer in Tino built by code with no model call; and cutting the reading's
questions before raising the turn's cap.

## Building it, in order

**Stage 1 · The search box** — needed whatever else happens.

- `CS-1` Mock-ups first, in English: marking before sending, the blocks with
  their numbered marks, the line of what is not sold, the sheet on a phone and a
  desktop, the viewfinder.
- `CS-2` `IObjectDetector`, its contract suite and the two lists of kinds; the
  three candidates measured on the author's photographs
  (`docs/analysis/object-detectors.md`), the author's choice, and only then the
  chosen one's conversion (ADR 0031) and `POST /api/search/by-image/objects`; `q`
  and `category` on the by-image search. The photographs (`CS-4`) are therefore
  needed before this step, not after it. `detect_objects` and `find_by_picture` built as
  `IReadTool`s now, so stage 2 finds them ready.
- `CS-3` The shared picture-search component and the search box's sheet, the
  camera and its fallback, the browser specs. **Built 2026-10-19**: the sheet,
  the viewfinder and its fallback; `visual-search.spec.ts` (eight) and
  `camera.spec.ts` (two, on Chromium's fake camera) hold CS-9's rows in a browser.
  `detect_objects` and `find_by_picture` as `IReadTool`s wait for stage 2, which is
  the only reader they have.
- `CS-4` The photographs — ABO's chosen and labelled, the author's redrawn — the
  picture suite, the three floors, `docs/privacy.md`, ABO's attribution.
- `CS-5` The tracking plan's events, an ADR (*a picture is a question, not a
  fact*), Stryker scoped, the notebook, the close of stage 1.

**Stage 2 · Tino** — optional; decided on stage 1's evidence, and may wait
behind another phase.

- `CS-6` Tino: the attach switched on, marking in the composer, the blocks as its
  first answer, `read_picture`, the multipart turn, the reads in the answer and
  back, the *En tu foto* line; regenerate the OpenAPI document and the frontend
  types; the picture suite's Tino cases; Stryker scoped and the close.

## Open questions

Stage 1 has every decision it needs (1, 3, 4 and 5, ruled 2026-10-19). Decisions 2, 6 and 7 are needed only if stage 2 is
built. Each is the author's and each changes what is built.
