# SeedImages

Draws the product illustrations in `seed/images/`. It runs **on a developer's
machine**, once for the ones that are missing and occasionally to redo one. It
does not enter CI: there is no service container with a GPU, and this is an
authoring tool, like `dotnet-ef`.

## State

Under construction. Two commands today, and neither draws anything:

```bash
# Draw the ones that are missing. About 76 seconds each on an RTX 5070 Laptop.
dotnet run --project tools/SeedImages -- generate --models <dir> --take 5
dotnet run --project tools/SeedImages -- generate --models <dir> --only B073WXYZ01 --force

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

## The model

It is not downloaded for you and it is not in the repository: almost 10 GB, and
`models/` is in `.gitignore`. What is needed is an ONNX export of **SDXL base 1.0
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

**`generate` guarantees what arithmetic can guarantee.** It measures the object
and insets it to the central 75%, because the result card crops to 4:3 and asking
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

It needs Ollama running and a model with vision. `qwen3.5:9b` is the default and
fits in the card; the image travels on a **chat** message, because
`/api/generate` accepts an `images` field, answers 200 and ignores it.

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
