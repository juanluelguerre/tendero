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

## The first real turns (2026-10-05)

The whole stack with `Tendero__Tino=true`, the seed catalogue of 100 products, the
same model on the GPU, six questions through `POST /api/assistant/turns` — the turn
the product runs, not a script. The container held 4.8 GB of the card and 1.3 GiB
of RAM.

**First run: three of six did not answer.** Each ended as `Unavailable / Model`
after 13 – 18 seconds. The rows in `telemetry.ai_invocations` said why: every one
of those turns ended with a composing call of **exactly 400 output tokens**. The
cap was the gathering's, 400, and four products with three reasons each do not fit
in it; the JSON was cut, and what is cut is not an answer. The three that did
answer were the short ones.

**The fix, and the second run: six of six.** The composing call has its own cap
(`Tino:ComposeOutputTokens`, 900) and the instructions ask for the JSON on one
line. The longest answer then took 548 output tokens.

| Question | Kind | Time | Reads |
|---|---|---|---|
| *una sartén que valga para inducción y no pese mucho* | products, 2 | 11.4 s | search ×2, specs |
| *¿puede ir al horno?* (on a pan's page) | products, 1 | 5.1 s | search, specs, product |
| *¿cuántos días tengo para devolver algo?* | shop | 2.6 s | return policy ×2 |
| *a backpack for a laptop* | products, 4 | 12.0 s | search, specs, product ×4 |
| *quiero algo para la cocina* | products, 4 | 14.6 s | search, specs ×2 |
| *which is the best pan?* | products, 4 | 11.1 s | search, specs, product ×3 |

Five model calls a turn at most, up to 10,269 input tokens a turn, the largest
single call 3,076.

**What it answers well.** *¿puede ir al horno?* on the cast iron pan's page: one
reason, *"Sartén apta para horno"*, cited to the description's sentence that says
so. The return policy: the shop's own sentence, with the domain's 14 days. The
backpacks: *"It has an anti-theft back pocket"* cited to the approved claim.

**What it gets wrong, and which step owns it.**

- **A reason that says more than its fact** — T-3's job, and these are its first
  cases: *"Está hecho de acero inoxidable"* cited to *piezas: 3*; *"Son 2 paños de
  cocina de lino de color crema"* cited to *material: lino*; *"It is induction-ready
  and holds 1 L"* cited to *induction: yes*. Each cites a real fact of the right
  product, so the first check passes them; the figures check catches the litre, and
  entailment the rest.
- ***quiero algo para la cocina* is answered, not asked back.** The model searched
  and listed four things. The prompt asks for a question; T-5's suite scores it.
- ***no pese mucho* is not answered.** The catalogue holds no weight for those pans,
  so nothing could be cited — and the answer does not say so. A *sin dato* for a part
  of a question is not a kind an answer has; noted for the spec.
- **Time is close to the cap.** 14.6 s of a turn's 20. The gathering reads more
  than it needs (a search and specs repeated, a product read per candidate).
  T-5 fixes the caps and the prompt from the suite; a second model slot would not
  help a single turn.
- **Reads are lost when no profile answers**: an `Unavailable / Model` says nothing
  of what was read, because the fallback starts again. It is the storefront's
  fallback either way.
