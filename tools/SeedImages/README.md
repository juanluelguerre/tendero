# SeedImages

Draws the product illustrations in `seed/images/`. It runs **on a developer's
machine**, once for the ones that are missing and occasionally to redo one. It
does not enter CI: there is no service container with a GPU, and this is an
authoring tool, like `dotnet-ef`.

## Two generators, one port

`IIllustrationGenerator` is a port with keyed adapters, which is ADR 0003 applied
to one more external system: text goes in, pixels come out, and everything after
that — the safe area, the repainted background, the WebP, the vision pass — is
shared.

| `--provider` | what it is | per image | cost | reproducible |
|---|---|---|---|---|
| `gemini` (default) | one HTTPS call to `gemini-2.5-flash-image`, the model known as nano banana | ~6 s | ~$0.03, so **~$3 for the hundred** | no — there is no seed, so a redraw is a new roll |
| `sdxl` | Stable Diffusion XL inside this process, on ONNX Runtime with DirectML | ~76 s | nothing | yes — the noise is seeded from the `item_id` |

**The hosted one drew the catalogue, and the reason is quality rather than
speed.** The first eight illustrations were made by hand with nano banana, and
SDXL base 1.0 beside them is flat, grey and soft at the edges; a white product
disappears against the background entirely. Six prompt rewrites did not close the
gap, because the gap is the model. A catalogue with two visual registers is not a
catalogue, so the hundred were drawn where the eight came from.

**The local one stays, and not out of sentiment.** It is the only path that needs
no key, no network and no bill, and the only one that reproduces an image from
its `item_id`. The port declares that difference as `IsDeterministic` rather than
leaving a person to discover it after relying on it.

The key is read from `GEMINI_API_KEY` and from nowhere else — never a file, never
a commit — and it travels in a header rather than the query string, where every
proxy between here and there would log it. The free tier grants **zero** image
generation (the quota panels read `0/0`, which looks exactly like a quota you
have exhausted), so `gemini` needs prepaid credit on the project the key belongs
to.

## State

Working. Both generators draw, and the vision pass reviews:

```bash
# Draw the ones that are missing, hosted. About 6 seconds each.
dotnet run --project tools/SeedImages -- generate --take 5
dotnet run --project tools/SeedImages -- generate --only B073WXYZ01 --force

# The same, locally. About 76 seconds each on an RTX 5070 Laptop.
dotnet run --project tools/SeedImages -- generate --provider sdxl --models <dir> --take 5

# Ask a local vision model what it sees in the finished files.
dotnet run --project tools/SeedImages -- verify

# Where the object sits, by counting pixels. Milliseconds, no model.
dotnet run --project tools/SeedImages -- probe

# The prompts, loading no model at all. Instant.
dotnet run --project tools/SeedImages -- dry-run --take 5

# What each line of the prompt costs against CLIP's window.
dotnet run --project tools/SeedImages -- tokens --models <dir>

# What the ONNX graphs really declare: tensor names, types, shapes.
dotnet run --project tools/SeedImages -- inspect --models <dir> --ep dml
```

`inspect` exists because an SDXL export is five graphs whose input and output
names **differ between exporters**, and a pipeline written against the wrong ones
does not fail — it produces noise. Running it against a new model costs two
minutes and saves two days. It reports a missing component and steps over it
rather than refusing, because the moment it is most useful is on a half-finished
download.

## The model, for `--provider sdxl` only

It is not downloaded for you and it is not in the repository: almost 10 GB, and
`models/` is in `.gitignore`. `--models` is required by that provider and ignored
by the hosted one, which needs a key instead. What is needed is an ONNX export of
**SDXL base 1.0
optimised for DirectML**, in the diffusers folder layout:

```
<dir>/
  scheduler/scheduler_config.json
  tokenizer/{vocab.json, merges.txt, special_tokens_map.json}
  tokenizer_2/{...}
  text_encoder/model.onnx
  text_encoder_2/model.onnx (+ .data)
  unet/model.onnx (+ .data)
  vae_decoder/model.onnx
```

