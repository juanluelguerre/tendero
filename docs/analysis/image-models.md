# Image models in Tendero: what a spike found (2026-10-05)

A throwaway spike, nothing kept: does the stack we already run read pictures, what
would it cost, and where would it earn its place. Hardware: RTX 5070 laptop, 8 GB.
Not committed to a plan; this is the evidence for deciding one.

## Two kinds of model, two jobs

| | Image embeddings (SigLIP 2) | Vision-language model (VLM) |
|---|---|---|
| Produces | a vector | text, or JSON from a schema |
| Use | visual search, "more like this", duplicates | alt text, attributes seen in a photo, photo-vs-data checks |
| Cost | small, no generation | ~1.6–2 s a picture on the GPU |
| Phase | 13 (planned, 0 of 8 items started) | nowhere in the plan yet |

## What the spike measured

**The model we already serve reads pictures.** `Qwen/Qwen3.5-4B` at the pinned
revision is `Qwen3_5ForConditionalGeneration` — it has a vision tower
(`vision_config`, `image_token_id`, a preprocessor). Our Q8 GGUF holds the text half
only; llama.cpp needs the vision half as a second file, the *mmproj*.

- llama.cpp `server-cuda-v0.5.0` loaded `-m qwen3.5-4b-q8_0.gguf --mmproj mmproj-F16.gguf`
  and served images over `/v1/chat/completions` with no other change.
- **VRAM**: 5.5 GB with the mmproj, against 4.8 GB text only (+0.7 GB). It does not fit
  beside a second 4.8 GB model on 8 GB; one model serves both jobs, or they take turns.
- **Latency**: ~1.1k prompt tokens a picture (a fixed ~1,085 for a 512 px image),
  prompt 0.5–0.8 s, ~60 tokens/s out: **1.6–2.3 s** for an alt text in two languages.
- **Schema-constrained output works with an image**: `{alt_es, alt_en, dominant_colour,
  has_text}` came back valid on four products, and the Spanish read as Spanish.
- **Through our own factory**: `OpenAICompatibleChat.Create` with a `ChatMessage` of
  `TextContent` + `DataContent(bytes, "image/png")` reaches the model, and `Usage`
  reports the picture's tokens (1,052 in), so `MeteringChatClient` would count them
  with no change. (The factory wants the server's root, not `/v1`; it appends it.)
- **llama.cpp does not decode WebP**: the store's pictures are WebP, so a reader
  converts to PNG/JPEG first.

### Quality, four seed products, by eye

| Product | Alt text it wrote (en) | Verdict |
|---|---|---|
| Set of 3 non-stick pans | "Three stacked blue pans with black handles on a white background." | right |
| Trayecto 40L backpack | "Olive green backpack with orange and black accents, front compartments, side straps, a water bottle in the side pocket." | right, detailed |
| 26 cm cast-iron pan | "Black cast iron skillet with handle and pouring spout…" | right |
| Cantaro ceramic lamp | "Table lamp with terracotta base and beige fabric shade" | right |

Photo against the catalogue's name (a yes/no with what it saw): lamp vs its own name →
match, 0.95; lamp vs "Mochila de viaje" → **no match**; pans vs their name → match;
iron pan vs "Cuchillo de cocina" → **no match**, 0.99. Four products is not a golden
set — it says the idea is not absurd, not how often it is wrong. The "confidence" is
the model's word for itself, like every extractor's.

## SigLIP 2 (the plan's visual search)

`google/siglip2-base-patch16-224` is Apache-2.0, not gated, revision
`75de2d55ec2d0b4efc50b3e9ad70dba96a7b2fa2` (so400m-384: `e8e48729…`). The maker
publishes PyTorch weights only; the ONNX files are `onnx-community`'s re-package,
which ADR 0031 rule 5 refuses unless we export them ourselves or record the exception.
No package was added; none was tested.

## What this means

1. **Alt text is a by-product, not a feature of its own.** The same read that writes
   it can say what the picture shows (colour, material, has-text), and a person checks
   it. The shop's own seed has no alt text at all, so every product is a gap today.
2. **A picture-derived claim does not fit the Knowledge model as it is.** `Evidence`
   needs a quote found inside a text document. Options: a `SourceKind.ProductImage`
   whose "document" is the model's description with the quote taken from it (then the
   NLI scorer checks a model against itself), or a different support measure for
   pictures. A design decision, with a person in the loop either way (invariant 12).
