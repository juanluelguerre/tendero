# Steward S-6 · Measured and evaluated — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Steward is measured three ways — what it costs and how fast (metrics and a Grafana dashboard), whether it is used and worth it (four staff events), and whether it is right (the `copilot` evaluation suite, exact, no judge) — and the local model's context window is fixed from what the suite measured, not estimated.

**Architecture:** The metering client already writes a row per call; it now also counts calls and cost as metrics, and a turn counts itself by status, so `ai-usage.json` reads Prometheus like `shop-pulse` does (tokens and latency come from Microsoft.Extensions.AI's own GenAI instruments). The browser reports four events through `BackofficeUsage`, declared in the tracking plan with their question and decision. The `copilot` suite composes the real turn in-process — the contexts' tools, Assist, persistence — over a disposable Postgres laboratory it prepares itself (the seed catalogue, its stock, three products stripped of their English name), records every tool call through a decorator, and compares tools, arguments and proposals exactly. The `Scripted` model learns rounds and references to earlier tool results, so CI's browser spec can drive panel → card → review → approve → audit row over real products.

**Tech Stack:** .NET 11, Microsoft.Extensions.AI, Microsoft Agent Framework, OpenTelemetry/Prometheus/Grafana, Angular 22, Playwright.

**Spec:** `docs/specs/steward.md` — *AI observability*, *Evidence of use*, *Evaluation and tests*, *The models* (the window fixed from measurement).

## Global Constraints

- No event carries the question's text or anyone's identity (ADR 0036, 0041).
- No judge model: a proposal is a command, compared exactly.
- The model-backed run is local and at phase close; CI runs the suite's scoring with `Scripted`.
- Containers get sized resource limits; the disposable Postgres is deleted after the run.
- Hosted profiles are S-7's: no paid call here.

## Review Focus

1. The suite against a model that calls the wrong tool, invents arguments, or proposes nothing: each case scores it, nothing crashes.
2. A case whose laboratory product is missing (a renamed seed): the suite says which, and stops before asking a model.
3. An event emitted twice for one turn (a retry, the panel reopened).
4. The metrics' cardinality: profile, model, operation, outcome — never a conversation id.
5. The browser spec over a fresh stack: the product it proposes for exists by name, not by a hard-coded id.

---

### Task 1: Metrics and `ai-usage.json`
`tendero.ai.calls` (operation, profile, model, outcome) and `tendero.ai.cost` (micro-euros, same labels) in `MeteringChatClient`; `tendero.steward.turns` (status, fell_back) in the turn. Tracking plan metrics; the dashboard beside `shop-pulse`: cost per day and per model, calls and errors, latency p50/p95 and tokens per model (GenAI instruments), turns by status, fallbacks. Tests with the instrument recorder.

### Task 2: The four events
`steward_opened` (view), `steward_answered` (status, tools, calls, cost bucket, profile), `proposals_reviewed` (kind proposals | claims, proposed, approved, rejected, unticked), `steward_unavailable` (fell_back); the Proposals screen in `backoffice_view_opened`. Tracking plan with question and decision; regenerated types; store specs.

### Task 3: The scripted model, in rounds
`ScriptedTurn.Then` — a second round of calls after the first round's results — and `{from:<tool>:<path>}` in an argument, replaced by that tool's latest result at that JSON path. Contract tests in `AI.Tests`.

### Task 4: The `copilot` suite
`--suite copilot --database <conn> [--chat <url> --model <id> | --script <file>]`; the laboratory fixture; `tools/SearchEval/copilot/cases.json` (20–30 cases); the scorer (pure, `SearchEval.Tests`); the report: right tool, valid arguments, exact proposals, latency p50/p95, tokens, € and the largest conversation.

### Task 5: The browser spec
`Tendero__StewardScript=<file>` in the AppHost points the API at a Scripted profile; CI sets it; `steward.spec.ts`: ask → card → review → approve → the audit row naming the proposal.

### Task 6: Measured on the local model
The suite against `steward-model`; results into `docs/analysis/steward-models.md`; `ContextTokens` and the container's `-c` fixed from the largest conversation plus a margin.

### Task 7: The record
Spec as built, board, notebook, CLAUDE.md commands.
