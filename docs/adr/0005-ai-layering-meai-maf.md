# 0005 — MEAI for pipelines, MAF for agents, no Harness

**Status**: accepted

## Context
Microsoft Agent Framework 1.0 (GA Apr 2026) unified Semantic Kernel and
AutoGen; its Harness (stable since Build 2026) targets long-running autonomous
agents with shell/filesystem access.

## Decision
Microsoft.Extensions.AI for deterministic pipelines (enrichment, translation,
embeddings). Microsoft Agent Framework only where a real agent exists (phase-3
shopping assistant: tool-calling over catalog/stock/cart). No Agent Harness:
Tendero's agents are short-lived, tool-constrained conversations. No
LangChain/AutoGen in .NET services; the Python sidecar is evaluation and
document ingestion only.

## Consequences
Pipelines stay testable without an agent runtime. Revisit if a genuinely
autonomous backoffice agent ever enters the roadmap.