**Check its licence before downloading it** (ADR 0006). The test is not the one
a package gets: the weights are never distributed — they run once, here — and
what does leave is the hundred images. So **a generator's licence is read by
looking at what it says about its outputs**. `openrail++` explicitly disclaims
any rights over them and passes; a non-commercial model does not, because that
restriction reaches the files that end up in the repository.

## Why DirectML and not CUDA

Because on a Blackwell GPU — the RTX 50 series — **ONNX Runtime's CUDA provider
ships no compiled kernels**. onnxruntime issue 26177 is closed and its answer is
to build ONNX Runtime from source with `CMAKE_CUDA_ARCHITECTURES=120`, patching
two headers along the way. The export the ONNX Runtime team publishes for CUDA
says as much on its own model card: *"It cannot run in other execution providers
like CPU or DirectML"* — and it does not: its UNet fails to load on both, on
`com.microsoft.SkipGroupNorm` and `com.microsoft.NhwcConv`, which are CUDA
contrib kernels.

DirectML accelerates on any DX12 GPU, needs nothing installed, and knows nothing
about CUDA architectures. It is 1.5 to 2.5 times slower, and slower beats CPU.

**CUDA is recorded as an option for later**, with the number that defers it:
ninety-two images are an hour and a half to three hours unattended on DirectML,
and half an hour to one on CUDA. Saving an hour on a job that runs by itself does
not pay for a from-source build — the scale rule in `CLAUDE.md`, with its
measured number. If it is ever wanted, `inspect --ep cuda` says in two minutes
whether it binds.

## One step count, for the whole catalogue

The schedule this export declares uses `leading` spacing, where the top of the
noise ladder is `(steps - 1) * (1000 / steps) + 1` — which is not 999, and which
**moves with the step count**. Measured: the starting noise level is 11.07 at
twenty steps, 11.52 at thirty and 13.16 at fifty.

So `--steps` is not a quality dial. Changing it changes the picture rather than
its refinement, and a catalogue where six images came from a different ladder
does not look like a catalogue. **Pick a number, generate all hundred with it,
and record it** — regenerating one later at a different count is a different
illustration, not a better one.

## Two passes, because they cannot share the card

The UNet is about five gigabytes and a vision model is several more, on a card
that has eight. So `generate` draws a batch and `verify` looks at the batch
afterwards. Interleaving them would thrash the GPU and turn a two-hour run into
an afternoon.

**`generate` guarantees what arithmetic can guarantee.** It writes 1024px WebP
— the size every source actually produces, so nothing is ever enlarged — and it
measures the object and insets it to the central 75%, because the result card crops to 4:3 and asking
SDXL for a margin produced 94% three rewrites running. It repaints the background
to `#FAF9F7`, because the greys the model chose across one batch were #CACDCE,
#C4C4C7, #B4B5B7, #99A09F and #A2A3A4 — fifty levels apart, and a hundred of
those would not look like one catalogue. And when the ink reaches all four edges
it tries another seed, up to three, because that is a tiled sheet of the product
rather than one of it.

**`verify` asks the questions pixels cannot answer.** A tiled sheet fills the
frame and is caught by counting; a lamp standing on a desk leaves a perfectly
good margin. Three closed questions per image, with a JSON schema and temperature
zero: is there text, what colour is the object, and is anything drawn besides the
product. Text or a scene marks the image to be drawn again. **A colour mismatch is
a note and never a retry** — `seed/IMAGES-TODO.md` already decided the cheap
repair there is to change the catalogue, and that is a person's call.

**Two more questions since phase 13a**, because a picture is accepted only when it
is right for its product and drawn as the others are:

- **Is it the product described?** The model says what it sees, is then told the
  product's name and description, and says whether they match and what differs.
- **Is it drawn as the reference is?** The candidate beside one of the pictures
  drawn by hand (`--reference`, `B10SHO0101` by default): the same flat
  illustration, the same plain background.

**And two whose answer is a value, not a verdict** (phase 13a, I-2b): the model
reads and code compares.