3. **The mmproj is a pinned-weights question (ADR 0031).** No maker publishes a GGUF
   of Qwen3.5 and rule 5 refuses a third party's copy, so the right file is one
   `convert_hf_to_gguf.py --mmproj` produces from the pinned revision — which this
   spike did **not** do (it used a third party's `mmproj-F16.gguf`, spike only; the
   converter's support for this architecture in v0.5.0 is unverified). Our `full` image
   already carries the converter; `prepare-extractor.sh` would grow a second output.
4. **Uploaded pictures have no policy.** No customer upload exists, `IImageStore` has
   no `Delete`, `docs/privacy.md` covers the browsing trail only. A search-by-photo or
   a returns photo needs that first (do not keep the query picture, strip EXIF).
5. **Smaller things the code will want**: `InProcessOnnxChat` and `ScriptedChatClient`
   read `.Text` and would drop a picture; `catalogue_gaps` returns a product, not the
   (product, image, missing culture) a VLM needs; `Catalog:Images:RootPath` is not set
   in the AppHost, so a container cannot read the store; only 18 of the 100 seed
   pictures exist.

## Deferred to phase 13 (the author, 2026-10-05)

Images wait for phase 13 and after; nothing here is scheduled before it. Two things
to pick up then:

- **The 82 missing seed pictures come first**, before anything is measured on them.
  One attempt was made on 2026-10-05 and drew nothing: `gemini-2.5-flash-image`
  answered **401 UNAUTHENTICATED** to the `GEMINI_API_KEY` in the environment, so the
  key has to be renewed (and its project needs prepaid credit — the free tier grants
  no image generation). Nothing was spent; `seed/images/` still holds 18.
- **The whole generation process is written down as it is run**, for the blog and the
  book, with the reason Gemini drew the catalogue: the first eight were made by hand
  with nano banana, SDXL beside them is flat and soft and six prompt rewrites did not
  close a gap that is the model's, and a catalogue with two visual registers is not a
  catalogue (`tools/SeedImages/README.md`, `seed/IMAGES-TODO.md`). The record should
  carry the prompt, the spec, the batches with their time and cost, the vision pass
  and every picture redrawn and why.

### What a generated picture must pass (the author, 2026-10-05)

A picture is accepted only when it is **right for its product's description** and
**in the same pattern as the ones that exist**. Today's `verify` pass covers part of
that and no more:

| Requirement | Checked today | Missing |
|---|---|---|
| No text in the picture | `has_text` (vision pass) | — |
| The product's colour is its attribute's | `dominant_colour` against the catalogue | — |
| A plain background, no scene | `has_scene` | — |
| The object sits in the safe area | `probe`, by counting pixels | — |
| **It shows the product its name and description say** | nothing | a photo-against-description check: what the picture shows, whether it matches, and what differs (count, shape, material, parts named in the description). The spike's yes/no against the name caught both swapped names on four products; it needs the description too, and a set of known-wrong pairs to measure how often it misses. |
| **It is in the register of the first eight** | nothing — by eye | a comparison against the eight reference pictures (line illustration, flat background from the tokens, same framing), by a vision model shown a reference beside the candidate, or by image embeddings' distance to the reference set; and a person's look at every one a check flags. |

**Confirmed by the author (2026-10-05) as the rule:** in both checks the verdict is a
proposal and a person accepts the batch. A flagged picture is redrawn or the catalogue
is corrected — whichever is wrong, as with `B13BAG0710` — and each case is recorded
with its reason.

## Decisions for the author

- Build a VLM job (alt text + photo-vs-data check as proposals), SigLIP 2 visual
  search (phase 13), both, or neither yet?
- Fix the weight under ADR 0031: our own `--mmproj` conversion (recommended) or a
  recorded exception for a third party's.
- Which of the two evidence models for picture claims (point 2).
- Whether to generate the 82 missing seed pictures before measuring anything on them.
