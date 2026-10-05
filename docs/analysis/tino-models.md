# Tino's models — what was measured before building on them

Tino's answer is typed (`docs/specs/tino.md`, *An answer*): the model's reply is
held to a JSON schema. This file holds what was measured about that, and will
hold the `assistant` suite's results (T-5).

## The spike of 2026-10-05: is a schema held, and can tools sit beside it?

**How.** llama.cpp `server-cuda-v0.5.0`, Qwen3.5-4B Q8 (the pinned GGUF Steward
uses), the RTX 5070 Laptop, one slot of 8,192 tokens, thinking off, temperature 0.
A hand-written sheet of eight facts about three pans and the shop's return policy;
ten questions, five in Spanish and five in English; one call each, no tools,
`response_format: json_schema`. The schema's product `code` and each reason's
`cites` were **enums of what the sheet held**. The container held 4.8 GB of the
card and 2.0 GiB of RAM. A throwaway script, deleted; nothing below is from the
turn the product will run.

**Findings.**

| | |
|---|---|
| Valid JSON of the schema | 10 of 10, in both rounds |
| A code or a fact id outside the enums | none — the grammar cannot write one |
| **Tools declared beside the schema** | **the server refuses the call**: `400 Failed to initialize samplers: failed to parse grammar`, 4 of 4 |
| A reason citing **another product's** fact | 1 question of 10 (*which pan is the lightest?*): "lighter than the other pans" cited to the other pans' weights |
| Latency, a `products` answer | 1.6 – 6.7 s, 81 – 390 output tokens |
| Latency, `ask`, `noData`, `shop` | 0.6 – 1.4 s, 27 – 60 output tokens |

**What they decide.**

1. **A turn has two phases.** Tools and a schema in one call is not available on
   this server, so Tino gathers with tools and composes without them. The plan
   assumed it; now it is measured.
2. **One shape per kind, nothing nullable.** The first schema had every kind's
   fields in one object, the unused ones `null`. It was valid every time and
   wrong twice: an `ask` whose question was `null`, and a policy question answered
   as three products each citing the policy. With `anyOf` — one object per kind,
   every field required — both went, and the short kinds fell from 3 – 6 s to
   about a second, because the model stopped writing the fields it did not need.
3. **A fifth kind, `shop`.** A question about the shop itself (*how many days to
   return something?*) is not about a product. It gets its own shape: one to three
   sentences, each citing a fact of the shop.
4. **Comparisons are where the model cites wrongly**, and the enum cannot stop
   it: the id exists, it is another product's. Code drops such a reason (the
   plan's ruling 3). T-5's suite needs comparison cases.
5. **About half of a `products` answer's tokens are indentation.** The model
   writes the JSON pretty-printed, and the grammar allows any whitespace. It is
   time, not correctness; asking for one line, or a grammar that forbids the
   whitespace, is measured in T-5.
6. **`about` needs an example.** Asked for "what was asked about", the model
   copied the question. Told that it completes *This product's details don't say
   …*, with one example in each language, it wrote *si puede ir al horno* and
   *whether it can go in the dishwasher*.
7. **The language of a reason follows the fact more than the question**: a
   Spanish question got *Works on induction hobs.*, the English fact's words. In
   the product the reads answer in the conversation's culture, so the facts will
   be in the shopper's language; the suite scores it either way.