- **How many are drawn?** A number, held by code to the product's `piezas` — and
  asked only of a product that declares them. Asked whether a picture *matched*
  "a set of three", the model passed one pan.
- **Is something printed on the product?** A symbol, an emblem, a care label:
  apart from the question about text, because a symbol is not text.

The colour already worked this way: the model names one of the catalogue's nine
and code compares it with the product's.

**Every line `verify` prints is a proposal.** It says `LOOK`, not `REDO`: a person
looks at the batch and accepts it, and a flagged picture is redrawn or its
catalogue entry corrected, whichever is wrong.

### Who looks: a port with two adapters

`IVisionModel` is to the vision pass what `IIllustrationGenerator` is to the
drawing: pictures and a question in, an answer held to a schema out. What is
asked and how it is read is shared (`VisionChecks`).

| `--provider` | what it is | needs |
|---|---|---|
| `ollama` (default) | a vision model behind Ollama; `qwen3.5:9b` fits the card. The image travels on a **chat** message, because `/api/generate` accepts an `images` field, answers 200 and ignores it | Ollama installed, and the model pulled |
| `llama` | **the shop's own model** on llama.cpp's server, with its vision projector | the container the stack already uses, and the projector `prepare-vision.sh` converts |

The second is the one phase 13a uses: the model that accepts a picture is the one
that will later read it for alt text and claims, on weights converted by us from
the pinned revision (ADR 0031).

```bash
# Once: convert the projector (8.8 GB of download, 672 MB kept).
docker run --rm --entrypoint sh -v tendero-extractor-models:/models \
  -v "$PWD/src/AppHost/scripts:/scripts:ro" ghcr.io/ggml-org/llama.cpp:full-v0.5.0 /scripts/prepare-vision.sh

# The model with its eyes, on the GPU (5.7 GB of the card's 8).
docker run -d --name tendero-vision-model --label com.docker.compose.project=tendero \
  --gpus all --memory 7g --memory-swap 7g --cpus 12 -p 39481:8080 \
  -v tendero-extractor-models:/models:ro ghcr.io/ggml-org/llama.cpp:server-cuda-v0.5.0 \
  -m /models/qwen3.5-4b-q8_0.gguf --mmproj /models/qwen3.5-4b-mmproj-f16.gguf \
  --host 0.0.0.0 --port 8080 -c 8192 -np 1 -t 12 --jinja --load-mode none --cache-ram 0 \
  --chat-template-kwargs '{"enable_thinking":false}' -ngl 99

dotnet run --project tools/SeedImages -- verify --provider llama --only B10SHO0102,B10SHO0103
dotnet run --project tools/SeedImages -- pairs --provider llama
docker rm -f tendero-vision-model
```

### How often the description check is wrong

`pairs` shows every picture twice — with its own product's data and with another
product's — before the check is trusted with a batch. Measured on 2026-10-06, the
shop's model, 23 pictures, about 1.4 s a question:

| Paired with | Its own product refused (false alarm) | Another product accepted (miss) |
|---|---|---|
| the next product in the folder (`--offset 1`, a shoe beside a shoe) | 0 of 23 | **4 of 23** |
| one eleven further on (`--offset 11`) | 0 of 23 | **3 of 23** |

It never refuses a right picture, and it misses about one wrong one in seven —
always the same way: **the kind of object is right and something about it is
not**. One pan passed as a set of three; a backpack as a gym duffel; a ceramic
table lamp as a desk lamp with a USB port; a trail shoe as a racing shoe. So it
catches a picture of the wrong thing, not a picture of the right thing drawn
wrong, and the person's look at the batch is not optional.

### What each question catches

`pairs` shows a wrong thing. `defects` shows **the right thing drawn wrong**: it
draws one defect of each kind into every picture there is (`DefectForge` — by
code, so it is free and the same on every run), asks every question of the
picture as it is and with the defect in, and counts which question newly doubted
it. The pictures a person rejects are kept with `reject` and measured beside them.

