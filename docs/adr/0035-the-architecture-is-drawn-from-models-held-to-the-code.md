# ADR 0035 — The architecture is drawn from models, and each model is held to the code

Status: accepted · 2026-09-23 · Related: [ADR 0006](0006-dependency-baseline.md), [ADR 0014](0014-context-map.md), [ADR 0017](0017-the-identity-provider-is-a-port.md), [ADR 0033](0033-a-design-artefact-is-disposable.md)

## Context

The architecture was described in Mermaid blocks in `docs/architecture.md` and in
the diagrams of article 00, each drawn by hand and none checked against anything.
Two questions nobody could answer from them: who uses Tendero and through which of
its three agent surfaces — the system context, C4's level 1, which did not exist —
and what the domain actually contains, which only reading five `Domain/` folders
told you.

A diagram drawn by hand is a second source of truth, and this repository already
has the record of what that costs: the mockups nobody read (ADR 0033), and a
figure that was wrong for three weeks — `Order` has seven states and **ten**
edges, and `current-state.md`, `roadmap.md` and a comment in `ReturnRequest.cs`
said twelve. It was found the first time the table was drawn from the code.

Two tools were weighed for C4, on 2026-09-23:

- **Structurizr**, the reference tool by C4's author. Mid-transition: Structurizr
  Lite was archived on 2026-02-04, the cloud service closes on 2026-09-30, and the
  consolidated build is open core — the CLI is free, the server in the prebuilt
  binaries needs a paid licence unless built from source.
- **LikeC4**, MIT at 1.59.4 (published 2026-09-21), a model language in the same
  family: one model, many views. It validates with a non-zero exit on an
  unresolved reference, exports the model as JSON and each view as Mermaid.
  Audited before use: its only telemetry is a language-server channel to the
  editor, which its CLI starts disabled; no outbound call of its own was found.

## Decision

**1 · Level 4 is generated from the code.** `docs/domain-model.md` is written by
reflection over the public types of the five `*.Domain` namespaces — a class
diagram per context, the events, every `TransitionTable` as a state machine — by
`tests/Architecture.Tests/DomainModelDocument.cs`. A test fails when the file and
the code disagree; `UPDATE_DOMAIN_MODEL=1` regenerates it. It labels only what the
code declares (`aggregate`, `record`), never "entity" or "value object".

**2 · Levels 1 and 2 are a LikeC4 model, `docs/c4/tendero.c4`.** A container's id
is its Aspire resource name, and `tools/c4.mjs` fails when the two lists disagree
in either direction. An AppHost resource that is not drawn as a container is named
in the script with its reason — today the database inside Postgres, a development
tool, and Keycloak, drawn as the external identity provider it is in production
(ADR 0017; decided by the author on 2026-09-23).

**3 · The diagrams are committed as Mermaid**, in `docs/c4/README.md`, so GitHub
renders them with nothing running. CI regenerates them and fails on a difference.

**4 · LikeC4 is a tool, not a dependency.** It runs through `npx` at an exact
version, links into nothing and ships with nothing, like Stryker in the tool
manifest. Its licence is checked at that version, and a version bump is a one-line
change that CI proves.

**5 · Level 3 is not modelled.** Every slice has the same components, and
`docs/architecture.md` describes that shape once.

## Consequences

- Changing the domain, a transition table or the AppHost's resources without
  regenerating the matching document fails the build, which is the point.
- The AppHost is read with a pattern over `AppHost.cs`, not by building its
  model: that would take `Aspire.Hosting.Testing` and an answer to which
  configuration flags to set. The script refuses to pass when the pattern finds
  no `api`, so a pattern that stopped matching cannot pass vacuously.
- The Mermaid LikeC4 writes uses the `@{ icon: … }` node syntax of Mermaid 11. It
  parses with Mermaid 11 locally; if a renderer is older, the fix is a LikeC4
  option or a post-processing step, not a hand-drawn diagram.
- The Mermaid diagrams in `docs/architecture.md` stay for now. They describe the
  same containers, and replacing them with links to `docs/c4/` is a follow-up,
  not part of this decision.
