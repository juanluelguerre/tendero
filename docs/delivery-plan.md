# Delivery plan

The board. Open this to know what to do next; everything else is reference.

- **What exists today** → [`analysis/current-state.md`](analysis/current-state.md)
- **What is missing and why it matters** → [`analysis/gap-analysis.md`](analysis/gap-analysis.md)
- **Where each thing goes architecturally** → [`analysis/roadmap.md`](analysis/roadmap.md)
- **Per-article publishing metadata** (slug, excerpt, tags, blockers) → [`blog/index.md`](blog/index.md) — **that file stays the owner of it. This one does not duplicate it.**
- **Raw material, written as things happen** → [`blog/notebook.md`](blog/notebook.md)

---

## Where I left off

> Update these three lines at the end of every session. They are the point of the file.

- **Current phase:** **Design before code: the experiment ran, and it was accepted (2026-09-18, on `feature/design-before-code`).** A fresh agent, blind to the shipped component, built 589 nodes in **12 min 20 s** with no human intervention. Scored against criteria declared beforehand and verified against the document rather than the agent's report: **0 literal colours**, structure **3 of 3** (the row order read rather than the label trusted), and the author kept two of three new decisions. `verdict.md` carries the scoring. **The sharpest finding is not the verdict**: the one rejected candidate was faithful to `P8-11`, which still says *"whatever the flag says"* — and `SearchVector` was deleted the day before the brief was frozen. **A specification does not compile, so nothing goes red when reality moves past it.** The pass also surfaced a **shipped accessibility defect** it was not looking for: `[data-theme='dark']` redefines `--positive` and neither `--danger` nor `--warning`, so `.tag--danger` ships at about **2.6:1** in a backoffice that is dark by default. And the honest counterweight: the shipped panel covers **six states to the blind pass's four**. ADR 0033 amended twice by the author — the artefact is **kept** as evidence rather than deleted on a schedule, and **both** the `.pen` and its HTML export are public, because the editable file teaches a reader what a mockup is made of. The rule is executable now: `design-mockups.spec.ts` holds a `.pen`'s colours to `tokens.css` unless a person declared them in `proposals.json` **with a reason**, closing the gap where `design-tokens.spec.ts` keeps `.css` files and 575 hand-written hexes sat outside every gate. Four tests, four mutations, four kills. **What it could not measure**: this was a redesign, not a greenfield — and *the sketch rung can be prose; the mockup rung cannot*. Before that: **the question that started it**, and finding that this repository had already run the experiment and lost the result. Two design documents sat in `docs/design/` — public, referenced by **nothing**, pointing at three files that do not exist — and they say in their own text what they are: round one *"eran de otro producto (reparto de barrio)"*, round two *"Reconstruido desde el código"*. One designed a different shop; the other was drawn after the screens. The day after they were written, PR #1 shipped the screens article 18 calls correct and not a shop, so **the artefact that exists to catch exactly that was in the repository, produced in the one order that cannot catch it.** Between them, **575 hand-written hexadecimal colours** against the design system's only hard rule — invisible to the test that enforces it, which walks the whole repository and keeps `.css` files. The diagram sources copy the palette too and have a reason to; these did not, which is the mockup rung's flaw stated without mentioning AI: **a static artefact in colour is a copy of the design system, and a copy with no test drifts in silence.** ADR 0033 written: a design artefact is disposable and never enters the public repository, `tokens.css` stays the single source, and pen.dev is an experiment with a removal condition rather than an adoption — forced into that shape by a measurement, since its MCP server answers `A file needs to be open in the editor` and therefore can never be a gate. **The A-B is declared and not yet run**: a blind redesign of the backoffice search-evaluation panel, four criteria fixed beforehand, published whether accepted or refuted. The old book *De 0 a N con Task[in]* turns out to be the baseline rather than a footnote — its `Capitulo03` is literally *"Boceto, wireframe, mockup y prototipo"* — and the book's preface and chapter 6 quote it inline, which is what makes them work while it is still private. Before that: **hybrid search is the default, every search page says what kind of answer it is, and the refuted scope classifier is evaluation tooling (2026-09-17, merged into `develop` and pushed as `6e069f3`; the board's "not pushed, no pull request yet" was stale and is corrected here).** **Layer B left the shipped code the same evening, by the author's decision.** It had been kept off in `src/Search`, and "off" could not become "on" without a calibration file no protocol run will produce until there is a better signal — so it was unreachable code, an enum value on the wire nothing could send (`OutOfScope`, with `outOfScopeLabel`, plus UCP's `not_sold` and a storefront branch), and eleven mutation survivors no test could kill. The classifier, the anchor file, the protocol's rules and the 40 scope query pairs per culture are `tools/SearchEval/QueryScope` now, and `dotnet run --project tools/SearchEval -- --suite scope` still runs the protocol whole — which is the half a trained successor needs. The shipped answer has two values, `Exact` and `Related`, the OpenAPI document and the generated TypeScript were regenerated, and 1035 backend tests (1014 green, 21 skipped) and 69 frontend are green with a clean `-warnaserror` build; **Search's Stryker baseline is re-derived at the next phase close**, because the project it was measured on lost a folder. Before that: Asked how real shops handle a query they have nothing for, instead of a sixth threshold, two research reports answered: say plainly that nothing matches, and decide "we don't sell that" in a query-understanding step, never by a threshold on a retrieval score (ADR 0032). **Layer A**, with no model: `Exact` when a product carries every word of the query in what it is (name, brand, attributes, category path; no fuzziness; one `best_fields` clause per word, because `brand` has its own analyser — "Kuisina cafetera" counted 0 with the old body and 1 with the new one on Elasticsearch 9.5.0), `Related` otherwise, and a help card for returns; the storefront shows a notice, MCP returns `answer`, UCP adds `no_exact_match` / `help_available`. **Layer B**, scope by anchors, was measured once by a protocol declared beforehand and **refuted** on `scope-dev`: in-scope queries marked out of scope 9 / 5 / 1 / 1 at margins 0.02 / 0.05 / 0.08 / 0.12, and "gift for someone who works from home" told the shop does not sell stationery at every margin; its first run gave no verdict because TEI accepts 32 texts per request and the anchors were 133, fixed test-first. By the author's decision its code stays in the tree, off, as material for the article. The five refuted knobs (H1–H5) and the fusion weights are deleted; `SearchRerank` became a GPU capability; **`SearchVector` met its removal condition** in the third gate run (hybrid reranked 0.843 / 0.831 against lexical 0.748 / 0.692, identical to 2026-09-15) and is deleted, so `FeatureId` is empty. Every no-answer query is admitted (1.00), false out-of-scope 0 (trivially, while layer B is off), and the notice lands on 0.481 (en) / 0.462 (es) of queries that have answers — 0.538 / 0.519 until the full-match check learned to ignore a bare measurement token, measured per query kind first: every use-case and symptom query gets it, and thirteen of the fifty-five landed on pages answered at 0.90 or better. ADR 0032 written, 0023 and 0031 amended. **Phase close:** Stryker of covered 76.23 % for `src/Search` against its 75.97 % baseline, and a first 69.34 % for `src/Ucp`, every survivor classified in `docs/testing.md`. Before that: **Phase 8 reached `develop` (2026-09-16), with mutation testing adopted as a phase-close convention.** The working tree went in as commits by theme on `feature/phase-8-hybrid-search`, merged into `develop` and pushed once, on the author's say-so. Stryker.NET is in the tool manifest with `src/Search/stryker-config.json` committed; a fourth run from that config reproduced the third exactly (of covered 75.97 %, 93 survivors), so that is Search's baseline, and `CLAUDE.md`, `docs/testing.md` and ADR 0006 carry the two-level rule. Article 19's outline and the notebook hold the explanation of the method and the report. Before that: **Stryker's survivors, sorted and the real ones closed (2026-09-16, night).** Of the first run's 171 survivors, 80 were logging, telemetry and configuration strings, 37 were evaluation code whose tests live in `SearchEval.Tests`, 22 were plumbing the integration tests cover, and about 18 were real gaps — closed with 12 new test cases and a strengthened assertion (the Elasticsearch request body's paging, sort, browse and branch filter; the reranker response's out-of-range indexes; a lone flag attribute; an empty category branch; a model name's odd characters; the Qdrant registration), each re-proven by hand, 14 mutations for 14 kills. A third Stryker run, temporary again and uninstalled, with both test projects and logging ignored: of covered 55.81 % → **75.97 %**, survivors 171 → 93; its reports are in `docs/blog/reports/2026-09-16-hybrid-search/stryker/run-3/`. 950 backend tests (929 green, 21 skipped), build clean. Before that: **Two more attempts at the open questions, both measured to a verdict (2026-09-16, afternoon).** **Queries with nothing to find**: H5 decided per query — no answer when even the head's best reranker score is under T (`NoAnswerBelowRerankScore`, 0 by default, three mutations caught) — and was refuted on `tune` (−0.074 at the lowest T): it empties "gift for grandma", "regalo abuela" and "staying dry on the bike", because the reranker scores their best products as low as a lamp for "móvil para mi madre de 70 años". Its absolute score measures literal closeness, not whether the shop has something, so the next signal is a design question, deliberately not tuned against splits already looked at many times. **The reranker on a CPU through ONNX Runtime**, with the author's say-so to download `onnx-community/gte-multilingual-reranker-base` INT8 (341 MB, pinned by revision and file hash): 35× faster than candle, ~0.5–0.7 s for ten pairs, and at the real 800 ms budget hybrid en 0.809 / es 0.788 against 0.810 / 0.783 without it — the declared rule needed both cultures up, so it stays GPU-only; the evaluation tool can now pin a locally served candidate by `sha256:`. 938 backend tests (917 green, 21 skipped), build clean. Before that: **The three things hybrid search left open, each measured to a verdict (2026-09-16).** **The queries with nothing to find** got a fourth declared hypothesis, H4 — the reranker turns away what scores below T, and the unread tail with it (`MinimumRerankScore`, 0 by default, four mutations caught): it empties the page for "móvil para mi madre de 70 años" from T = 0.01, and costs every other query 0.2 of NDCG and recall@50 0.939 → 0.666, so no value qualified and it was refuted on `tune`. The open question stays open and better defined. **The reranker on a CPU**: the smaller, permissive multilingual candidate the research found, `gte-multilingual-reranker-base`, downloaded with the author's say-so, took 35 s at the median for twenty pairs on TEI's candle backend against an 800 ms budget — discarded without measuring quality, its weights deleted; what remains is ONNX Runtime with an export, a download not yet approved. **Stryker.NET was trialled** outside the repository and uninstalled: 983 mutants in six minutes, identical in every unchanged file across two runs at concurrency 1, and six real gaps the hand-made lists had missed — each closed with a test and re-proven by hand. The evaluation tool can now measure a candidate reranker (`--reranker-model id@sha`, pinned by full SHA). 936 backend tests (915 green, 21 skipped), build clean. Before that: **Hybrid search beats lexical in both cultures, and what made it was not tuning: a realistic exam, a defect found by a number that did not add up, and the reranker.** The golden set grew from 22 to 52 scored queries per culture plus 8 with nothing to find, each labelled with Baymard's query type, a `tune`/`holdout` split decided before measuring, and its counterpart query in the other culture; the new queries were shaped after real shopper phrasing in Amazon's ESCI dataset and judged blind against the whole catalogue, then four blind passes judged everything any strategy put in a first ten (1053 / 1075 judgments, every criterion and decision in `tools/SearchEval/proposals/2026-09-15-new-queries.md`, the author having delegated grading doubts to professional e-commerce rules). The report breaks NDCG down by type and gives the no-answer queries their own `shown@10`, with nine mutations caught. Three tuning hypotheses were declared in the notebook before running — a similarity floor, a weight on the vector ranking (`VectorWeight`, new), and routing to meaning when no product carries every word (`MeaningFirstWithoutFullMatch` behind a new `ILexicalFullMatch` port) — and **the third one's number exposed a defect**: in `Elastic.Clients.Elasticsearch` a second `.Filter(...)` or `.Term(...)` on one descriptor replaces the first, so the card reader hybrid search depends on had lost its id filter (meaning's products dropped unless they were among the cheapest) and a category browse had lost `status: active`. Fixed test-first with `ElasticsearchRequestBodyTests`, which read the body the client really writes and failed against the old code. With it fixed, two identical runs: lexical en 0.748 / es 0.692, vector alone 0.812 / 0.773, hybrid 0.810 / 0.783, **hybrid + reranker 0.843 / 0.831**, and on `holdout` 0.837 / 0.856 against lexical 0.744 / 0.729; Spanish use-case queries 0.263 → 0.619. The three hypotheses, repeated on the fixed code, were refuted on `holdout` again, so every default stays. Thresholds re-derived by the stated rule (lexical es 0.67 / 0.72, en 0.73 / 0.78; hybrid es 0.77 / 0.91, en 0.79 / 0.92). Stryker.NET evaluated without installing it: Apache-2.0, but its MTP runner is preview and miscounts on xunit v3 above concurrency 1 — a one-project trial, not adoption. 927 backend tests (906 green, 21 skipped), 65 frontend. Before that: **Before tuning hybrid search, the ruler was measured, and it had been built by lexical search.** (Its hybrid and vector figures predate the card-reader fix.) The golden set was pooled from lexical query variants only, so `judged@10` — new, beside a vector-only diagnostic strategy — read 1.000 for lexical, 0.84 for hybrid and 0.39 for the vector side. `SearchEval --pool` wrote the 282 unjudged first-ten results; they were judged blind, the author decided the criteria that grade many rows at once and resolved nine doubts, and the golden set went from 450 to 732 judgments (`tools/SearchEval/proposals/2026-09-15-pool.md` records every change). **242 of the 282 are 0**: the bias was real and small. With a fair ruler, two identical runs: hybrid en 0.944 against lexical 0.921, es 0.916 against 0.921; the vector side alone 0.51 / 0.53. Thresholds re-derived for the one allowed reason — the ruler changed, not the engine. What is left is the vector side filling its twenty places with irrelevant neighbours and fusion letting them in. Before that: **Testing phase 8 in a browser found two older defects, and both are fixed.** A cart line froze its product name as ONE string in the language of the request that added it, so a basket filled in Spanish stayed Spanish in English — against invariant 6, which forbids exactly that — and checkout copied it onto the order with the culture the cart was STARTED in, because `Cart.UseCulture` existed and nothing called it. Lines keep `LocalizedText` now, the cart resolves it on reading and checkout adopts the language it is placed in; a migration wraps the carts already saved, verified against the development database and in the browser, and three mutations each turn a test red. And the seed's cast iron pan said `material: acero inoxidable`: a `CAST_IRON` option was added, because `MATERIAL` is a closed set, and the gate did not move a thousandth. 902 backend tests (881 green, 21 skipped), 65 frontend. Before that: **Phase 8 is built, and hybrid search does not beat lexical in Spanish.** `P8-3` to `P8-9` are committed on `feature/phase-8-hybrid-search` — a mixed vector space refused three times, the catalogue projected into Qdrant on the outbox without ever failing a message, fusion by rank, a reranker, and a vector side that fails returning lexical's answer byte for byte — and `P8-10` and `P8-11` wait in the working tree. **Measured on the GPU against throwaway engines**: en 0.935 → 0.944, es 0.927 → 0.912, identical across two runs; the reranker makes it worse (en 0.912, under its threshold); `regalo para casa`, the query kept for meaning to earn, 0.305 → 0.355. **One experiment, declared before it ran**, `VectorDepth` 20 → 10, was refuted (es 0.891) and the search stopped there, because trying values against 22 queries per culture is fitting the golden set. Hybrid's thresholds sit ~0.013 under the measurement, lexical's are untouched, and `SearchVector` stays off — ADR 0023 written, two flags and not five, each with one reader and a removal condition. The evaluation moved into `src/Search/Evaluation` so the gate and the new backoffice panel share one metric; the panel measures on a button, lists the queries hybrid lost on first, and explains two identical columns with the vector layer's own reason — six mutations, none surviving. **The full suite found a composition hole `P8-4` left**: `OutboxDrainTests` built its container without the vector layer, so every `ProductUpserted` failed in the worker with nothing else red; it registers what `WorkerServices` registers now. 898 backend tests (877 green, 21 skipped), 65 frontend. Before that: **`P8-2` is in: `bge-m3` behind `Microsoft.Extensions.AI`.** The adapter is `Microsoft.Extensions.AI.OpenAI` 10.10.0 over TEI's OpenAI-compatible route, and because compatible is a surface rather than a contract, the request the SDK really sends is captured and held to the fields TEI accepts — the one thing that would otherwise fail only at run time. `EmbeddingModelDescriptor` refuses a tag or a short SHA and names the collection after model, revision and dimensions; the generator exists only when a model server does. Verified against the real container behind an opt-in switch, and by eight mutations with none surviving. 804 backend tests (783 green, 21 skipped — the model test among them unless asked for). Before that: **`P8-1` is in, and it found that renaming a folder loses a database.** Qdrant and Text Embeddings Inference are declared in the AppHost and start on demand (`Tendero__Vectors=true`), verified live in both directions — and the first run from the renamed `tendero-workshop` folder came up with an EMPTY catalogue. `WithDataVolume()` with no name keys the volume on a hash of the AppHost's path, so the rename created `…-d02296bb03-postgres-data` beside the real one, with nothing failing. Postgres and Keycloak volumes are named now (`tendero-postgres-data`, `tendero-keycloak-data`), the old data was copied across byte for byte (69,829,479 bytes, 1,371 entries), and the database came back with 100 products, 8 orders and 2 customers; the old volumes are kept until somebody deletes them on purpose. Before that: **phase 8 starts with what it is allowed to load.** The licence check the board put first is done and recorded as ADR 0031, the first time the Tier-1 rule met model weights: `bge-m3` at revision `5617a9f` is MIT and `bge-reranker-v2-m3` at `953dc6f` is Apache-2.0, both read at the exact revision, the base model checked too, and one fine-tuning dataset written down for declaring no licence. **The check changed `P8-1`**: Ollama's `bge-m3` names no source revision and its licence layer is an unfilled MIT template, and the reranker exists there only as third-party uploads with no rerank endpoint — so both are served by Text Embeddings Inference pinned by `--revision`, and Ollama waits for a generative model. A spike ran both at those revisions: one vector space for both cultures (0.793 between the Spanish and the English coffee maker, 0.409 to a backpack), 79–127 ms per query embedding, and a reranker that is right and unusable on a CPU — 9.8–12.3 s for twenty candidates. **The GPU could not be measured**: inside Docker Desktop on WSL2 `nvidia-smi` saw the RTX 5070 and CUDA did not, and TEI served on the CPU without failing, which is why the startup check has to read the backend rather than the health endpoint. The same day **the public repository went live**: `tendero` is this history with `docs/blog/` filtered out, advanced one article tag at a time (*The public repository* in `CLAUDE.md`), and article 00's tag went out first. Before that: **phase 9 reaches `develop`.** `feature/mcp-server` took the 46 commits `develop` had gained since it branched, in one merge whose seven conflicts were all documentation — and one collision that was not a text conflict at all: **both branches had written an ADR 0028**. `develop`'s (prices include VAT) was already merged and is quoted by code and tests, so the branch's two moved up — JsonSchema.Net is 0029 and protocol routes are 0030 — and the commit messages that name the old numbers stay as they were. 786 backend tests (766 green, 20 skipped), 61 frontend, format gate clean; the Playwright suites are left to CI. Before that: **the UI review's pending list, closed, and the lexical baseline made honest before phase 8.** Two returns racing for the last unit are refused by the database: each return is numbered within its order and a unique index on (order, number) lets one in — proven deterministically against Postgres and by mutation — and the same number tells two returns of one order apart on screen. The minor findings went too: the decline is said in the shop's language and brought into view, an authorised payment no longer says "Paid", row actions are quiet and `Shipped` is not agent clay, every page has its own translated title, a guest's checkout is not prefilled with the seeded shopper's name, the placeholder card links to its product, and the test factories stopped pointing at the day job's Postgres and Elasticsearch. **Search**: the golden set was re-pooled until nothing in any variant's top ten was unjudged (268 → 450 judgments, the brand queries included), an OR fallback beside the boosted AND took the committed baseline to **es 0.927 / 0.939, en 0.935 / 0.955** (thresholds 0.91/0.92 and 0.92/0.94), and the Spanish catalogue gained the accents 200 fields never had — without moving the gate a thousandth, because the light Spanish stemmer folds them. 687 backend tests (667 green, 20 skipped). Before that, the same day: **a UI review in a real browser found nine defects under 663 green tests, and all nine are fixed** (branch `fix/ui-review-2026-09-11`, fifteen commits in one pull request to `develop`). The two that touched money first: **one shirt was refunded twice** — `ReturnRequest.Open` checked each request against what was bought and never against the returns already open, so it now takes them as a required parameter, the order page asks the same rule (`Returnable`), and a CsCheck property over request sequences guards it; and **VAT was added on top of prices the shop displays as VAT-inclusive** — a 59,96 € line charged 72,55 € — so the calculator now extracts it (ADR 0028) and `Order.Place` refuses totals that do not add up. Then: the worker rebuilds an empty search index at startup (verified live, 100 + 100 documents in 15 s); the catalogue list is the shopkeeper's (it served drafts anonymously); the backoffice guard asks for the role; a re-import replaces the gallery so the illustration is the cover; command results declare their audit verdict (`IAuditedResult`, an architecture rule and exhaustive switches — the declined card is `Denied` now) and a guest is `guest`, not `system`; an account holder's order is 404 to a stranger (`OrderAccess`); and a Catalogue tab reaches variants for published products. 684 backend tests (664 green, 20 skipped), frontend lint + tests green, both Playwright suites green against the stack. **The search-relevance question was answered too**: the thresholds dropped on 2026-09-04 because the corpus went from 6 to 100 products, but most of the low recall is the lexical query's `AND`, not a vocabulary gap — an AND-plus-OR fallback measured es 0.813/0.633 → 0.895/0.932 and en 0.766/0.556 → 0.890/0.945 with no AI at all, over a golden set that leaves 37–47 % of that variant's top-10 unannotated. Before that: **a strict SOLID review of the day's code, and what it changed.** The webhook handler's `HandleAsync` had grown to verify, look up and decide in one method; each kind now has its own, and it saves only when a decision changed the order. `EfCustomerRepository.ForSubjectAsync` lost a branch no row could reach — only a guest can be superseded, and a guest has no subject — together with the second query it cost, and gained an integration test against Postgres, which it had never had. `HttpPrincipalAccessor` memoises with the C# 14 `field` keyword. Every change re-proven by mutation: 31, none surviving. 645 backend tests (625 green, 20 skipped). Before that: **before phase 8, the delicate code with no tests of its own got some, and each test had to prove itself by failing.** Four classes nothing exercised directly — culture negotiation, the payment webhook handler, the search document and the outbox worker — got 63 tests, and **73 mutations of their code, every one turned red by at least one test**. Two of the tests failed on their very first run, which is the better finding. `Accept-Language: en;q=0` — "not English" — was answered in English, because a quality of zero was sorted last instead of refused (RFC 9110). And **every real `payment.failed` webhook threw**: the handler moved authorised orders to `PaymentFailed`, an edge the table only allows from `Pending`, and since ADR 0025 no saved order is ever `Pending` — a 500 the provider would have retried forever, on an order left alive with no money behind it. It now cancels an order whose money has not moved, so the compensation that already existed releases the stock and voids the hold (ADR 0025, amended); a failure after a capture, or on an order that shipped uncaptured, leaves the order alone and **logs a warning for a person** — the second one because the goods left and the money did not, and a return is the customer's to start, not the system's. The outbox worker had never been tested at all: three suites drained the outbox with **copies** of its loop, so its retry cap and its poison-message isolation could have gone with nothing red. Those three suites drive the real worker now, and a pass that leaves a failed message fails the test with the error the worker stored, so a broken handler is as loud as it was with the copies. `docs/testing.md` records the practice. 643 backend tests (623 green, 20 skipped with a reason). Before that, the same day: **the tariff is a fact about the customer, and the request no longer gets a say.** `Segments.For` used to honour whatever segment a signed-in shopper sent, so `"segment": "vip"` was a VIP quote, a test asserted it, and the endpoint's comment said the opposite. Checkout sent nothing and charged retail: no money leaked, but a quote could promise a price the till refused, and a real VIP was never charged as one. The principal accessor now reads the segment from `Accounts` alongside the customer id — one indexed two-column read, as before — Pricing reads it off the principal, and `QuoteCartRequest` has no `segment` field at all, so the OpenAPI document and the generated frontend types lost it too. Three tests, each watched failing first — and **the principal accessor, the one class that turns a token into "who is acting", had no tests of its own**, so it got eleven, proven by seven mutations that each turn exactly one of them red. 580 backend tests (560 green, 20 skipped with a reason), 39 frontend. Before that, the same day: **the first two articles were read against the code before they were tagged, and the reading found more than a tag.** `blog/00-from-cocktaildev-to-tendero` and `blog/01-one-port-n-catalogs` both point at the README fix cut on top of the merge of PR [#8](https://github.com/juanluelguerre/tendero/pull/8). Article 01's snippets predated `TimeProvider` and variants, five of them stale; article 00 said 33 routes and 37 frontend tests where there are 34 and 39; and **diagram 02 drew data flow as dependencies** — Catalog → Search the way the data travels, which is the reverse of the reference, three real crossings missing, and two arrows "into Search" landing on Inventory's box. It was redrawn from the `.csproj` references, with the core in layers so every dependency points down and the one arrow that points up dotted, because it is an event. Sixteen code comments still spoke the initial plan's phase numbers, and two of them hid real gaps — the next task is one of them. Earlier the same day: **the EF fix reached `develop`** as PR [#7](https://github.com/juanluelguerre/tendero/pull/7). It had been sitting on `feature/mcp-server`, so `develop` could not build its EF model and its CI had been red since 2026-09-07; it is green again. **Phase 9 is closed on `feature/mcp-server`**, pushed and deliberately unmerged, and nine merged branches were deleted. Before that: **phase 9 closed on its branch, and it grew three rows the plan did not have.** The manifest is derived from a capability registry, the catalogue capability is served over **both** of UCP's main bindings — `mcp` and `rest` — and every answer is validated against the specification's own JSON Schemas, vendored at a pinned commit. **The conformance run found six defects that reading prose had not**, and the first was that the validation itself was passing over a schema root that requires nothing; the negative control is the only reason that was caught rather than published as evidence. Verified on the full Aspire stack with the hundred products imported, published and indexed. Article 10 is written with its diagram. Before that: **the half that matters was the half I did not plan.** The MCP server mounts in the API process, five read tools became eight, and `/.well-known/ucp` is composed from a capability registry rather than written by hand. But composing it meant reading UCP's own binding, and **our `/mcp` was not it**: the specification fixes `search_catalog`, `lookup_catalog` and `get_product` with a required `meta`, a response envelope and money in minor units, so declaring `dev.ucp.shopping` would have been a lie in the one document whose entire value is being trustworthy. The binding was implemented first (`P9-10`), which cost our own `get_product` its name. Then the specification's JSON Schemas corrected three things I had reasoned: the key field is `keys`, a capability's registry value is an **array**, and a business profile **requires** `payment_handlers` — so "absent rather than empty", argued in three places, made the document invalid. Verify was declined for Respawn's reason: `UPDATE_UCP=1` reuses the snapshot mechanism the OpenAPI document already has. ADRs 0020 and 0021 written, `docs/mcp.md` written with each client's instructions read rather than remembered. Before that: **a style sweep in a documentation commit left the catalogue unable to be read, and only Docker could see it.** Seven aggregates keep their collections in private fields and seven EF configurations name those fields BY STRING; `5864494` took the leading underscore off the fields and left the strings alone. Four complex collections then threw on model build and three plain properties went quietly to shadow state, so the API could not have served a product — with `-warnaserror`, `dotnet format`, the architecture rules and 500 domain tests all green, because none of them can see inside a string. The only suite that failed is the one that needs a container, on the machine where `docker pull` had been broken for days. Seven strings repaired, `tests/Persistence.Tests` added as the guard that needs **no** database, verified by mutation both ways. **And the imaging half of `SeedImages` never worked on Linux at all**: SkiaSharp ships the Windows and macOS binaries with the wrapper and the Linux one is opt-in, so four tests failed with `libSkiaSharp` missing on the platform CI runs on. **567 tests, 547 green, 20 skipped with a reason, format gate clean.** Before that: **the catalogue is being drawn, and the generator that draws it is not the one I wrote.** `tools/SeedImages` grew a port, `IIllustrationGenerator`, with two keyed adapters: SDXL inside the .NET process on ONNX Runtime with DirectML, and one HTTPS call to `gemini-2.5-flash-image` — nano banana, the model the first eight illustrations were made with by hand. **The hosted one drew the catalogue, for quality and not for speed**: SDXL's output beside those eight is flat, grey and soft at the edges, a white product disappears against the background, and six prompt rewrites did not close the gap because the gap is the model. 18 of 100 images exist, at ~6 s and ~$0.03 each — **~$3 for the hundred** — with the vision pass reporting 18 usable and two notes, one of them a real catalogue error. The swap cost one thirty-line interface and did not touch the deterministic tail. **The returns promise landed on the product page too**, which closes the last non-scale row of the storefront backlog: the window is published at `GET /api/returns/policy` instead of being typed into a template, because fourteen days was already written twice with nothing comparing the two. Before that: **the blog caught up with the code.** The four diagrams in article 00 were a photograph of 2026-09-02 and had gone quietly wrong: two bounded contexts where there are five, UCP labelled phase 3 when it is 9 and 11, Qdrant labelled phase 2 when it is 8, a shipped agent surface (WebMCP) missing entirely, and **Grafana drawn as if it ran when the AppHost does not declare it** — the exact failure the article condemns two paragraphs above it. All four regenerated from their `.html` sources through `.svg` to 2000px `.png`, the article's own table corrected (6 endpoints → 33 routes, 117 tests → 503 + 37 + 20, 13 ADRs → 21) and the prose rewritten in plainer English. Articles 16 and 17 went from broken links out of `blog/index.md` to finished drafts in the same day — the two pieces the queue had been holding that were blocked on nothing — and then **17 folded into 16 as its closing section**, because both defects were found by building the same product detail page and that is a better piece than two. Outline 09 stopped carrying the pre-100-product baseline it would have published as a "before". Before that: a review pass for SOLID/DRY across back and front, **merged** to `develop` as PR [#2](https://github.com/juanluelguerre/tendero/pull/2) — 503 backend tests, 37 frontend and 20 browser specs green (one honest skip), every `.md` audited against the code. Since then, an evening of environment failures on Linux under Rider, none of them in the code and all three in the notebook: two dotnet roots, nvm's PATH, and a failed `docker pull` that deletes the tag it cannot replace. Before that: the storefront's look, off the board's backlog and ahead of phase 8 — **the home page is a shop now**: departments off the taxonomy phase 2 built, offers off the promotion engine, novedades off `available_from`, and a category page that browses. 495 tests, format gate clean. Merged to `develop` as PR [#1](https://github.com/juanluelguerre/tendero/pull/1) — opened as a pull request, fast-forwarded on the author's say-so, and GitHub recorded it as merged either way.
- **Next task:** **Merge `feature/design-before-code` into `develop` and push once** — authorised by the author, batched with the design-system sheet so it is one CI run rather than two. Verified before merging: 28 frontend tests green, the new spec lints clean, **nothing under `src/` is touched**, and the two public artefacts carry no paths, usernames or addresses. Then **the two kept decisions become work on the shipped panel** — the verdict sentence above the numbers and the plain-language glosses on `NDCG@10` and `recall@50`, with four i18n strings in Spanish and English — and **the dark theme gets a `--danger` and a `--warning` of its own**, which the design-system sheet proposes with measured contrast ratios. **Article 22 is ready to schedule**: the author proposes moving it ahead of the blocked article 17, taking 2026-10-13 and pushing everything a fortnight, with the precedent of 17 and 18 on 2026-09-07; it must then carry article 18's premise in a paragraph rather than assume it. After that, **`P10-1`**, the `Knowledge` context. **Still open from 2026-09-17, and still the author's**: whether a notice on about half the queries that have answers (0.481 en, 0.462 es) is too often for a shop to say. **Stryker's Search run is historical**: the next phase close measures Search again rather than ratcheting against a project that changed shape. **Parked on 2026-09-10, by decision:** the remaining 82 illustrations, which is what blocks article 17. `P7-9`'s Token Exchange still waits for phase 11.
- **Next publication:** article 01 on **2026-09-29**, tagged `blog/01-one-port-n-catalogs`. Article 00 went out on **2026-09-16** ([elguerre.com](https://elguerre.com/2026/09/16/tendero-ai-native-ecommerce-dotnet/), WordPress post 4144), with its tag already public and one fix published after it (the Vite installers). For 01: re-read it against B2 English, a TL;DR of at most three bullets and a meta description (editorial rules 9 and 10), check its snippets against its tag, send it as a draft with `/blog-post 01`, publish its tag with `docs/blog/publish-public.ps1`, and then the checklist in `blog/index.md`

**Decisions taken 2026-09-18** — 1 · **the project gains an optional second
product: a self-published technical book**, in `docs/book/`, whose spine is the
step-by-step of building Tendero with a coding agent and whose payload is the
architecture — *From Zero to a Hundred with Tendero*, English first with Spanish
left open as a second edition, scope through phase 11. The outline is not free:
it is a commitment to finish phases 10 and 11 · 2 · **the publication filter
gained a second path, and the rule that forbade it was measured rather than
argued.** Adding `--path docs/book` leaves all 273 filtered commits exactly where
they were — filtered with one path and with both, develop comes out at
`f8d5b663` and the commit behind `blog/00` and `blog/01` at `0955af5a`, with
develop's whole rev-list identical commit for commit — because removing a path
that appears in no commit yields the same tree, the same parents and therefore
the same commit.
The rule now reads: *the filter may only ever gain a path that has **never**
existed in the history, and only after a comparison run proves no hash moves* ·
3 · **the leak check names both paths**, because from inside the script a filter
argument silently dropped and a genuine leak look identical · 4 · **the notebook
gains a fourth rule** — quote the exchange verbatim when the exchange is the
point. The record holds five to eight such moments against 85 entries, and a book
may not promise what would have to be reconstructed · 5 · **a phase close files
its material into the book chapters it feeds**, which is what makes the skeleton
fill itself instead of depending on somebody remembering · 6 · **a design artefact
never becomes a source of truth and never enters the public repository** (ADR 0033). It lives
under `docs/blog/reports/` with the material it is material for, and is **kept
there as evidence** — amended the same day by the author: the first draft said it
was deleted once the article and chapter using it were written, by analogy with
diagram sources, and that analogy was wrong. A diagram source is deleted because
its export replaces it; a `.pen` is replaced by nothing, and this repository keeps
every other measurement's evidence. Deleting one is a decision, never a schedule.
Staying out of the public repository is the opposite of what an
agentic canvas is sold on, and the reason is the artefact's job rather than the
tool's quality: a sketch earns its keep by being cheap to throw away, and a file
kept in a public repository is a file somebody maintains, in a format whose owner
reserves breaking changes · 7 · **the design tool is an experiment with a removal
condition, not an adoption**, in ADR 0009's shape — one branch, one screen, the
comparison published either way. What forced the shape is a measurement:
pen.dev's MCP server answers `A file needs to be open in the editor`, so it needs
the signed-in desktop app open and **can never be a gate**, and every other
quality rule here is one.

**Decisions taken 2026-09-08** — 1 · **a closed option does not describe a colour, it picks the nearest one**, and which one is nearest is measured rather than eyeballed. `B13BAG0710`'s felt is RGB (68, 66, 62) — a warm charcoal at L* ≈ 28, neither ink nor the mid grey the word suggests — so of the two candidates the catalogue actually offers, `negro` wins by distance. Saying it that way is a smaller claim than "the sleeve is black", and it is the one the data supports. The gate was run rather than reasoned about, because colour is indexed and the margin is ~0.013. · 2 · **a member EF is told about by string is a reference the compiler cannot check**, so it gets a test that builds the model and refuses a property in shadow state — and that test lives where it needs no database, because the eighteen failures that found this one all needed Docker and Docker is what had been broken. · 3 · **the fix is the strings and not the fields**: `.editorconfig` asks for no underscore and `this.`, the sweep was right, and renaming the domain back to satisfy a persistence detail would be the tail wagging the aggregate. · 4 · `SkiaSharp.NativeAssets.Linux` is added unconditionally rather than behind an OS condition — a restore that differs per machine is the defect article 06 is about, and the package is nothing but `.so` files a Windows build never copies.

**Decisions taken 2026-09-07** — 1 · **the illustration generator is a port with two adapters**, and the one that draws the hundred is the hosted one: a catalogue with two visual registers is not a catalogue, and the eight that already existed set the register · 2 · **the local SDXL adapter stays**, because it is the only path with no key, no network and no bill, and the only one that reproduces an image from its `item_id` — `IsDeterministic` is declared on the port rather than left for somebody to discover · 3 · **the port is at the level of a BATCH**, since SDXL opens a five gigabyte session once per run and a per-image port would have made it open ninety-two · 4 · **measurements are stripped from the prompt by code**, after three rounds of asking the model not to letter `4 mm` onto a midsole: a figure beside a part reads as that part's label, and it was never earning its place in a drawing · 5 · **a vision note never fails a batch** — a model arguing about olive cannot throw away work that is fine — and a colour mismatch is repaired in the catalogue rather than by redrawing · 6 · the article-17 diagram was **redrawn as the architecture** rather than as SDXL's insides, because a diagram of one implementation would have drawn the half that did not draw the hundred.

**Decisions taken 2026-09-06** — 1 · **a diagram is a claim and it goes stale like any other**, so the four in article 00 are regenerated from source rather than patched, and the `.html` → `.svg` → `.png` chain is reproducible in one command · 2 · **Grafana came out of the stack diagram** because the AppHost does not declare it: the article's own "nothing is declared until code reads it" rule, applied to the article · 3 · `1 hit · score 8.91` came out of the flow diagram — that score was measured over six documents and the corpus is a hundred; the diagram says `the only Active one` now, which needs no measurement and makes the point better · 4 · **article 00 moves to plainer English (B2)**, because the English version exists for reach and the reader it is written for is not a native speaker · 5 · `blog/index.md` credited article 17 with `416 → 434` tests; fourteen of those are article 16's, and the notebook says so · 6 · **article 17 is not a post, it is the last section of 16** — the two defects were found by the same screen on the same week, so continuity beats a second byline, and the merge freed 2027-02-02 for the phase-0 article that was double-booked on 2027-01-19.

**Decisions taken 2026-09-05** — 1 · **the transition table is a type**: `TransitionTable<TStatus>` in the SharedKernel, and `Order`, `Cart`, `ReturnRequest` and `Reservation` declare their edges on it instead of each carrying its own guard and its own wording for the same refusal · 2 · **strongly-typed ids are converted once per type** through EF's pre-convention configuration, not fourteen times per property; the model snapshot did not move and the migration baseline test agreed · 3 · **a test walks every handler the API references and resolves it from the API's container**, because five composition failures in this notebook were all found by a person · 4 · **the screens are held to the agent tools' rule**: no `HttpClient` under `features/**`, so the order page and checkout go through an `OrderService` a tool can share · 5 · a page whose inputs are signals is an `rxResource` (the PDP) or holds its in-flight subscription and cancels it; a subscribe inside an effect lets the LAST answer win rather than the last one asked for · 6 · the home page and the stock grid reload on a language switch, as the review queue already did.

**Decisions taken 2026-09-04** — 1 · **the API declares its issuer** at `GET /api/auth/config`; a build-time flag in each app is two places that can disagree, and they did · 2 · Keycloak is reached over Aspire's own development certificate, which the OS already trusts, so no browser warning stands between a fresh clone and a token · 3 · **a cancelled order carries why it was cancelled**, as a code the interface translates plus a detail carrying the SKU and the numbers — the reason used to exist only inside a processed outbox message · 4 · every fixed port is deliberately off the default (55432, 59200, 58443), because 5432 and 9200 are somebody's work services · 5 · **Keycloak, pgweb and elasticvue are always DECLARED and started on demand** (`WithExplicitStart`), because a resource created only behind an environment variable is invisible to anybody launching from Visual Studio — the flag now decides whether the API TRUSTS Keycloak, not whether it exists · 6 · Elasticsearch's CORS origin names elasticvue instead of `*`: a starred origin on a node bound to 127.0.0.1 lets any page the browser visits delete these indexes, and naming it is only possible because the port is fixed · 7 · **sign-in is a redirect** (Authorization Code + PKCE) for BOTH issuers: the password grant put credentials in the application and made the swap invisible, and the development issuer grew `/connect/authorize` rather than stay the easier half · 8 · `angular-oauth2-oidc` 22.0.2 added after the ADR 0006 check — MIT, peers `>=22.0.0`, one transitive dependency already present · 9 · **every container carries the Compose project label** so Docker Desktop collapses them under one `tendero` group instead of listing eight loose containers among other projects' — the label is applied over the model rather than at each call site, because three of them are created by integrations.

**Decisions taken 2026-09-04 (evening)** — 1 · **the catalogue is 100 products**, not six: six was below the floor at which a shop can be judged, and it made the search gate measure ranking over a corpus where everything retrieved was annotated · 2 · **the NDCG baseline was re-derived rather than defended** — es 0.943 → 0.819, en 0.937 → 0.766 — because NDCG@10 over six documents and over a hundred are different measurements, and the recall drop (0.909 → 0.642) is the vocabulary gap becoming measurable instead of being one query per culture · 3 · the review queue shows what the import DID, and the awning stripe finally does the job `design/DESIGN.md` assigned it.

**Decisions taken 2026-09-04 (night)** — 1 · **the reindex endpoint had never worked**, and could not have: the reader held a Postgres reader open for the whole walk and Npgsql has no MARS, so phase 4's `IAvailabilityReader` — asked once per product, on the same connection — made it a guaranteed 500 with every test green. It reads in chunks now, and the regression test drives the real port through a real Postgres · 2 · **an index that already exists is never re-mapped**, so `categoryCodes` was typed `text` dynamically and a term filter matched nothing while reporting nothing; `reindex?recreate=true` rebuilds the mapping, and create and recreate now share one method so they cannot drift. Same finding as ADR 0012's `EnsureCreatedAsync`, one projection to the left · 3 · **the department pictures are illustrations, not photographs** — a stock photo of somebody else's shop is a promise this catalogue cannot keep — and the art sits BESIDE the words everywhere, so a real photograph can replace a drawing without contrast being re-solved · 4 · a department showing 24 of 55 products is a shop with 31 unreachable things; `Ver más` appends a page rather than an infinite scroll that eats the footer.

**Standing chore — cleared 2026-09-04.** The eleven commits that had piled up locally are on `origin/develop`, `0bfb443`'s GitHub Actions bump among them, so CI has finally seen every one of them. The push went through with the token already configured; whatever the `workflow` scope was refusing earlier, it is not refusing now — which is worth a line in the notebook rather than a claim here.

**Decisions taken 2026-09-02** — 1 · an ADR generalises the context principle rather than fixing a count · 2 · nothing is anonymous; the identity provider is a port whose first adapter is a development issuer, Keycloak later · 3 · the variant is the indexed unit and the product the returned one, via `collapse` · 4 · licensing splits into two tiers, so the Grafana stack is back in · 5 · UCP is split, read capabilities in phase 9 and the transactional half in phase 11.

**Decisions taken 2026-09-03 (evening)** — 7 · WebMCP offers `get_product` and **not** `apply_filter`: a filter tool over a shop with no facets is speculative, and facets land in phase 8 · 8 · the "same services the UI calls" rule is an **eslint boundary**, because review cannot enforce it · 9 · the browser's model context is injected rather than read from `navigator`, so the degradation is testable where it actually happens.

**Decisions taken 2026-09-03** — 1 · the outbox is the process manager, orchestrated from `Ordering` (ADR 0024) · 2 · checkout authorises the payment before it places the order, so a decline costs nothing (ADR 0025) · 3 · cart lines are jsonb, not a table — mutating one through the aggregate is not querying it · 4 · returns are their own aggregate, because they are per line · 5 · Playwright stays the thinnest layer, and no self-healing agent touches a suite whose point is what the system refuses · 6 · **the URL carries a public code and the slug is decoration** (ADR 0026), because a slug cannot be unique inside jsonb and does not survive a rename. The Shopify alternative — a slug table with history and `-2` suffixes — was built halfway and rejected for managing both problems rather than removing them.

---

## How this is ordered

The sequence optimises for **what it evidences**, not for what is cheap. Nothing
is cut for being laborious. What *is* constrained is data volume: a hundred
products, two warehouses, three roles. Scale work is deferred with its measured number
recorded, which is better material than building it.

Two hard dependencies drive everything: **auth unlocks the agent economy**
(UCP, mandates, the activity panel), and **cart/checkout unlocks everything
transactional**. Phase 0 comes first because the NDCG gate, the architecture
rules and the OpenAPI contract test are theatre until something runs them on a
pull request.

**Sizes are sequencing information, not a budget.** S fits in one session, M in a
few, L is a project. Use them to decide what fits tonight, never to decide what
to drop.

---

## Publication calendar

**Fortnightly, decoupled from shipping.** Write when a phase closes and the
notebook is warm; publish on the date regardless. This is the single most
important process decision in the file, because the failure mode of a developer
blog is not running out of material — it is publishing four things in a week and
then vanishing for two months.

You start with an unusual advantage: **ten finished drafts and one half-written**.
That is roughly five months of publishing without writing a new line,
during which phases 0–5 refill the queue.

| Date | Article | State |
|---|---|---|
| 2026-09-16 | 00 · From CocktailDev to Tendero | **published** — [elguerre.com](https://elguerre.com/2026/09/16/tendero-ai-native-ecommerce-dotnet/), tag `blog/00-from-cocktaildev-to-tendero` |
| 2026-09-29 | 01 · One port, N catalogs | ready — tag `blog/01-one-port-n-catalogs` |
| 2026-10-13 | **17 · A hundred pictures, and not a line of Python** | outline — blocked on the hundred images existing |
| 2026-10-27 | **18 · Every screen was correct and none of them was a shop** | outline — needs writing; the screenshots need 17 to have happened |
| 2026-11-10 | 02 · Search with regression tests: NDCG as a CI gate | ready |
| 2026-11-24 | 03 · The catalogue nobody could find | ready |
| 2026-12-08 | 04 · Your build is green and nothing works | ready |
| 2026-12-22 | 05 · The parameter and the header are not alternatives | ready |
| 2027-01-05 | 06 · A repository that built on exactly one machine | ready |
| 2027-01-19 | 07 · The feature flag you did not need | ready — short on purpose, do not pad |
| 2027-02-02 | 15 · The bot you did need, and the two you didn't | ready — pairs with 07, publish it right after |
| 2027-02-16 | 16 · The URL that guessed which product you meant | ready — carries what was article 17 as its closing section |
| 2027-03-02 | **NEW** · from phase 0 | write when P0 closes |
| 2027-03-16 | **NEW** · from phase 2 | write when P2 closes |
| 2027-03-30 | **NEW** · from phase 3 | write when P3 closes |
| … | one per closed phase, in order | |

**Two articles were inserted at 2026-10-13 and 2026-10-27 (decided 2026-09-07),
and everything after them moved a month.** The reason is an arc rather than a
preference: 01 ends with a catalogue full of products nobody can look at, 17
fills it with a hundred illustrations, and 18 finally shows the shop — with
photographs in it, which is what makes its screenshots worth taking. It also puts
the **only visual piece in the queue** early, in a series whose first ten
articles are failures and numbers without a single image.

The old table skipped 2027-02-16, which fortnightly publishing does not; the
dates above are recomputed rather than shifted, so that gap is gone.

**Every article links a tag named after its file**, `blog/NN-slug`, cut once its
snippets and numbers have been checked against the code. 00 and 01 were cut on
2026-09-10, and checking them was not a formality: article 01's snippets predated
`TimeProvider` and variants, and diagram 02 drew data flow as dependencies and
left three of them out — redrawn the same day. What remains for both is the
WordPress upload, which is part of publishing rather than a prerequisite for it.

---

## Editorial rules — how to make each one land

Reusable criteria, derived from what already worked in this repository.

**1 · Lead with the failure or the number, never the architecture.** Your own
notebook proves it: *"the gate caught 7 of 22 queries returning zero results"*
beats *"I built an evaluation harness"*. Your strongest existing titles —
*"Your build is green and nothing works"*, *"The catalogue nobody could find"* —
are all of this shape, and none of them was in the original plan. The tutorials
were.

**2 · One auditable before/after number per article.** Already a repo rule: a
draft may not contain a number the notebook cannot account for. This is what
separates the writing from the 95% of technical content that is opinion.

**3 · Show what the system REFUSES.** The suppressed discount with its reason.
The agent checkout denied because the mandate only covered 200 €. The idempotent
publish that saves nothing. A system that says no is memorable; a system that
says yes is a demo like any other.

**4 · Keep being honest about what broke.** *"Being wrong in public"* and
*"claiming a diagnosis before reading the logs"* are already in the notebook.
That voice is the competitive advantage, and it is precisely what a senior
architect recognises as the signal of another one.

**5 · Chain them.** Each article closes with the question the next one opens. A
series recruits readers; standalone posts do not.

**6 · The social post is not the article.** First line is the number or the
failure. Then the visual. Then three lines of context. Link last. `#ecommerce`
plus two or three specific ones (`#dotnet`, `#AIEngineering`, `#MCP`,
`#agenticcommerce`). **English on both LinkedIn and X**, in the same plain B2 English as the articles (decided 2026-09-16; it used to be Spanish on LinkedIn). LinkedIn allows bullets and a few emoji; a post on X stays under 280 characters, or becomes a short thread.

**7 · Treat the UCP article as a launch, not a post.** *"There is no .NET
reference implementation of UCP"* is the single most distinctive claim available
to this project. It belongs in the README, in a CV, and in a conference talk
proposal — not only in one article on one Tuesday.

**8 · Every article ends with *How we know it works*** (decided 2026-09-15, announced
in article 00). Three lines: the test that failed first or the deliberate breakage
that proves the claim, the gate that holds it on every pull request, and — from the
first article whose tag contains Stryker.NET — the mutation score of the code the
article is about. Only what the article's own tag contains: an older tag says
"not yet" rather than borrowing today's number.

**9 · Every article opens with a TL;DR, and is found by what it is about** (decided
2026-09-16, first applied to article 00). Right under the title — after the hero
image when there is one — three to five bullets a reader can stop at, each a claim
the article itself makes. And search is not an afterthought, as long as it never
overrides rule 1: a meta description of at most 160 characters, a slug and a title
carrying the words people search for (the project, .NET, ecommerce, AI, agents —
only where true of the article), and a heading or two that says the same. The story
still leads; the keywords make it findable.

**10 · Plain English, at B2 level, and a short TL;DR** (decided 2026-09-16; the
first move towards it was article 00's on 2026-09-06). The articles are read by
people for whom English is a second language, so they are written for an upper-
intermediate reader: short sentences, common words, one idea per sentence, and no
idiom a non-native reader would have to look up — technical terms stay, and the
glossary explains them. The TL;DR has **at most three bullets, one short sentence
each**; if it needs more, the article is not clear yet. Tendero's pre-flight
refuses a TL;DR with more than three.

---

## Phase 0 · Make the gates real

`hecha` (2026-09-02) · priority **critical** · size **M**

Everything in this phase already exists as a promise somewhere in the docs. This
turns each promise into something a pull request enforces.

- [x] `P0-1` GitHub Actions: build with `-warnaserror`, run all tests, run the architecture rules — **S** · also runs the Nx module boundaries, which were enforced by nothing
- [x] `P0-2` Elasticsearch as a service container; run `SearchEval --ci` on every PR and post the report as a comment — **S** · sticky comment, job summary as the fork-safe fallback
- [x] `P0-3` `Microsoft.AspNetCore.OpenApi`; document generated by the contract test into `docs/openapi/tendero.json`, committed — **S** · endpoints moved to `TypedResults` so the schemas are real, plus a transformer narrowing `[number, string]` to `number`
- [x] `P0-4` Contract test: committed document equals the runtime one — **S** · `UPDATE_OPENAPI=1` regenerates; verified it fails on drift
- [x] `P0-5` `openapi-typescript` generating `shared-api` types; hand-written clients kept (ADR 0010) — **S** · run via pinned `npx`, not a devDependency: its peer excludes TypeScript 6
- [x] `P0-6` EF Core Design + an initial migration reproducing today's schema exactly; `MigrateAsync` replaces `EnsureCreatedAsync` — **M** · EF tools pinned in `.config/dotnet-tools.json`; the machine's global `dotnet ef` was 10.0.2 and useless against an 11 runtime
- [x] `P0-7` Testcontainers + a database per test; four integration tests — **M** · Respawn declined, a fresh database per test is cheaper than a reset; tests skip with a reason when Docker is absent
- [x] `P0-8` `TimeProvider` injected into `Product` and `Order` — **S** · 53 call sites, which is the argument for doing it at two aggregates rather than eight
- [x] `P0-9` `SearchEval --suite` switch, search suite behind it — **S** · baseline reproduced identically afterwards
- [x] `P0-10` `TelemetrySources` constants replacing the four string literals — **S**
- [x] `P0-11` `dev-issuer`: OIDC discovery + JWKS + token minting for three seeded identities. `AddDevIssuer` throws outside Development — **M**
- [x] `P0-12` `AddJwtBearer` + CORS + the policies; `ClockSkew = 0` so an expiry test cannot pass by accident — **S**
- [x] `P0-13` `IPrincipalAccessor` in SharedKernel, with `CommercePrincipal` and `AgentId` — **S**
- [x] `P0-14` Every endpoint carries a policy or an explicit `AllowAnonymous`; two tests enforce it in both directions — **S**
- [x] `P0-15` `IdentityProviderContractTests`, abstract; the dev issuer inherits it in three lines and Keycloak will too — **M**
- [x] `P0-16` Backoffice login: identity picker, token interceptor, route guard, sign-out — **M**

**Risks.** The migration baseline is the one to get right: generate it against a
database created by the current model, and diff it, or every later migration
inherits the error. Preview EF Core may generate something odd — that is a
notebook entry either way.

The dev issuer's risk is scope creep: it exists to sign tokens for six seeded
identities, not to manage users. If it grows a registration flow or a password
reset, stop — that is the moment Keycloak was supposed to arrive. A hard guard
helps: refuse to start when `Environment` is not Development.

**Demo.** A pull request with green checks and a bot comment reading
`es NDCG@10 0.860 · en 0.720 · thresholds met`. Plus a backoffice that now asks
who you are before letting you publish.

**Evidences.** Engineering maturity; making quality gates executable rather than
documentary; contract-testing an API surface; managing a preview toolchain;
**treating the identity provider as a port so the real security path runs from
day one against a fake issuer**.

**Article.** *"The gate that never ran"* — a repo carrying a "tracked in CI"
badge with no CI, and what it costs to make eight documented guarantees real.
Opens the second half of the series. Write at close, publish 2027-02-02.
`#dotnet #CI #testing #ecommerce`

---

## Phase 1 · Variants

`hecha` (2026-09-02) · priority **high** · size **L**

- [x] `P1-1` `VariantId` in SharedKernel; `Variant` child entity with SKU, price, axis values, tax class — **M**
- [x] `P1-2` `Variants` as a table (ADR 0008 amendment) — **M** · `VariantExternalReferences` is NOT built: the seed carries no per-variant source ids, and the table waits for the connector that does (the Shopify concern `P1-10` names)
- [x] `P1-3` Implicit default variant minted at import, so every purchasable thing is a variant — **S**
- [x] `P1-4` `OrderLine` grows `VariantId`, `Sku`, `VariantLabel` (snapshot) — **S**
- [x] `P1-5` Index is one document per `(variant, culture)`; `inStock` waits for Inventory (phase 4) — **M**
- [x] `P1-6` Query collapses on `productId`; the hit carries the winning variant's id, SKU and price — **M** · gate reproduces the baseline exactly, which was the whole argument
- [x] `P1-7` Product counts via a `cardinality` aggregation, not the hit total — **S**
- [x] `P1-8` `DefineVariants` slice + backoffice matrix generator — **M** · the whole cartesian product, with the count shown before you press
- [x] `P1-9` Storefront: price range on the card — **M** · **the variant picker is NOT done**: it needs a PDP, and there is no product detail page yet. Moved to the cart phase, where it is needed rather than decorative
- [x] `P1-10` ADR 0015 written and indexed — **S** · the connector half of the contract waits for `ExternalProduct` to carry variants, which is a Shopify-connector concern (phase 2). The default-variant guarantee is a mapper test today, because minting it is the mapper's job, not a connector's

**Risks.** The baseline must not move. Run the gate before and after; collapsed
results are product results, so the numbers should be identical — if they shift,
the collapse changed relevance and that is a bug, not an improvement. Second
risk: EF preview and a child entity table, with the backing-field friction ADR
0008 already documents. Third: deep pagination with `collapse` has rough edges —
find them at six products rather than later.

**Demo.** A card showing `24,90 – 29,90 €` with the matched colour already
selected. Then the real one: filter `navy + 38` and watch a product that has navy
in a 40 **not** appear.

**Evidences.** Choosing the indexed unit for reasons you can defend, and knowing
the false-positive facet problem exists before a customer finds it; refactoring
an aggregate without moving a measured baseline; knowing when a snapshot grows.

**Article.** *"The filter that promised a product that didn't exist"* — why
matching happens per variant and results come back per product, and what
`collapse` costs in exchange. `#search #ecommerce #Elasticsearch #DDD`

---

## Phase 2 · Localized attributes and taxonomy

`hecha` (2026-09-02) · priority **high** · size **L** · **es 0.860 → 0.943, en 0.720 → 0.937**

The highest-value phase in the floor, because it is the one that produces a
number.

- [x] `P2-1` `AttributeDefinition` aggregate, plus **aliases**: the seed sends `genero`, the label is `género`, and matching on the label would miss on the accent in silence — **M**
- [x] `P2-2` `Product.Attributes` is a typed `AttributeValue[]` — **M**
- [x] `P2-3` `IAttributeDefinitionReader` port, cached per process — **S** · invalidation lands with the editing screen; nothing can change them at runtime yet
- [x] `P2-4` The mapper resolves source keys and values against definitions; unmatched values keep the raw text rather than being dropped — **M** · the Draft-definition proposal lands with the review screen
- [x] `P2-5` `Category` aggregate: localized name, parent, materialized path. 13 categories, three levels — **M**
- [x] `P2-6` `attributesText` per culture; `categoryPathText` analysed and `categoryCode` keyword — **M** · the whole branch is indexed, not the leaf, which is what did most of the work
- [x] `P2-7` `seed/attributes.sample.json`: 14 definitions, 9 options, es+en — **S**
- [x] `P2-8` Measured: **en 0.720 → 0.811, recall 0.682 → 0.773; es unchanged** — **S** · Spanish not moving was the regression test
- [x] `P2-9` English thresholds raised to 0.80 / 0.76, same margin as before — **S**
- [x] `P2-10` Backoffice: attribute definitions screen with es/en columns and a missing-translation tag — **M** · read-only, and said so: editing needs persistence, and the table for it was created and dropped the next day for having no reader

**Risks.** The Spanish score must **not** move. If it does, the rendering changed
something it should not have — that is the regression test, and it is worth
saying so out loud in the article. Second risk: retrofitting definitions over
existing free-text attributes is the messiest data migration in the roadmap.

**Demo.** Type "navy blue" into the English label of the `NAVY_BLUE` option,
reindex, search `navy blue shoes` in English. Zero results becomes first place.

**Evidences.** Search quality as a consequence of the domain model rather than of
query tuning; measuring a change in isolation; i18n as a modelling problem.

**Article.** *"The three queries that scored zero, and what they were really
telling me"* — the flagship of the floor. One table, four numbers, one cause.
`#search #i18n #Elasticsearch #ecommerce`

---

## Phase 3 · Pricing and promotions

`hecha` (2026-09-02) · priority **high** · size **L** · **73 pricing tests, 239 in total**

- [x] `P3-1` `Money` grows subtraction, division, percentage, comparison, a named rounding policy and largest-remainder allocation — **S**
- [x] `P3-2` `src/Pricing` context; pure `IPriceResolver` and `IPromotionEngine` — **M** · an architecture rule computes, by reflection, that Pricing references SharedKernel and nothing else
- [x] `P3-3` `PriceList` + entries; two lists (`retail`, `vip`) — **M** · **keyed on SKU, not `VariantId`**: the internal id is a GUID v7 minted per import, so a tariff keyed on it would expire on every reimport — the same call the golden set already made
- [x] `P3-4` Four effects: `PercentOffLine`, `AmountOffOrder`, `BuyXGetY`, `FreeShipping` — **M** · four branches in one place; the effect is data and the arithmetic is the engine's
- [x] `P3-5` `CombinationPolicy` as a declarative table, in the `AllowedTransitions` idiom — **M** · three rows, two booleans, and a test that walks the enum in full
- [x] `P3-6` `AppliedDiscount` carries a `RuleReason` — **S** · suppressed promotions come back too, with a code a test asserts and a text in es+en
- [x] `P3-7` `ITaxCalculator` keyed adapters + contract suite — **M** · `flat-vat` and `zero`, seven shared assertions each
- [x] `P3-8` `PriceQuote` with expiry and input hash; ADR 0016 — **M** · the hash covers the tariffs and promotions too, which is the half that gets forgotten
- [x] `P3-9` `POST /api/pricing/quote`, callable before Cart exists — **S** · `AllowAnonymous`, but the **segment comes from the principal and never from the body**: a guest asking for `vip` is quoted `retail` · **only true for guests until 2026-09-10**: anybody signed in got whatever segment they sent. The request carries no segment now, and the principal carries the one `Accounts` holds
- [x] `P3-10` CsCheck properties — **M** · seven of them at 2,000 carts each, and **one found a real bug**: the engine matched lines by SKU, so a cart with the same SKU twice threw while allocating an order discount. Matches by position now
- [x] `P3-11` Backoffice promotions screen with a combination column — **M** · read-only, and says so: promotions come from a committed file, and the table for editing them waits for an editor

**Risks, and what actually happened.** Rounding was the predicted one, and it
behaved: `Money.Allocate` plus a single named policy applied once at the end
held across 5,000 generated allocations. The risk that *bit* was not on the list
— matching lines by SKU instead of by position, found by a property test in a
cart the API cannot currently produce. Scope creep in effect types did not
happen: four, and no more.

**Demo.** `POST /api/pricing/quote` with two shirts and a pan: two discounts
applied and a third **suppressed**, with the reason printed —
*"No acumulable con Rebajas de verano."* Then the same cart as a guest and as a
signed-in VIP, and watch the unit price change without the request changing.

**Evidences.** Modelling complex business rules declaratively; property-based
verification of a combinatorial space; keeping a context pure enough to test.

**Article.** *"The discount that didn't apply, and why the shop should say so"* —
combination rules as a table, and explainability as a domain concern.
`#DDD #ecommerce #dotnet`

---

## Phase 4 · Inventory

`hecha` (2026-09-03) · priority **high** · size **M** · **303 tests, es 0.943 / en 0.937 unmoved**

- [x] `P4-1` `src/Inventory` context; `Warehouse` (two), `StockItem(Sku, WarehouseId)` — **M** · keyed on **SKU, not `VariantId`**, which is what keeps Inventory from referencing Catalog at all
- [x] `P4-2` `Reservation` with its own transition table — **M** · four states, and a refusal is born `Released` carrying its reason
- [x] `P4-3` `IStockLedger` (reserve/commit/release/receive/**count**), `IAllocationStrategy` keyed + contract suite — **M** · `count` was not on the list and had to be: receiving nothing is a delivery that did not happen, and only a stocktake can say zero
- [x] `P4-4` Saga handlers over the existing outbox, both directions including compensation — **M** · **orchestrated from Ordering**, because the roadmap's own architecture rule contradicted its arrows (ADR 0024). Four integration tests drive it through a real Postgres and the real outbox
- [x] `P4-5` Architecture rule: `Inventory.*` must not depend on `Ordering.Domain` — **S** · two rules, and both had to be checked against a real coupling: the compiler omits an unused ProjectReference from the manifest, so a rule written against references alone passes vacuously
- [x] `P4-6` `StockLevelChanged` → `inStock` on the search document (ADR 0007 widening) — **S** · and exposed on the hit, because a field nobody reads is the half-built seam this repo already complains about
- [x] `P4-7` Backoffice stock grid, editable; reservations list — **M** · the first screen in the backoffice that **writes**

**Risks, and what actually happened.** Compensation was the predicted one and it
behaved — but only because the test drains the outbox **twice**: the cancellation
the saga itself causes is a new message, and a test that drained once would have
looked green while the release never ran.

Three failures were not on the list, and all three are the same failure.
**Composition is invisible to every gate this repository owns.** The saga shipped
with the worker scanning only `Search`'s assembly, so orders would have been
placed, messages drained, and no stock ever held — the outbox marks a message
processed whether or not anybody handled it. The NDCG gate composes its own
container and had been broken since the indexer grew an `IAvailabilityReader`;
nobody noticed because nothing runs it locally. And the seeder filtered its zero
rows out, so the out-of-stock product the demo exists to show had no row at all.

The fourth was real and only phase 4 could have caused it: `delete_by_query`
aborts with a 409 when a document's version moved under its snapshot, and two
projections now rebuild the same product. **Ten dead-lettered messages, and
`inStock` stuck false on a shop that had stock.** The projection writes first and
sweeps after now, which also closes the window where a published product was
briefly absent from the index.

**Demo.** Type 0 into the pans row in the backoffice. The count goes through the
ledger, raises `StockLevelChanged`, drains through the outbox, reindexes the
product — and the storefront card comes back desaturated with *Sold out* beside a
coffee maker that is not. Then the refusal: an order for more than exists cancels
itself, and the reservation is there in `Released` saying
*"PANS: 2 asked for, 1 available."*

**Evidences.** Cross-context consistency without shared entities; a saga built on
an outbox rather than a framework; compensation as a first-class path.

**Article.** *"My saga was already there and I hadn't noticed"* — the outbox as
the process manager, and the three composition failures that no build, no test
and no architecture rule could see. Write at close, publish per calendar.
`#distributedsystems #DDD #ecommerce`

---

## Phase 5 · Cart, checkout, payments, returns

`hecha` (2026-09-03) · priority **critical** · size **XL** — the gateway phase · **434 tests, es 0.943 / en 0.937 unmoved**

Everything agent-native depends on this being real.

- [x] `P5-1` `Cart` aggregate; lines as a table; guest token; expiry — **M** · lines are **jsonb, not a table**: the roadmap's reason was "UCP mutates single lines", and mutating one through the aggregate is not querying it. ADR 0008's actual criterion sends them to a column
- [x] `P5-2` `Address` value object; shipping and billing on `Order` — **S** · in the **SharedKernel**, because three contexts need the shape and none owns it
- [x] `P5-3` `IShippingRateProvider` keyed (`flat-rate`, `zone-rate`) + contract suite — **M** · **not `weight-bands`**: the catalogue carries no weight, and adding one changes `attributesText`, which moves a measured search baseline for a reason unrelated to search. The two adapters differ in the input they READ instead, which tests the port better
- [x] `P5-4` `IPaymentProvider` keyed (`fake` with failure injection) + contract suite — **M** · the suite ADR 0003 described in the present tense and `docs/testing.md` named by filename. **Four** operations, not three: `VoidAsync` exists because an order that authorises and cannot be filled has to give the hold back. `stripe-mock` needs a NuGet package and a container — a dependency decision, not a coding one
- [x] `P5-5` `PlaceOrder`: quote revalidation, hash mismatch path, idempotency key — **M** · revalidation re-runs the **same engine** through one confined adapter (ADR 0025); a checkout with its own arithmetic would compare two hashes from two systems and call it agreement
- [x] `P5-6` Payment webhook endpoint with signature verification and replay protection — **M** · HMAC over `<timestamp>.<raw body>`, fixed-time compare, five-minute tolerance. Three refusals and three verdicts: forging, replaying, and none of our business
- [x] `P5-7` `ReturnRequest` aggregate + its transition table; window opens on `OrderDelivered` — **M** · six states, and the edge that is easy to leave out is `Received → Rejected`: the parcel arrived and the goods are not what the reason claimed
- [x] `P5-8` Refund via the payment port; restock via the stock ledger — **M** · **receiving** restocks, not approving, and damaged goods never do. The refund carries the returned lines' share of the order's discount and tax
- [x] `P5-9` Storefront: cart, checkout, order confirmation, order page with a return request — **L** · the cart shows **suppressed** promotions with their reason, which is the phase-3 engine finally visible
- [x] `P5-10` Backoffice: orders list, ship/deliver, returns queue — **L** · the orders table shows **authorised** and **captured** apart, because a shipped order that was never captured is the row worth finding
- [x] `P5-11` Playwright — **M** · **6 backoffice specs + 8 storefront**, 7.8s, against the real stack. The storefront half waited on the PDP because the PDP changed the flow it would test. The pyramid's thinnest layer on purpose: the loop is already covered against a real Postgres, so these assert only what a browser can — the guard, the interceptor, and a table that never keeps its own copy of `AllowedTransitions`. Verified by mutation: a heading downgraded to a `<p>` turns the tab spec red
- [x] `P5-12` Delete the dead `product.addToCart` and `nav.orders` keys by using them — **S** · both used. `nav.agents` and `nav.search` stay dead on purpose: they belong to phases 11 and 8, and they are the gap analysis written in i18n
- [x] `P5-13` **The locale and the query go into the URL** — **M** · `?q=` and now `/es/…`. A path with no language gets one rather than a 404, switching NAVIGATES instead of flipping a signal, and the PDP carries canonical + `hreflang` listing itself among its alternates. One route table mounted once per culture, so `/es/cart` and `/en/cart` cannot drift
- [x] `P5-14` `GET /api/catalog/products/{code}` — **M** · not on the original list, and it should have been: the PDP was three tasks' blocker and had no query. Reads the catalogue and not the index, because the page needs structured attributes and out-of-stock variants and has to survive Elasticsearch being down. Catalog became the third context to reference Inventory's ports, so the picker renders **once**. Keyed on a **public code**, not the slug: a slug cannot carry a unique constraint inside jsonb and does not survive a rename, so looking a product up by one meant guessing between two and 404ing every link on a typo fix (ADR 0026). The first version WAS by slug, and replacing it deleted three runtime-only failures along with the hand-written SQL that caused them

- [x] `P5-15` **The product detail page** — **L** · not on the original list, and it is what `P1-9`, `P5-13` and `P5-11` had all been waiting on. The picker shows every option the catalogue defines with **three** states — chosen, sold out (selectable), not sold (disabled and still rendered) — because an option that vanishes tells a shopper their size is not made. Building it found two older defects: `DefineVariants` was **unreachable for the entire shipped catalogue** (the axis guard counted variants rather than coordinated ones, and importing mints an implicit `-DEFAULT`), and an axis with no definition rendered a control with nothing to press

**What building it found.** Two defects older than this phase, both invisible
until a screen needed them. `DefineVariants` had tests, shipped in phase 1, and
could not run on a single real product — every imported product carries an
implicit `-DEFAULT` variant, and the guard refused to declare axes while any
variant existed. And the seed declares `COLOR` but no `SIZE`, so the axis came
back with an empty option list: a dead control above six variants that plainly
had sizes.

**Old text, kept because it was the plan.** The product detail page had a
**query** and not yet a screen. Phase 1 deferred the variant picker to "the cart phase, where it is
needed rather than decorative", and the cart phase went straight from the search
card to the basket — which works at one variant per product and would not at
eight. `P5-14` closed the half that was three tasks' blocker; the Angular route
is what `P5-13`'s locale segment, phase 1's picker (`P1-9`) and the storefront
spec (`P5-11`) now wait on, and it is the next session's first task.

**Risks, and what actually happened.** The scope creep the phase was warned about
did not happen: no gift cards, no partial shipments, no split payments. Returns
stayed out of `Order` and are per line.

What bit instead was **composition, for the fifth time**. Carter only scans
assemblies that reference it **directly**; `Ordering` inherited it transitively,
compiled cleanly, and cart, checkout, returns and the webhook were absent from
the running API and from the OpenAPI document with every test green. And
`IPrincipalAccessor` documented the worker case while nobody had registered
`SystemPrincipalAccessor` — caught this time by a test, which is the first time
one of these was found before a person found it.

The migration baseline test earned its keep too: EF generated `DEFAULT '{}'` on
the new jsonb columns, the model declares none, and the schema comparison went
red on the drift.

**Demo.** The whole loop on six products, and the refusals are the point. Add two
pairs of shoes: the cart shows one discount applied and **four suppressed, each
with its reason**. Pay with the card that declines — no order, no reservation,
the basket untouched. Pay again with the good one: stock is held and the order
confirms itself through the outbox. Ship it and the money is captured and the
shelf goes 4 → 2. Deliver it, send one back, and receiving puts the shelf at 3
before a euro moves.

**Evidences.** Owning the commerce plane; ports with contract suites across three
adapters; a state machine that stays readable because the second one is separate.

**Article.** *"Returns are not a state of the order"* — why a five-state
per-order machine was the wrong answer, and what the `OrderDelivered` comment had
already predicted. `#DDD #ecommerce #dotnet`

---

## Phase 6 · WebMCP

`hecha` (2026-09-03) · priority **high** · size **M** — the cheapest differentiator · **6 agent specs, 28 frontend tests**

- [x] `P6-1` `libs/shared/agent`: registration mechanism, feature-detected — **S** · the browser's context is an **injection token** defaulting to null, because reaching for `navigator` inside the service makes the degradation untestable in the one environment that matters — jsdom has no `modelContext`, so every test would take the "unsupported" branch and the registration path would never run
- [x] `P6-2` Storefront tools — **M** · `search_products`, **`get_product`**, `add_to_cart`, `get_cart`. **Not `apply_filter`**: a filter tool over a shop with no facets is a tool over something that does not exist, and facets are phase 8's aggregations. `get_product` replaced it because `add_to_cart` takes a SKU and only that says which SKUs exist
- [x] `P6-3` Tools call the same Angular services the UI calls — **S** · an **eslint boundary**, not review: a tool file may not import `HttpClient`. Verified by mutation in both directions
- [x] `P6-4` Descriptors in one file, so a spec change is one edit — **S** · and a spec asserts the exact list, so a fifth tool is a deliberate act rather than a drift
- [x] `P6-5` Degradation: without `navigator.modelContext` the shop is unchanged — **S** · tested in jsdom (the real absence, not a simulated one) **and** in a browser: no errors, no visual difference, nothing registered

**What the demo actually shows.** An agent inside the shopper's tab searching,
being **refused** a one-character query and an unknown code, adding two variants
to *their* cart, and reading it back with the suppressed discounts and their
reasons — *"10% en ropa: not applied — No acumulable con Rebajas de verano."*
The phase-3 promotion engine reaching a model, refusals intact. **Nothing in that
flow authenticates**, which is the entire point.

**Risks.** The spec is under incubation at the W3C Web Machine Learning CG and
Chrome-only — label it as such in the article rather than presenting it as
settled. It will change; that is fine because it is additive and degradable.

**Demo.** In Chrome, ask the browser agent to find a navy backpack under 40 € and
add it to the cart. It happens **in whatever session you already had** — guest or
signed in — because the agent inherits it. No second principal, no token of its
own, no mandate to verify.

**Evidences.** Reading an emerging standard and integrating it without betting on
it; understanding that the trust model — not the transport — is what distinguishes
agent surfaces.

**Article.** *"Three ways to open a shop to agents, and when each one is right"* —
WebMCP vs an MCP server vs UCP, compared by trust model. Almost nobody can write
this one, and it is the piece that positions everything after it.
`#WebMCP #MCP #agenticcommerce #ecommerce`

---

## Phase 7 · Accounts, audit, and swapping the issuer

`hecha` (2026-09-04, Token Exchange excepted) · priority **critical** · size **L** · **the swap was two environment variables**

Authentication already works from phase 0 against the fake issuer. This phase
adds the *domain* half — who the customer is — and then swaps the signer for a
real one, which should be a configuration change if phase 0 was done properly.

- [x] `P7-1` `src/Accounts`: `Customer` with nullable `Subject`; guests first class — **M** · a guest is a `Customer` with no subject, **not the absence of one**. `Segment` lives here rather than in `Pricing`, because a segment is a fact about a PERSON and Pricing is a pure function of values. No roles: they live in the token and map to policies, and a domain storing one would have two sources of truth for authorisation
- [x] `P7-2` `ClaimGuestAccount(cartToken, subject)` — link or merge on login — **M** · built as **two** commands in two contexts, not one: `POST /api/accounts/me` (Accounts) establishes the customer, `POST /api/cart/claim` (Ordering) attaches the basket. One operation would mean a context reaching into the other, and **Ordering will need Accounts for order history**, so the dependency this way would close a cycle — an architecture rule now says so. Three outcomes: registered, linked (the guest keeps its id, so yesterday's order stays theirs) or recognised (the guest is superseded, never deleted). The subject comes from the token and never from the body; a merge refuses anything that is not a guest, which is the security boundary rather than tidiness
- [x] `P7-3` Audit decorator on the dispatcher; `audit.AuditEntries` — **M** · writes on its OWN connection, outside the command's transaction (ADR 0027): a row inside it disappears exactly when the command fails, so the log would hold every success and no refusal. Three outcomes and not two — `Denied` is the system WORKING, and it is the row phase 11's panel opens on. Validation failures and queries leave no row. Credentials are redacted, which is not hypothetical: the guest claim and checkout both carry a cart token
- [x] `P7-4` Audit screen; storefront account page with order history — **M** · the audit screen **opens on the refusals**, because a log whose default view is a thousand successful publishes is a changelog — the same reason denials have their own partial index. Agent rows render in clay (`design/DESIGN.md`), so scanning shows at a glance what an agent did rather than a person. The storefront got auth wiring it never had: `GET /api/orders/mine` is a **separate slice** from the shopkeeper's list, because a nullable customer filter sitting one refactor away from a shopper's screen is a leak the type system would not mention
- [x] `P7-5` Verify the Aspire Keycloak integration's licence and exact version against 13.5.3 (ADR 0006 discipline) — **S** · **passes both halves** where `Aspire.Hosting.Elasticsearch` failed: MIT declared in the nuspec, and its only Aspire dependency is exactly the `Aspire.Hosting 13.5.3` the AppHost resolves. One trap worth knowing: **all 46 published versions are previews**, so `dotnet package search` returns "No results found", which reads exactly like a package that does not exist
- [x] `P7-6` `AddKeycloak(...).WithRealmImport(...)`; realm JSON committed — **M** · two failures only running it could find, and both looked identical: a token that validated perfectly while every policy denied. Keycloak treats `.` in `claim.name` as a **nesting separator**, so `ClaimTypes.Role`'s URI became an object rooted at `http://schemas`; and declaring `clientScopes` at realm level **replaces** Keycloak's built-in set, so `profile`, `email` and `roles` stopped existing
- [x] `P7-7` Keycloak inherits `IdentityProviderContractTests` — **S** · **and it did NOT pass unchanged, which is the finding.** The suite encoded its only adapter three times: a relative path, the test server's channel, and a JWKS at the dev issuer's location. It follows the `jwks_uri` discovery advertises now, which is truer and is what `JwtBearer` actually does. *A contract suite with one implementation has not been tested as a contract.* 6/6 green against a live realm
- [x] `P7-8` Point `AddJwtBearer` at Keycloak — **S** · **two environment variables.** `Program.cs` defaults the authority with `??=`, so setting it IS the swap: no code path, no second scheme, no branch. Verified live — shopkeeper 200, agent 403, no token 401, public catalogue 200
- [~] `P7-9` Agent `client_credentials` and Token Exchange — **M** · **`client_credentials` done**: `claude-desktop` authenticates as itself, holds the `agent` role and carries a hardcoded `agent_id` claim, which is exactly what `CommercePrincipal` reads to tell an agent from a person. **Token Exchange is not**, and it belongs beside the mandates in phase 11: acting on behalf of somebody is only meaningful when there is something authorising the scope
- [x] `P7-10` ADR 0017 — **S** · the decision AND the evidence, which arrived a phase apart

**Risks.** The realm file is the whole "clone and run" promise; if it drifts from
what the code expects, a fresh clone breaks in a way that is tedious to diagnose.
Export it from a running instance and commit it, do not hand-write it. Second
risk: the seeded identities must match between the dev issuer and the realm, or
every test fixture forks in two.

**Demo.** The swap itself: same login screen, same policies, same tests — one
configuration value pointing at a different issuer. Then the audit row naming who
published what, with a trace id.

**Evidences.** Treating an identity provider as a replaceable adapter with a
contract suite; cross-cutting concerns as dispatcher decorators; guest-to-account
merge as a domain operation.

**Article.** *"I faked the identity provider, not the authentication"* — why the
fake was the issuer and never the token validation, and what the swap cost.
`#dotnet #Keycloak #OIDC #architecture #ecommerce`

---

## Phase 8 · Embeddings, hybrid search, evals

`hecha` (2026-09-15, on `feature/phase-8-hybrid-search`; the default since 2026-09-17) · priority **high** · size **L** · **hybrid reranked en 0.843 / es 0.831 against lexical 0.748 / 0.692 in three consecutive gate runs — the default, degradable, and every page says what kind of answer it is**

- [x] `P8-1` Qdrant and Text Embeddings Inference in the AppHost, each model pinned by revision — **S** · **on the GPU when the machine has one** (2026-09-15): the AppHost reads the compute capability and the Docker runtime and picks TEI's image for that architecture, or the CPU image sized to the catalogue — twenty product texts in 0.375 s and a query in 16–17 ms, with 1.3 GB of VRAM and 1.06 GiB of RAM on the GPU, 3.70 GiB of RAM instead of 12.64 on the CPU · **declared always and started on demand** with `Tendero__Vectors=true`, because the embeddings container holds 12.7 GiB on a CPU; nothing waits for either, so a stopped vector layer is lexical search rather than a service that will not start (invariant 8). Qdrant through `Aspire.Hosting.Qdrant` 13.5.3 (MIT), which pins the image to v1.18.0 and `Qdrant.Client` to 1.18.1; the committed API key is refused-without and answered-with, verified live. TEI on the CPU image, serving exactly `5617a9f` as its info endpoint reports, with the weights in a named volume. Verified both ways on the real stack: flag on, both healthy and a 1024-dimension embedding; flag off, the API healthy and neither container started. **Not Ollama** (ADR 0031): its `bge-m3` names no source revision, and it has no official copy of the reranker and no rerank endpoint. The licence check that had to come first is done: `bge-m3@5617a9f` MIT, `bge-reranker-v2-m3@953dc6f` Apache-2.0
- [x] `P8-2` `bge-m3` via `Microsoft.Extensions.AI`; `EmbeddingModelDescriptor` — **M** · through **`Microsoft.Extensions.AI.OpenAI` 10.10.0** against TEI's OpenAI-compatible route, decided rather than defaulted: the MEAI pipeline brings `UseOpenTelemetry`, so an embedding is a GenAI span, and the same adapter serves the generative models later. **"OpenAI-compatible" is a surface, not a contract**, so a test captures the request the SDK really sends and holds it to the five fields TEI 1.9.3 declares — no Docker needed. The descriptor refuses anything but a full commit SHA and names the collection (`products_bge-m3_5617a9f_1024`); the generator is registered only when the AppHost started a model server. The OpenAI SDK is fenced inside `Search.Embeddings` by an architecture rule. Against the real TEI — opt-in with `TENDERO_MODEL_TESTS=1`, because 12.7 GiB is more than a CI runner has — the served revision is the pinned one and Spanish sits closer to English than to another product. Eight mutations, none surviving
- [x] `P8-3` Collection name carries model and dimensions; payload carries `modelId`; startup mismatch marks the layer unavailable — **M** · a mixed vector space is refused **three times**: the collection is named after model, revision and dimensions; `QdrantVectorLayer` checks on demand — remembered a minute, ten seconds when the answer is no — that the model server serves the pinned revision and the collection holds 1,024-dimension cosine vectors, and a "no" makes the layer unavailable rather than throwing; and every point carries the model that wrote it, asserted on read. A process started without the vector layer gets `NoVectorLayer`, a named stand-in, so a fresh clone composes
- [x] `P8-4` `ProjectProductToVectors` on the existing outbox; `reindex?target=` — **M** · one point per (product, culture), **not per variant**: variants share the embedded words and would take eight nearest-neighbour slots with copies of themselves. **The vector projection never fails a message**, because the outbox retries a whole message when one handler throws and a vector handler that threw would dead-letter the lexical projection — a failure is a warning and `tendero.search.vector_projection.failed` instead. An unchanged text is not embedded again (a fingerprint of text and model in the payload). `target=lexical|vector|all`, lexical by default so the endpoint means what it meant; `RebuildEmptyVectorIndex` waits for the layer in the worker's background. Seven integration tests against `qdrant/qdrant:v1.18.0`
- [x] `P8-5` `ISearchStrategy` keyed; RRF fusion (pure) — **M** · `Qdrant.Client` **1.18.1**, not 1.19.0: `Aspire.Hosting.Qdrant` pins it, and a newer client in a project the AppHost references is a downgrade `-warnaserror` fails on (ADR 0006) · fused **by position, never by score**, k = 60, ties to the words somebody typed. Products meaning found and words did not get their card from the lexical index (`ISearchHitReader`), so a hybrid hit cannot show a price or a stock level the lexical one would not, and a vector left behind for an archived product finds no card and is dropped. The storefront, the MCP tools and UCP all dispatch the one query, so one line decides for all three
- [x] `P8-6` `bge-reranker-v2-m3` behind `IReranker`, with a timeout budget — **M** · **a GPU feature**: 9.8–12.3 s for twenty candidates on a CPU, measured 2026-09-14 · through TEI's `/rerank`, the served revision checked once, 800 ms budget over the first twenty. The AppHost starts it with the vector layer **only on a GPU**. **Measured 2026-09-15, it makes hybrid worse** (en 0.944 → 0.912) — kept off, and its flag's removal condition names the outcome in which it goes with it
- [x] `P8-7` Staged degradation + `search.degraded` tag and counter — **S** · each AI stage fails towards the one before it: vector down or past its 1.5 s → lexical's own answer; reranker down or past its 800 ms → the fused order. Both tag the span with the stage and count `tendero.search.degraded`. A browse and a date order never reach the vector side
- [x] `P8-8` `HybridDegradesToLexical` test — byte-identical results with a throwing vector fake — **S** · compared **serialised, on page two**, so re-paging the candidates would not pass
- [x] `P8-9` Feature flags with removal conditions; the enum-coverage test; ADR 0023 — **S** · **two flags, not five**: `SearchMultimodal`, `UcpTransactional` and `Copilot` would each guard code that does not exist, which is article 07's objection. Every member carries `[RemovalCondition]` and is read in exactly one place in `src/`, and an architecture rule holds both. The AppHost writes each flag with the switch that starts what it guards
- [x] `P8-10` Hybrid thresholds committed; lexical must still clear its own — **S** · lexical's own is 0.91/0.92 (es) and 0.92/0.94 (en) since 2026-09-11, after an OR fallback — the gap hybrid was meant to earn turned out to be mostly an `AND` · **measured 2026-09-15 on the GPU, and hybrid does not beat lexical in Spanish**: en 0.935 → 0.944, es 0.927 → 0.912, identical across two runs; `regalo para casa` 0.305 → 0.355. Hybrid's thresholds sit ~0.013 under the measurement (es 0.90/0.93, en 0.93/0.94). **One tuning experiment, declared before it ran**, `VectorDepth` 20 → 10: refuted, es fell to 0.891 — and the search stopped there, because trying values against 22 queries per culture is fitting the golden set. `SearchEval --strategy hybrid` scores both strategies over one corpus and fails if either misses its own bar; CI runs lexical, since a runner has no model server · **re-measured the same day with a fair ruler**: the golden set had been pooled from lexical alone (`judged@10` 1.000 lexical, 0.84 hybrid, 0.39 vector), the 282 unjudged first-ten results were judged and applied under the author's criteria (450 → 732 judgments), and the result is hybrid en 0.944 against lexical 0.921, es 0.916 against 0.921. Thresholds re-derived: lexical es 0.90/0.91, en 0.90/0.90; hybrid es 0.90/0.92, en 0.93/0.91
- [x] `P8-11` Backoffice search-evaluation panel, two columns with the delta — **M** · the gate's measurement **moved into `src/Search/Evaluation`** with the golden sets embedded from where they live, so the CI gate and `GET /api/search/evaluation` cannot disagree about the metric. Both strategies are asked whatever the flag says, because the flag decides what the storefront gets and not what a shopkeeper may measure. **It measures on a button, never on opening** — 44 queries twice take seconds — the queries hybrid lost on come first, and two identical columns are explained with the vector layer's own reason instead of being left to read as "meaning adds nothing". Six mutations of the page, none surviving — one did at first, because a floating-point `5.5e-17` printed as `0,000` and still painted green
- [x] `P8-12` **What the shop says when it has nothing, and the flags closed** (2026-09-17) — **L** · not on the original list: the open question `P8-10` left. **Asked how real shops do it rather than tried a sixth threshold**, and the answer was a query-understanding step beside retrieval (ADR 0032). Layer A, no model: `Exact` / `Related` from a per-word full match over what a product is, and a help card for returns — storefront notice, `answer` over MCP, `messages` over UCP. Layer B, scope by anchors, **refuted** on `scope-dev` (9 / 5 / 1 / 1 in-scope queries marked out at 0.02 / 0.05 / 0.08 / 0.12) and, **later the same day and by the author's decision, taken out of the shipped code and out of the contract** — the answer has two values, `outOfScopeLabel` and UCP's `not_sold` are gone, and the classifier, the anchor file, the protocol's rules and the 40 scope query pairs per culture are `tools/SearchEval/QueryScope` with `--suite scope` still running the whole thing (ADR 0032, amended). It could never have registered — no calibration file, so the branch was unreachable, the wire carried a value nothing could produce, and eleven mutation survivors had no test that could kill them. **Search's Stryker baseline is therefore re-derived at the next phase close**, since the project it was measured on lost a folder. `SearchRerank` became a GPU capability; `SearchVector` met its condition in the third gate run and is deleted. Every no-answer query admitted (1.00), notice rate 0.481 (en) / 0.462 (es) — 0.538 / 0.519 until the full-match check learned to ignore a bare measurement token ("24 cm" in the catalogue, "24cm" from a shopper), which silenced six notices on pages answered at 0.634 or better and moved no NDCG, since ranking is untouched. **Where the notice lands is measured per query kind** (`docs/blog/reports/2026-09-17-no-answer-research/notice-by-kind.md`, read in `notice-by-kind-reading.md`, rerun in `notice-by-kind-after.md`): every use-case and symptom query gets it, and there it is the most honest sentence on the page. **Phase close:** Stryker of covered 76.23 % for `src/Search` (baseline was 75.97 %) and a first 69.34 % for `src/Ucp`, every survivor classified in `docs/testing.md`

**Risks.** The vocabulary-gap queries in the golden set are the ones hybrid has
to earn; if it does not move them, publish that too. Second risk: the first
run pulls gigabytes — 2.2 GB per model — and on a CPU the two TEI containers held
12.7 GiB and 5.3 GiB of RAM; document it in the README prerequisites as the
Elasticsearch heap already is.

**Demo.** Split screen, same query: lexical left, hybrid right, NDCG delta below.
`reading light with usb` finally returns something.

**Evidences.** Hybrid retrieval built correctly; **proven** graceful degradation;
AI quality as a CI gate; making a mixed vector space structurally impossible.

**Article.** *"Hybrid search in .NET, and the test that proves it degrades"* —
the degradation test is the part nobody writes. `#RAG #search #dotnet #AIEngineering`

---

## Phase 9 · Read-only MCP and the UCP manifest

`hecha` (2026-09-08) · priority **critical** · size **M** — the reputational phase · **619 tests, and the manifest is served rather than written**

- [x] `P9-1` `src/Mcp` project; `MapMcp()` in the API process — **M** · `ModelContextProtocol.AspNetCore` 2.2.0, Apache-2.0 declared as an EXPRESSION in the nuspec and pinning nothing of ours — the two checks ADR 0006 exists for. It targets net10.0 and not net11.0, which is fine and is written down rather than rediscovered. The route is `AllowAnonymous` **by explicit decision** and out of the OpenAPI document, because that document is the REST API's shape and MCP is JSON-RPC over one route. Three tests, and the mutation matters more than the count: deleting the `MapTenderoMcp()` call turns all three red, and removing the `AllowAnonymous` turns the endpoint-policy test red naming `/mcp/`. Verified outside the test server too — real Kestrel, real HTTP, `initialize` answering `serverInfo.name = tendero` on an SSE frame
- [x] `P9-2` Five read tools over `IQueryDispatcher`; flat ids on the wire — **M** · `search_products`, `get_product`, `list_categories`, `list_offers`, `get_return_policy` — and **two of the roadmap's five are not among them**. `get_product_specs` and `compare_products` answer from approved claims and `Knowledge` is phase 10; a comparison with no claims cannot say "sin dato" with any authority, which is its whole argument. Same call phase 6 made when `apply_filter` was dropped for being a tool over facets that did not exist. What replaced them exists and an agent needs it: what is on offer, and how long the shopper has to send it back. **Flat ids cost nothing**, which is the finding — every query result already carries `string ProductId`, so the tools return them unmapped and a DTO of their own would have been a second contract to keep in step. `get_return_policy` did cost something: the endpoint answered from the aggregate's constant with no handler, deliberately, and a second transport that may not touch `*.Domain` turned "one line or a round trip" into "one source or two copies" — so it has a query now and the comment says why it changed. **Culture goes through `CultureNegotiation.Resolve` and it did not at first**: the tools handed the caller's raw string to the query, so `EN-GB` would have reached a handler as `EN-GB`. Five theory rows, three of them red under mutation
- [x] `P9-3` Architecture rule: `Mcp` implements no handler and touches no `*.Domain` — **S** · written over a `Transports` array rather than over `Mcp`, so `Ucp` joins it in `P9-5` by being added to a list instead of by somebody remembering. The forbidden namespaces are **computed** from the contexts and asserted non-empty, like every other rule in that file — a seventh context is inside the rule the day it exists, and a broken discovery cannot pass vacuously. Verified by mutation: an `IQueryHandler` declared inside `src/Mcp` that reads `ReturnRequest.Window` turns **both** rules red and both name the type
- [x] `P9-4` Culture over MCP: tool parameter → **the request's own `Accept-Language`** → `es`; `missingCultures` as data; ADR 0013 amendment — **S** · the session default is **not built, because the premise was wrong**: the roadmap said there is no `Accept-Language` over MCP, which is true of stdio and false of the streamable HTTP transport actually chosen. The tools call `CultureNegotiation.Resolve` with the same two arguments an endpoint passes it, so this is the same chain rather than an analogue — and inventing a session field would have been a protocol extension where a standard header already sits. `Content-Language` becomes a field (`Answered<T>.Culture`) because JSON-RPC has no headers; `Vary` has no analogue and is not needed. **`missingCultures` needed nothing**: `ProductDetail` already carries it. One defect found writing it: a `culture` parameter defaulting to `"es"` makes "said nothing" and "asked for Spanish" the same value, and the rest of the chain is dead code — it defaults to `null` now. Verified at both levels, and the transport half could not be assumed: a test sends `Accept-Language` on the JSON-RPC POST and reads the culture back out of the envelope
- [x] `P9-5` `src/Ucp` becomes a real project — **S** · and it becomes one by holding the thing that must not be invented: **the document's shape, read from the specification** at `ucp.dev/2026-04-08` rather than guessed. `{ ucp: { version, services, capabilities } }`, services keyed by reverse-DNS and holding one entry per transport binding, capabilities keyed by their own id. **`payment_handlers` and `signing_keys` are absent rather than empty**: a shop publishing an empty payment-handler map advertises a checkout it cannot honour. The version is a pinned constant and **no conformance is claimed** — what is asserted is the shape, for the capabilities we serve. `Ucp` joined `Solution.Transports` in the same commit, so `P9-3`'s two rules constrain it from its first line rather than from whenever somebody remembers. One thing checking bought: the roadmap's `dev.ucp.identity` is really **`dev.ucp.common.identity_linking`**, while its two guessed catalogue ids were exact. The guard that matters is not the four assertions about today's field names — every member is one word, so camelCase and the spec agree by accident — it is the reflection test that **every member carries an explicit `[JsonPropertyName]`**, because the accident ends at phase 11's `payment_handlers`. Verified by mutation, and it names the member
- [x] `P9-6` `IUcpCapability` + registry by assembly scanning; capabilities name their slices — **M** · **two capabilities and not seven**, which is the decision rather than the amount of work: every slice behind cart, discount, fulfillment and checkout exists and has since phase 5, and what does not exist is any way for an agent to be AUTHORISED — its own principal and a signed mandate are phase 11. Declaring `dev.ucp.shopping.checkout` today would advertise a checkout no agent can reach, which is the same rule that keeps `payment_handlers` out of the profile. A theory names the six absent ids so phase 11 is a deliberate act. A capability holds `typeof(SearchProductsQuery)` and not a string, so **deleting a slice is a build error** — verified by mutation, `CS0246`. And a test asserts every named type is really an `IQuery<>` or `ICommand<>`, because naming a plain record would compile and would advertise a verb with nothing behind it — also verified, and it names the offender. Discovery by scanning even though every implementation is in one assembly: adding a capability is adding a class, and forgetting to register one is not a failure mode that exists. Ordered by id, because a snapshot over a manifest whose keys move is a coin toss
- [x] `P9-10` **The UCP catalogue MCP binding** — **M** · numbered last and ordered HERE, because renumbering breaks the references the roadmap and the code already carry. It exists because composing the profile meant reading the binding, and the binding says the transport must expose `search_catalog`, `lookup_catalog` and `get_product` with ITS shapes — `meta` carrying `ucp-agent.profile`, a `ucp` envelope, a `products` array of UCP Products, money in **minor units**. Ours were `search_products`/`get_product` answering `SearchResultPage`. **Our `/mcp` was not UCP's MCP binding**, so declaring `dev.ucp.shopping` in the profile would have been a lie in the one document whose whole value is being trustworthy. Both tool sets live on one server because `MapMcp` offers one per process, and **the name collision was resolved in the protocol's favour**: our `get_product` is `get_product_detail` now. Eight tools. The mapper reads `ProductDetail` and not a search hit, because UCP requires a description and a hit does not carry one — an N+1 **bounded by the specification's own default page size of ten**, recorded in the backlog with that number rather than fixed by widening this shop's search contract for somebody else's protocol. Verified over real HTTP: eight tools listed, a call with no `ucp-agent.profile` refused, a 60-identifier lookup refused by name and number
- [x] `P9-7` `/.well-known/ucp` derived from the registry; snapshot — **M** · composed from `UcpCapabilities.Declared` on every request, so it cannot describe a shop this process is not, and anonymous **because the specification requires it** rather than because we chose it. **Verify was declined**, and the reason is Respawn's: `docs/openapi/tendero.json` is already a committed snapshot compared by a test with `UPDATE_OPENAPI=1` to regenerate, so the second derived document uses the same mechanism (`UPDATE_UCP=1`) instead of a second workflow to explain. **Reading the specification's own JSON Schemas corrected three things reasoning had got wrong**: the signing-key field is `keys` (a JWK Set) and not `signing_keys`; a capability's registry value is an **array**, the same shape as a service's; and a business profile **requires** `payment_handlers`, so "absent rather than empty" — written into this file, the README and a commit message — made the document invalid. An empty map does not advertise a checkout, it says this shop settles no payments over UCP. The capability `schema` is the protocol's canonical URL (`ucp.dev/schemas/shopping/catalog_search.json`), found in the spec's repository rather than guessed. Served over real Kestrel with no token, `endpoint` derived from the request host
- [x] `P9-8` `docs/mcp.md` with the Claude Desktop / VS Code config — **S** · three clients, and each one's instructions were **read from its own documentation** rather than remembered: Claude Code takes `claude mcp add --transport http` (confirmed from `--help` on this machine, which also gave the `-H "Accept-Language: en"` that makes `P9-4`'s culture chain reachable from a client); VS Code takes `.vscode/mcp.json` with `servers` / `type: http`; and **Claude Desktop's remote path is not a config file at all** — it is Settings → Connectors → Add custom connector, so the `claude_desktop_config.json` most people would reach for is the local-server shape and does not apply. The caveat is written in rather than left for a reader to hit: a custom connector expects an internet-hosted `https` server and may refuse a plain-http localhost URL, and **neither client path was exercised here** — what was is `curl` against Kestrel, which is why the page leads with it. Both documented refusal snippets were run verbatim against a running API before the page was committed
- [x] `P9-9` ADR 0020, ADR 0021 — **S** · **0020** records what the two architecture rules protect and what the rule cost exactly once — the returns policy endpoint, where a second transport turned "one line or a round trip" into "one source or two copies"; it also says what a transport still owns, because the rule is *may not answer a question of its own*, not *may add no shapes*. **0021** records derivation, and it is mostly a list of things a manifest may not do: advertise what nobody can reach (six capability ids named so phase 11 is deliberate), claim a binding it does not serve (which is why `P9-10` exists), and guess a shape (three corrections from the spec's own JSON Schemas). Verify's refusal is written down there with Respawn's reasoning. The ADR index's note about the reserved gap now names which of 0018-0023 are still unwritten

- [x] `P9-11` **Conformance, measured against the specification's own schemas** — **M** · 116 schema files vendored at a pinned commit (`3b114a1`, 2026-09-08) so validation is offline and cannot change verdict under us. Six defects found, all of them invisible to prose and all of them the kind an agent hits on its first real call. The negative controls matter as much as the positives: the FIRST run reported the profile valid **and** a profile with no `payment_handlers` valid, because `SchemaRegistry.Get` resolves a document and ignores the fragment — the validation was passing over `profile.json`'s root, which requires nothing. Three levels now: the shapes (Ucp.Tests, no infrastructure), the MCP transport envelope including `structuredContent` (Api.Tests, in process), and **every shipped product in both cultures against a real Postgres** (Integration.Tests). 649 tests

- [x] `P9-12` **UCP's REST binding**, the second transport of the same capability — **M** · three POSTs at the specification's own paths under a `/ucp` prefix that is the only part of them we chose, and the profile now declares TWO entries for `dev.ucp.shopping` — which is why a service is a list and not an object. **The semantics moved to one place first**: `UcpCatalogOperations`, called by both transports, so paging, culture and the lookup loop cannot drift into two implementations of one protocol. A transport now only marshals arguments and spells a refusal. REST is the binding where **both directions** have a schema, so requests are validated too — the one thing MCP cannot give. The status codes fall out of the schemas rather than out of taste: a lookup that finds nothing is **200** with a `messages` entry, because an unknown id is a business outcome; `product` is 404, because its response schema requires a `product` and there is no valid 200 shape for a miss. **Verified on the full Aspire stack** with the hundred products imported, published and indexed: search over Elasticsearch, the cursor actually paging, `inputs` correlation, both miss behaviours, `Accept-Language` honoured, minor units — and every live response validated against UCP's own schemas, profile and MCP envelope included

**Risks.** UCP is young; pin the capability version strings and treat the schema
URIs as versioned. Do not claim conformance you have not tested.

**Demo.** Claude connected to Tendero, asked in English for a navy backpack under
40 € and what it is made of — answered from `search_products` and
`get_product_specs`. Record this one.

**Evidences.** Implementing a protocol from a specification; a manifest derived
from code rather than hand-maintained; reusing an application layer across
transports without duplicating it.

**Article.** *"A UCP manifest that cannot lie"* — derivation from a capability
registry, so deleting a slice breaks the build.
`#MCP #UCP #agenticcommerce #dotnet #ecommerce`

---

## Phase 10 · Knowledge: claims with provenance

`no empezada` · priority **high** · size **L**

- [ ] `P10-1` `src/Knowledge`; `SourceDocument` content-addressed like `ImageId` — **M**
- [ ] `P10-2` `ProductClaim` with evidence, confidence, provenance (model, revision, prompt version, run) — **M**
- [ ] `P10-3` `IClaimExtractor` via MEAI structured output — **M**
- [ ] `P10-4` `ProposeClaimsFromDescription` — no new sources, no scraping — **M**
- [ ] `P10-5` Review queue for claims, reusing the existing pattern — **M**
- [ ] `P10-6` `ClaimApproved` → outbox → Catalog `SetAttribute` and Search `specsText`. Knowledge never writes to Catalog — **M**
- [ ] `P10-7` `CompareProducts`: approved claims only; "sin dato", never a guess — **M**
- [ ] `P10-8` PDP specs table with source chips and confidence — **M**
- [ ] `P10-9` `claims` eval suite — **M**
- [ ] `P10-10` ADR 0018 — **S**

**Risks.** Confidence is easy to display and hard to calibrate — say plainly in
the article that a model's self-reported confidence is not a probability. Second
risk: source documents are user-facing text from an external origin, so treat
extracted content as data, never as instructions.

**Demo.** A specs table where every row carries a source chip; hover shows the
exact sentence. Reject one claim in the backoffice and watch it leave the PDP.

**Evidences.** Provenance and confidence modelled structurally; human-in-the-loop
where approval has a real effect; refusing to let a model write to the aggregate.

**Article.** *"The AI writes claims, not products"* — the one-sentence rule that
keeps an aggregate from rotting. `#AIEngineering #RAG #DDD #ecommerce`

---

## Phase 11 · UCP transactional, AP2 and Know Your Agent

`no empezada` · priority **critical** · size **XL** — **the headline**

- [ ] `P11-1` Cart, discount, fulfillment, checkout and orders capabilities in the manifest — **L**
- [ ] `P11-2` `AgentPrincipal` in Accounts; `CommercePrincipal(Customer, Agent?, Mandate?)` — **M**
- [ ] `P11-3` Agent tokens: `client_credentials`, and Token Exchange for acting on behalf — **M**
- [ ] `P11-4` `IMandateVerifier` keyed by format version; `fake` self-signed JWS as default — **M**
- [ ] `P11-5` `MandateScope` snapshotted onto the order, never re-resolved — **S**
- [ ] `P11-6` `IAgentPolicy` decorator; three declarative rules; denials recorded — **M**
- [ ] `P11-7` Agent activity panel over the audit table — **M**
- [ ] `P11-8` `com.elguerre.tendero.knowledge` extension capability — **S**
- [ ] `P11-9` ADR 0022 — **S**
- [ ] `P11-10` README leads with the reference-implementation claim; talk proposal drafted — **S**

**Risks.** AP2's wire format is firming through H2 2026 into early 2027 — that is
exactly why the verifier is keyed by format version. State the draft you
implemented and its date, in the code and in the article.

**Demo.** The activity panel: an agent searched four times, added two items,
attempted a 340 € checkout, **denied** — *"the mandate authorises up to 200 €"* —
with the mandate scope rendered beside it. **The denied row is the demo.**

**Evidences.** Implementing an emerging payment-authorization protocol; agent
identity distinct from customer identity; policy as a declarative, auditable
table.

**Article.** *"A UCP merchant server in .NET"* — the missing reference
implementation. **Treat as a launch**: README, LinkedIn, X, dev.to, a talk
proposal, and a note to the UCP community. `#UCP #AP2 #agenticcommerce #dotnet #ecommerce`

---

## Phase 12 · Copilot and AI observability

`no empezada` · priority **medium** · size **L**

- [ ] `P12-1` Grafana + Tempo + Prometheus in the AppHost (AGPLv3 is fine for a service we run unmodified — see the two-tier licensing policy) — **S**
- [ ] `P12-2` MEAI delegating client: OTel GenAI span + `telemetry.AiInvocations` row — **M**
- [ ] `P12-3` `src/Assist`; Microsoft Agent Framework, five read-only tools — **L**
- [ ] `P12-4` `ActionProposal` with a registered command type and payload — **M**
- [ ] `P12-5` Approval deserializes and dispatches through `ICommandDispatcher` — **M**
- [ ] `P12-6` `copilot` eval suite: assert the proposed command, no judge — **M**
- [ ] `P12-7` AI observability panel: cost per conversation, p50/p95, tokens per model, degradation counter — **M**
- [ ] `P12-8` ADR 0019 — **S**

**Risks.** Prompt injection through catalogue and claim text — the copilot reads
data written by importers and models. Constrain it to typed read tools over
curated tables, never raw traces, and never free SQL.

**Demo.** *"12 products have no English name — shall I translate them?"* → a table
of proposals → Approve → twelve commands dispatch → the audit log shows who
approved what. Plus the cost line: *"this conversation: 3 calls, 1,842 tokens,
€0.0021"*.

**Evidences.** Drawing the AI/deterministic boundary; making an agent's action
vocabulary a closed set; treating cost and latency as product features.

**Article.** *"My copilot doesn't write text, it writes commands"* — why the
answer ends in a button. `#AIEngineering #agents #dotnet #ecommerce`

---

## Phase 13 · Multimodal and the kit solver

`no empezada` · priority **medium** · size **L**

- [ ] `P13-1` Tokenizer-fidelity fixture test **before** committing to in-process ONNX — **S**
- [ ] `P13-2` SigLIP 2 via ONNX Runtime; `IImageEmbeddingGenerator` — **L**
- [ ] `P13-3` Image collection keyed on `ImageId`; dedup for free — **M**
- [ ] `P13-4` `POST /api/search/by-image`; then text→image — **M**
- [ ] `P13-5` `IKitSolver`, pure: branch-and-bound with a solver trace — **M**
- [ ] `P13-6` CsCheck properties: budget never exceeded, order-stable, monotone in candidates — **M**
- [ ] `P13-7` Explanation generated from the trace; a model may phrase, never add facts — **M**
- [ ] `P13-8` Kit form first; natural-language parsing second — **M**

**Risks.** Model weights need the same per-version licence check as NuGet
packages — extend the policy before adding the first one. SigLIP and SigLIP 2
spaces are not interchangeable; the collection-naming rule from P8-3 already
prevents mixing them.

**Demo.** Drop a photo of a pan into the search box and get the pan. Then a kit:
*"Kitchen kit · €118.40 of €120"* with *"I chose the 24 cm pan over the 28 cm
because €11.50 remained and the 28 costs €14.90."*

**Evidences.** In-process model inference in .NET without a Python sidecar; a
deterministic solver behind a conversational surface; explanations derived from a
trace rather than generated.

**Article.** *"A recommendation that shows its arithmetic"* — the model parses and
phrases, the solver decides. `#AIEngineering #ecommerce #dotnet`

---

## Standing backlog — deferred, with the number

Not scheduled. Each is recorded so the deferral is a decision rather than an
oversight, and each is a paragraph in some future article.

| Item | Measured today | Enters when |
|---|---|---|
| Outbox coalescing | 47 rows for 6 products (~8 events each); ~1.2M for the full 147k ABO set | A real import, or the outbox lag metric turns bad |
| Bulk indexing | one HTTP call per product per culture | Same |
| Reindex as a background job | fine for 6 products; the wrong shape for 147k | Same |
| Image derivatives | six 640px images — nothing to measure | A deployment target exists |
| Orphan index documents | 6 hard-deleted rows left 12 documents; running SearchEval against the stack's own Elasticsearch left 6 more, because the gate mints fresh product ids per run and the sweep filters by `productId` | Something other than `psql` produces the case. Meanwhile: **do not point the gate at a running stack's engine** |
| `SKIP LOCKED`, exponential backoff | one replica | A second replica |
| **The outbox on a library instead of our own worker** — Hangfire, or an alternative worth weighing: Wolverine, DotNetCore.CAP, Rebus, TickerQ, Quartz.NET | recorded 2026-09-16 at the author's request. Today the outbox is hand-written (`TenderoDbContext` drains events into `outbox.OutboxMessages` in the same transaction; `OutboxProcessor` polls every 2 s, batches 50, stops at 5 attempts, records `tendero.outbox.lag`), and the worker has its own tests since 2026-09-10. **The bar is the author's**: a change goes in only if it adds a differential value to the project, or it shows something a reader who is a developer can learn from — never just to swap working code for a dependency. What to compare, and write down, before choosing: **the licence of the exact version** (Tier 1, because it is linked — Hangfire's core is LGPL-3.0 with paid Pro features, which the permissive-only rule does not allow as it stands, so check it and say so; MassTransit v9+ is banned already); whether it keeps the event and the state change **in one transaction** (the whole argument of the outbox) or only schedules jobs, which is not an outbox; retries, dead letters and a dashboard against the dead-letter view the roadmap already plans; what it needs in Postgres; and what the architecture rules, the three suites that drive the real worker, and `docs/testing.md` would have to change. A good outcome can also be **"we keep ours"**, with the comparison as the article — why a hand-written outbox (a 119-line worker plus the drain in `SaveChanges`) is enough at this scale, and when a library starts to pay | A comparison worth an article, or the first need the hand-written worker cannot meet — a second replica, scheduled or delayed jobs (the reservation expiry sweep below), or a real message broker. Needs an ADR, and the author's say-so before any package is added |
| Full ABO import (147k) | not attempted | It is the trigger for the four rows above |
| Shopify connector | one connector today, and the port is proven | A second source is genuinely wanted |
| Multi-currency | one currency per order, enforced | A second market. **It brings UCP's minor units with it**: `UcpCatalogMapper.Money` multiplies by 100, which is right for the euro and wrong for the yen and the dinar |
| `search_catalog`'s N+1 | ten lookups per search, over a hundred products, because UCP requires a description on every Product and a search hit carries none | The number is worth fixing, or a description is projected onto the search document. Widening this shop's own search contract to satisfy somebody else's protocol is the move that was declined |
| Promotions and tariffs in Postgres | 2 lists, 7 promotions, both in committed files | The backoffice can edit one. Building the table first is what phase 2 already did and undid |
| Reservation expiry sweep | `Reservation.Lifetime` is 15 min and `HasExpiredAt` exists; nothing sweeps. Carts exist now and expire in 7 days, and `ix_carts_status_expires_at` is already the index the sweep would read | A scheduled worker. Checkout authorises before it places (ADR 0025), so a declined card leaves nothing behind — which is what took the urgency out of this |
| `stripe-mock` as a second payment adapter | one adapter, one contract suite; the same shape `SeedCatalogConnector` and `dev-issuer` ship in | It needs `Stripe.net` (or hand-rolled form-encoded HTTP) and a container. A dependency decision, and the port is proven without it |
| ~~**CUDA inside Docker on this laptop**~~ | **Closed 2026-09-15, and it was not this laptop** — a TEI entrypoint bug on NVIDIA drivers 6xx (upstream #870), fixed by pinning TEI to commit `4150561`; ADR 0031, amended. Recorded 2026-09-14. TEI's Blackwell image (`120-1.9.3`) runs `nvidia-smi` against the RTX 5070 (driver 610.88, `compute,utility`, `/dev/dxg` present) and still fails CUDA initialisation with `CUDA_ERROR_NO_DEVICE`; `/usr/lib/wsl/lib` is not mounted in the container. TEI then serves on the CPU with a warning and nothing else | `P8-6`, which cannot meet a budget without it. The in-process ONNX Runtime path `tools/SeedImages` already takes on this GPU is the fallback (ADR 0031) |
| **An ONNX export of the reranker** | asked 2026-09-14, not measured. `bge-reranker-v2-m3` has no ONNX at revision `953dc6f`, so TEI ran it on candle: **9.8–12.3 s for twenty pairs** on the CPU. The same TEI image ran `bge-m3` — the same XLM-RoBERTa size — through ONNX Runtime at **~1.5 s for twenty texts**, which is the evidence that an export could make the reranker viable WITHOUT a GPU, and INT8 quantisation could cut it further. It would also unblock the in-process fallback ADR 0031 names. What it costs: the export is a Python step (or Hugging Face's `sentence-transformers/backend-export` Space), the ONNX is a **derived artefact** whose provenance has to be recorded (source revision, exporter and its version) and checked for **numerical equivalence** against the spike's own reference — logits −6.450 / −6.648 / −10.626 / −10.962 and 0.9958 for the precise query — and a quantised model moves scores, so the NDCG gate decides, not a feeling | The start of `P8-6`, measured beside the GPU path: FP32 and INT8 latency for twenty candidates, and the logit check, before choosing · **updated 2026-09-16**: the reranker earns its place on a GPU (hybrid on `holdout` 0.795 / 0.794 → 0.837 / 0.856), so the CPU question is now worth asking. A smaller model through TEI's candle backend does not answer it — `gte-multilingual-reranker-base` took 35 s for twenty pairs — and the options left were ONNX Runtime ones · **measured 2026-09-16**: the `gte-multilingual-reranker-base` INT8 export on TEI's ORT backend reranks twenty pairs in ~1–1.5 s and ten in ~0.5–0.7 s, and at the real 800 ms budget over a head of ten scores hybrid en 0.809 / es 0.788 against 0.810 / 0.783 without a reranker — not worth it, and unconstrained it adds only 0.005 / 0.009 against the GPU reranker's 0.033 / 0.048. What remains untried is `bge-reranker-v2-m3` itself as INT8 (571 MB, no declared licence or source revision, so the logit check would have to admit it), whose quality is the proven one and whose latency, by `bge-m3`'s own ONNX figures, is unlikely to fit. Research and every number in the 2026-09-16 notebook entries |
| **Recovering an order that left uncaptured** — asking for the payment again, or taking the goods back **without a refund** | recorded 2026-09-10. A capture that fails on shipping is left uncaptured (ADR 0025), and a `payment.failed` for such an order now logs a warning and changes nothing; the backoffice already shows shipped-and-uncaptured apart. That is detection, not recovery. **A return cannot do it**: it is the customer's to start, per line, its window opens on delivery, and its refund needs a capture that never happened. What is missing is new domain work — a second payment attempt against an existing order (a new authorisation and capture, which the port can already express), and a "return without refund" the SHOP opens, with its own reason and no money leg | The first real provider, or the first time that warning fires on something that is not a test. Order matters: asking for the money comes before asking for the goods |
| **New catalogue categories** (toys, air fresheners, plants, books, phones) — **optional** | recorded 2026-09-15 from the author's own query examples: "juguetes para niños de 6 años", "ambientador coche", "planta para la casa", "libro para aprender inglés", "móvil para mi madre de 70 años". Tendero sells none of these, so for now they enter the golden set as a separate **"not sold here" group** that measures whether search admits it has nothing rather than showing confident junk — the vector side always returns its nearest neighbours. Adding the categories would mean products, illustrations, attribute definitions and taxonomy, against the 100-product scale rule | The shop needs to be judged on those categories, or the "not sold here" group shows a failure only real products could fix |
| ~~**Stryker.NET, a one-project trial**~~ **Adopted 2026-09-16 by the author's decision**: `dotnet-stryker` 5.0.0 in the tool manifest, run at every phase close over the projects the phase touched, with a ratchet (`of covered` never below the project's baseline) and every survivor classified — `CLAUDE.md`, `docs/testing.md`, ADR 0006 | evaluated 2026-09-15 by reading, not installing: `dotnet-stryker` 5.0.0 is Apache-2.0 with no EULA and the dashboard off by default, but this repository tests on Microsoft.Testing.Platform (xunit.v3 4.0, MTP v2), Stryker's MTP runner is labelled preview, 5.0.0 under-reports kills nondeterministically above concurrency 1 (issue #3832), and its embedded Roslyn 5.9 predates C# 15. No maintained alternative exists. The trial: `src/Search` with `tests/Search.Tests`, project mode, `--concurrency 1 --skip-version-check`, judged against the hand-made mutation lists | **Trial done 2026-09-16**, tool removed afterwards: 983 mutants in 6 minutes, 32.73 %, identical in every unchanged file across two runs at concurrency 1, and six real gaps found and closed with tests. Adoption — the manifest, a CI job, a threshold, and filtering out telemetry-tag mutants — waits for the author's decision, #3832 and the MTP runner leaving preview |
| **NetArchTest.Rules → ArchUnitNET** | recorded 2026-09-18, at the author's suggestion, not yet evaluated line by line. `tests/Architecture.Tests` runs six ideas as roughly sixteen assertions on `NetArchTest.Rules` 1.3.2 — its last release was 2021-05-23, five years with no version bump and no claim of its own about .NET 11 or xUnit v3. `TngTech.ArchUnitNET` last released 0.13.3 on 2026-03-05, is Apache-2.0 (Tier 1, permissive), and ships a dedicated `TngTech.ArchUnitNET.xUnitV3` package — the currency question Stryker's preview MTP runner already put on this board, answered before it becomes a finding instead of after. What is unverified: whether its DSL expresses the one rule here that is genuinely dynamic — the forbidden-dependency list `ArchitectureRules.cs` computes by reflection from each assembly's own references, asserted non-empty so a clean context cannot pass vacuously — without a custom predicate; a documented case also says ArchUnitNET cannot express every rule NetArchTest can (matching a class to the generic argument of the interface it implements), which this repo's own rules do not need but is worth knowing before committing | The next testing-tooling pass, or the day `NetArchTest.Rules` actually breaks against a newer SDK rather than merely looking unmaintained. A small spike first: port the reflection-based forbidden-dependency rule and the slice-isolation rule; if either is awkward in ArchUnitNET's DSL, a licence-and-currency argument alone is not enough to redo all sixteen assertions |
| **Trained query classifier (Q2PT)** | recorded 2026-09-17 (ADR 0032). Today: 23 categories, a scope classifier by anchors that was measured and refuted — and kept as evaluation tooling for exactly this row — and no interaction logs. Shops at scale decide "we don't sell that" with a classifier trained from queries to product types — Amazon's Q2PT, Instacart's query understanding. To decide when it enters: **the label source** (ESCI mapped to the taxonomy, LLM distillation reviewed by a person, or our own logs), **ML.NET or a small head over the embeddings `bge-m3` already produces**, and **the licence of the exact version** of everything linked (Tier 1) | The taxonomy passes a few hundred categories, interaction logs exist, or layer B is refuted by a failure a trained model could fix. Layer B is the baseline it has to beat, and its exam is waiting: `tools/SearchEval/QueryScope` plus `--suite scope`, the same queries and the same rules |
| **A relative signal for "nothing here": how concentrated the results are** | recorded 2026-09-18, and **it needs its own research before it is tried**. The three refuted hypotheses (H1, H4, H5) and layer B all asked an ABSOLUTE number — a cosine, a reranker score, a distance to a category name — and an absolute number measures how much a query reads like a product, not whether the shop can answer it. The signal not yet tried is relative and comes from what retrieval already returned: if the first ten results sit in one category branch, something fits; if they scatter across unrelated branches, nothing does. It costs no model — `categoryCodes` is on every search document — and it is measurable with the harness that is already there. Before writing a line: read what the field calls this (result-set coherence, query performance prediction's post-retrieval predictors, e-commerce "null query" and low-confidence detection), because a name for it probably exists and somebody has measured its limits | Somebody wants to attack the open question again, and has an afternoon for research first. Declare the hypothesis, its values and its acceptance rule in the notebook before measuring, and use `tools/SearchEval/QueryScope` with `--suite scope` and NEW queries: `scope-dev` and `scope-test` have been looked at once now |
| **The blind design A-B, and article 22** | recorded 2026-09-18 (ADR 0033), **declared and not yet run**. The protocol is in the notebook with its acceptance rule fixed beforehand: redesign the backoffice search-evaluation panel on pen.dev's canvas from `P8-11`'s statement, the shape of `GET /api/search/evaluation`, `design/DESIGN.md` and `design/tokens.css`, **without opening the Angular component**, and score it on four criteria — tokens, structure out of three, one kept decision, cost. It needs the author at the machine: pen.dev's MCP server answers `A file needs to be open in the editor`, so nothing here runs unattended. Half of article 22's material is already complete and measured — the two orphaned mockups, the 575 hand-written hexes, the 2013 fidelity ladder — and the other half is this verdict, which is published whether it is accepted or refuted, the way layer B was. **In another repository, and the author's to do**: making *De 0 a N con Task[in]* public, so the book's quotations become citations. What blocks it, measured 2026-09-18: a broken `SUMMARY.md` link (`Task[in]-2-0.md` against `TaskIN-2-0.md` on disk), a `.gitbook.yaml` declaring a `README.md` that does not exist, five files outside the index — four of them pre-split monoliths duplicating whole chapters — a chapter-3 heading that disagrees with the index, and a stock software MIT licence applied to 127,000 words of prose | The author opens pen.dev and the blind arm can run. Nothing else waits on it: the chapter and the preface quote the old book inline rather than linking it, precisely so they work while it is private |
| **Six contrast failures, five of them in the light theme** | recorded 2026-09-19, found by laying the design system out on a canvas for the first time and **recomputed from `tokens.css` rather than taken from a report**. White on `--accent` **3.87:1** — that is the one clay action on every storefront view, at `--text-sm` 600, which WCAG does not count as large text, and `--accent-hover` `#B8451F` would carry it at 5.37:1. `--warning` on `--amber-50` **2.89:1** (the *Needs review* tag, the lowest in the system). `--accent` on `--accent-tint` **3.41:1** — the agent tag, load-bearing for the differentiator. `--positive` on `--olive-50` **4.06:1**. `--text-subtle` **3.32:1** on white and 2.94:1 on `--bg-page`, and **3.65:1** in dark: the same token failing in both themes. Plus dark `--danger` **2.56:1**, the one that started it. Not contrast, same audit: `[data-theme='dark']` redefines **12 of 15** semantic aliases and misses `--warning`, `--danger` and `--focus-ring`; the dark theme has no state tints, so a dark status tag must name a raw ramp that `tokens.css` forbids in components; the type scale is not the 1.25 ratio its comment claims; and `--shadow-pop` restates `--ink-900`'s channels by hand as `rgb(32 29 27 / 0.10)`, invisible to a hex-only regex. **Two repairs are already proposed** in `design/mockups/proposals.json` with measured ratios — `#E4655D` at 5.04:1 and `#DE8E16` at 6.37:1 — and `tokens.css` is untouched, because the flow is propose, approve, apply | The author approves a proposal. Nothing here is urgent enough to jump a phase, and nothing here is safe to leave undecided either: the first two are on the storefront's primary action and on the tag that marks what an agent did. An accessibility pass over the whole palette is the honest unit of work, not six one-line fixes |
| **Search evaluation at scale** | recorded 2026-09-17. 52 scored queries per culture, 1053 (es) and 1075 (en) hand judgments, 100 products. A hand-judged golden set stays a regression test past a few thousand products, not a measure of absolute quality: nobody can judge every product that could answer a query | The catalogue passes a few thousand products. Then interaction data (clicks, add-to-cart) or LLM judges validated against a sample of human judgments |
| **Backoffice evaluation panel shows the answer metrics** | `admitted` and the notice rate are in `SearchEval`'s report and in `CultureScore`, and left out of `GET /api/search/evaluation` on purpose when they were added (2026-09-17); false out-of-scope left with layer B the same day, since nothing can produce it | The notice-rate decision is taken, or the panel is next touched |
| **TEI's 32-text limit and `RerankDepth`** | TEI accepts at most 32 texts per request (`max_client_batch_size`), and `/rerank` refuses more. Found 2026-09-17 when the anchor build sent 133 and was refused; the anchors are batched now. `HybridSearchOptions.RerankDepth` defaults to 20 and has **no upper bound**, so a depth above 32 would be refused by the model server | Before anyone raises the depth: clamp it to 32 in validation, or chunk the pairs in the reranker |
| ~~**Layer B, the refuted scope classifier**~~ | **Decided 2026-09-17: neither deleted nor shipped.** The classifier, its anchor file, the protocol's selection and acceptance rules and the 40 scope query pairs per culture are `tools/SearchEval/QueryScope`, and `--suite scope` runs the protocol end to end; the shipped answer has two values and the contract lost `OutOfScope`, `outOfScopeLabel` and UCP's `not_sold`. The reason it could not stay: with no calibration file it could never register, so it was unreachable code, an impossible enum value on the wire and eleven mutation survivors nothing could kill. The experiment is in the history (*"feat(search): say what kind of answer a page is, and make hybrid the default"*) and in `docs/blog/reports/2026-09-17-no-answer-research/layer-b-evidence.md` | done |
| Cart lines as a table | jsonb, read only with the cart that owns them | Something queries a line on its own. UCP mutating one goes through the aggregate, so it is not that |
| Locale in the URL (`/es/…`) | one route serves two languages; the query is in the URL now, the language is still in a signal and in `localStorage` | The PDP route, which is where the per-culture slugs and `hreflang` both land (`P5-13`). The query already answers with every culture's slug |
| A slug table with uniqueness and redirects | **settled, not deferred** — ADR 0026 makes the slug decorative and the URL carries a code, so there is nothing to make unique and no history to keep | Never, unless the code leaves the URL |
| Spec Kit experiment | ADR 0009 reserves it for the UCP work | Phase 11 |
| `.claude/` skills and plugin | none exist; article 14 is blocked on it | After enough repetition to have opinions |
| **A storefront that looks like a shop** | the home page and the taxonomy landed 2026-09-04 and the returns promise 2026-09-07; what is left is the editorial imagery, and a delivery promise the code cannot make yet — a rate needs an address | Deliberately after the floor. See below — it is the largest single item here and the only one that is not deferred for scale |

### A real account, and the menu it earns

Recorded 2026-09-04, unscheduled. Today the account page is one screen: who you
are, sign out, and your orders. A shop people come back to has more, and the
list is worth writing down because each item needs a different amount of work
and two of them are not ours to hold.

| section | what it needs | where it lands |
|---|---|---|
| **Mis pedidos** | nothing — it ships | done |
| **Direcciones** | `Address` exists in the SharedKernel and is frozen onto orders; a BOOK of them is a new thing `Accounts` would own, with a default for checkout to preselect | `Accounts`, small |
| **Preferencias** | `Customer` already carries culture and currency; this is a screen over facts that exist, plus whatever notification settings arrive with a mailer | `Accounts`, small |
| **Cheques regalo** | a balance that is spent at checkout, so it is a second tender beside the payment port and it touches `Pricing` (it is not a discount), `Ordering` and the refund path. The interesting question is whether an unspent balance is a liability the shop tracks — it is | new, real work |
| **Mis pagos** | **not card data.** Storing a PAN is out of the question and the port has no tokenisation concept yet; what a shop keeps is a provider-side token and a masked label. It is a change to `IPaymentProvider` before it is a screen | `Ordering` + the port, medium |

**And this is the moment the account grows a sidebar.** One was declined on
2026-09-04 with the reason that Amazon has a menu because it has fifteen
sections and this had one — a menu with a single item is chrome pretending to be
structure. Three real sections is when it stops pretending, and addresses is the
one that gets it there.

**What to resist:** building the menu first. The order is a section, then a
second section, then the thing that lists them.

### A storefront that looks like a shop

**Reference: [warbyparker.com](https://www.warbyparker.com)** — the user's own,
recorded 2026-09-04. It could not be inspected when this was written (its server
answers 403 to a fetcher and no browser was connected), so nothing below claims
anything about how *they* do it. What is written down instead is what THIS shop
is missing, which is the half that matters when the work starts.

**The honest position today.** Every screen in the storefront is correct,
accessible, tokenised and tested. None of them is a shop. What exists is a
search box, a result grid, a product page, a cart and a checkout — the
transactional spine and nothing around it:

| gap | what is there today |
|---|---|
| **No home** | `/es` renders the search page. A shop's front door is the one screen that says what kind of shop it is, and there is nothing between the header and an empty result grid |
| **The taxonomy is invisible** | phase 2 built categories with a localized name, a parent and a materialized path, and the ONLY consumer is the search index. Nothing browses them, so "Hogar › Cocina › Menaje" exists as data and as a breadcrumb and never as a way in |
| **No imagery beyond the product shot** | and the seed's point at `cdn.example.com` on purpose, so a fresh clone is a grid of fallback tiles. The design has an editorial device — the awning stripe — used on exactly one `<hr>` |
| ~~**No promise on the PDP**~~ | **Closed 2026-09-07.** `initial-plan.md` §2 carried the number: **44% of sites do not show the return policy on the product page while ~60% of shoppers look for it there.** The page says it now, and it says only what the code honours — the window runs from DELIVERY because that is what `CanOpenAt` measures, and the shipping is not in the refund because `RefundableFrom` does not put it there. The number is published at `GET /api/returns/policy` rather than typed into a template: it was already written twice, in `ReturnRequest.Window` and in the order page's `{ days: 14 }`, with nothing comparing them |
| **The empty states are the honest minimum** | "Nothing here yet" is correct and is not a reason to stay |

**What it is NOT.** Not a rewrite of `design/tokens.css`, and not a change to
`design/DESIGN.md`'s opinions — the awning stripe, the clay accent, one accent
action per view, Bricolage/Instrument Sans/JetBrains Mono and tabular numbers all
stay. The system is fine; it is barely used. This is a **page** problem, not a
token problem, and any proposal that starts by picking new colours has
misdiagnosed it.

**Why it is not scheduled.** Every other row in this table is deferred for
scale; this one is deferred for sequence. A shop that looks beautiful and cannot
tell you why a discount did not apply is the demo this project exists to argue
against — the floor first, then the surface. It also wants two things that do not
exist yet: real photography (the image derivatives row above) and something to
browse (facets, phase 8).

**Enters when** phase 8 gives the catalogue facets to browse by, or earlier if a
published article needs the screenshots to carry it — article 00 leads with a
live search box, and a front page nobody has designed is a poor first impression
for the piece that introduces the project.

**One caution for whoever picks this up.** Take the *shape* of a reference, never
its palette. The single most recognisable failure mode in 2026 design is the
warm-cream, high-contrast-serif, terracotta-accent cluster that DESIGN.md already
names and deliberately avoids — and it is exactly what "make it look like that
nice site" produces.