```bash
dotnet run --project tools/SeedImages -- defects --provider llama            # the board, held to the recorded one
dotnet run --project tools/SeedImages -- defects --provider llama --record   # after a change that is meant
dotnet run --project tools/SeedImages -- defects --draw-only --dump <dir>    # the drawn defects, for a person to look at; no model
dotnet run --project tools/SeedImages -- reject --only B10SHO0106 --defect emblem --note "a hand-wash symbol on its side"
```

Measured on 2026-10-07, the shop's model, 28 pictures, about 20 minutes:

| Defect drawn in | Pictures | Caught by | By no question |
|---|---|---|---|
| Lettering | 28 | Text **28** · Scene 22 · Mark 4 | 0 |
| Emblem | 28 | Mark **27** · Text 11 · Scene 8 | 0 |
| Scene | 28 | Register 25 · Scene **23** (the other 5 it doubted already) | 0 |
| Colour | 11 | Colour **9** | 2 |
| Count | 3 | Count **3** | 0 |
| Background | 28 | Register **27** | 1 |

Right pictures doubted: Scene 5 of 28 (all bags), Colour 4 of 28, the rest none.

**The model reads well and judges badly.** Asked "is anything drawn besides the
product?" it said no to 28 pictures of 28 with a brown surface and a red ball
drawn in; asked what it saw, it named both every time. So the scene question
lists and code decides. The same held for a count, and "does it match the
description?" caught none of the 126.

The recorded board is `known-bad/baseline.json`. **Changing the model, its
projector, a question or its wording means running `defects` again**; `--ci`
fails when a question catches a smaller share of a kind, when more pictures are
caught by none, or when a question doubts more right pictures. `--record` and
`--ci` take the whole set only.

A stamped emblem is cleaner than one a generator draws: this is the easy case,
and the kept rejections are the hard one. One kind is only ever a rejection:
`form`, the product itself drawn wrong (a knife standing blade up in its block).

**A redraw can be told what the last one got wrong**, said as what the object is:

```bash
dotnet run --project tools/SeedImages -- generate --only B11COO0310 --force   --hint "Every knife is stored the right way: its blade is inside the block and only its handle shows above it."
```

And a product that declares more than one piece is asked for exactly that many,
each one whole, instead of *One single object* (`PromptTemplate.SetOf`).

## The prompts

They are built from the catalogue, not from the prose list in
`seed/IMAGES-TODO.md`: the two say the same thing today and the JSON is the one
the shop imports. The colour comes from `attributes["color"]` — a Spanish label —
and is translated through the options in `seed/attributes.sample.json`, which is
where the es/en pairing already lives.

**Thirty-five products declare no colour**, and that is the case that gets
forgotten: they produce a line with no colour clause instead of a limping
`The object is :`. One test checks that, and another checks that the style block
in the code is still published in `IMAGES-TODO.md`.

The subject line comes first, which is a change from the template that document
used to publish. CLIP takes 77 tokens and discards the rest without saying so,
and its tokenizer splits numbers one digit at a time, so each hex code costs five
or six. With the subject last — where anybody would naturally put it — the colour
and the product are exactly what falls off the end.

**Every measurement is stripped before any model sees the sentence.** A coffee
maker described as having a *"24-hour timer"* came back with `24h` on its
display, and a trail shoe with *"4 mm lugs"* came back three times with `4 mm`
lettered on its midsole — each time after the instruction had been told, in
plainer words, not to write numbers on the product. A figure sitting beside a
part reads as that part's label, and asking harder does not change that. It is
also a figure that was never earning its place: 4 mm of lug is a moulded sole at
any scale. Counts survive, because "set of 3 pans" is three pans; a run of
figures sharing one unit goes out together, because `20, 24 and 28 cm pans`
carries the unit only on the last one.

**And so is how a thing is washed.** A house slipper described as *"Hand
washable."* came back twice with a round emblem on its side: the hand-wash symbol
of a care label, against a prompt that says no labels and no badges. What cannot
be drawn as the object gets drawn as something printed on it, so a care
instruction — hand or machine washing, dry cleaning — never reaches the model.
Only the instruction: a wash bag, washed linen and a cast iron pan are the
product, and their sentences stay.
