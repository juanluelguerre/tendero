# ADR 0031 — Model weights are dependencies, served from a pinned revision

**Status:** accepted · 2026-09-14

## Context

`CLAUDE.md` puts model weights in Tier 1 beside NuGet and npm: permissive only,
checked at the exact version. Phase 8 is the first time the rule meets a model —
`bge-m3` for embeddings and `bge-reranker-v2-m3` for reranking — and the roadmap
had already assumed both would be served by Ollama.

Two things had never been written down: **what "the exact version" of a model
is**, and **what gets checked**. A package has a version number and a nuspec. A
model has neither by default, and the obvious way to fetch one — a registry tag
such as `bge-m3:latest` — names nothing that can be checked later.

## Decision

### How a model is checked

1. **The version is a commit.** A model is identified by its repository and the
   full revision SHA on Hugging Face, never by a tag or `main`.
2. **The licence is read at that revision.** When the repository carries no
   `LICENSE` file, the licence is the one declared in the model card's metadata,
   and the record says that is where it came from.
3. **The base model's licence is checked too**, because fine-tuned weights carry
   whatever the base allows.
4. **Training data is recorded, not decisive.** The rule evaluates the licence of
   the weights; a dataset with no declared licence is written down as a risk.
5. **Where the weights come from is part of the check.** A copy re-packaged by a
   registry or a third party is a different artefact from the revision that was
   checked.

### What was checked

| | `BAAI/bge-m3` | `BAAI/bge-reranker-v2-m3` |
|---|---|---|
| Revision | `5617a9f61b028005a4858fdac845db406aefb181` (2024-07-03) | `953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e` (2024-06-24) |
| Licence | **MIT** | **Apache-2.0** |
| Where the licence is declared | model card metadata; no `LICENSE` file | model card metadata; no `LICENSE` file |
| Base model | `FacebookAI/xlm-roberta-large`, **MIT** | a fine-tune in the bge-m3 line |
| Training data | `Shitao/MLDR` (MIT); `Shitao/bge-m3-data` declares **no licence** | `bge-m3-data`, quora, fever |
| Gated | no | no |

Both pass Tier 1. `bge-m3-data` declaring nothing is the recorded risk.

### How they are served

**Hugging Face Text Embeddings Inference** (TEI) 1.9.3, Apache-2.0 — a container
beside the application, so Tier 2 — **with each model pinned by `--revision`**.
TEI reports the revision it loaded in `/info` as `model_sha`, which is what lets a
startup check compare the model actually served against the one configured
(`P8-3`).

**Not Ollama, for these two.** Its official `bge-m3` is a GGUF conversion whose
licence layer is the MIT template with `[year] [fullname]` left unfilled and which
names no source revision, so what was checked and what would run cannot be tied
together. `bge-reranker-v2-m3` is not in its official library at all — only in
third-party uploads — and the API documents no rerank endpoint. Ollama stays the
candidate for the generative models of phases 10 and 12, and its hosting package
does not come back in phase 8.

## Measured

A spike on 2026-09-14 ran both models in TEI's CPU image (`cpu-1.9.3`) at the
pinned revisions. `/info` reported exactly those revisions.

| | CPU |
|---|---|
| `bge-m3` dimensions | 1024, identical output for identical input |
| cosine, *"cafetera italiana de 6 tazas"* vs *"Italian stovetop coffee maker"* | **0.793** (vs a backpack: 0.409) |
| one query embedded, warm | 79–127 ms |
| twenty product texts embedded | ~1.5 s |
| reranker, precise query, right product | **0.9958**, the other three 0.0000 |
| reranker, twenty candidates | **9.8–12.3 s** |
| resident memory | embeddings 12.7 GiB · reranker 5.3 GiB |

On the CPU image `bge-m3` loads through ONNX Runtime (`onnx/model.onnx` exists at
that revision) and the reranker through TEI's candle backend from
`model.safetensors`, where TEI approximates GeLU with GeLU + tanh.

**The GPU was not measured.** In Docker Desktop on WSL2, TEI's Blackwell image
(`120-1.9.3`) could run `nvidia-smi` against the RTX 5070 and still fail CUDA
initialisation with `CUDA_ERROR_NO_DEVICE`; `/usr/lib/wsl/lib` was not mounted in
the container. **TEI then served on the CPU without failing** — a warning in the
log and nothing else.

## Consequences

- `P8-1` declares Qdrant and TEI, not Ollama.
- **The reranker is a GPU feature.** At ten seconds for twenty candidates on a CPU
  it can never meet a request budget, so on a CPU the staged degradation of `P8-7`
  is the normal path rather than the exception.
- **A healthy endpoint does not prove a GPU.** Because TEI falls back silently, the
  startup check reads the backend actually in use — not only `/health` — and marks
  the reranker unavailable when it is on a CPU.
- The revision SHA belongs in `EmbeddingModelDescriptor`, and the vector
  collection's name follows from it (`P8-3`): a new revision is a new collection.
- TEI answers both `/embed` and an OpenAI-compatible `/v1/embeddings`. Whether
  `Microsoft.Extensions.AI` reaches it through `Microsoft.Extensions.AI.OpenAI` or
  through a small `IEmbeddingGenerator` over `HttpClient` is `P8-2`'s dependency
  decision, not this one.
- The memory above goes into the README prerequisites, as the Elasticsearch heap
  already does.
- The CUDA failure inside WSL2 is recorded on the board's backlog. It is a fault
  of this environment, and `P8-6` starts by fixing or bypassing it.

## What was rejected

**Ollama for both.** Covered above: no source revision for one model, no
official copy of the other, and no rerank endpoint.

**A third-party Ollama upload of the reranker.** Tier-1 weights from an author
nobody can name, called through `/api/embed` for something it is not.

**The reranker in process, on ONNX Runtime.** It is the path `tools/SeedImages`
already takes on this GPU, and it stays the fallback if TEI cannot reach CUDA.
It is not first because the reranker has no ONNX export at the checked revision —
producing one is a Python step outside the sidecar's scope — and DirectML is
Windows-only while CI runs on Linux.
