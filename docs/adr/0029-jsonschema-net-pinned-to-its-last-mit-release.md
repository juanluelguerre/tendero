# ADR 0029 — JsonSchema.Net is pinned to 8.0.5, its last MIT release

**Status:** accepted · 2026-09-09

## Context

Phase 9's headline claim is that Tendero is a .NET implementation of UCP. The
strongest evidence available for it is not a passing demo — it is the served
profile and the catalogue answers validated against **the specification's own
JSON Schemas**, which needs a JSON Schema library.

`JsonSchema.Net` is the obvious choice, and the licence was reported here as MIT
on the strength of the project being MIT. Reading the nuspec of the version that
was about to be added said otherwise:

```xml
<license type="file">OSMFEULA.txt</license>
```

The **source** is still MIT. The **binary release on NuGet**, from 9.0.0 onward,
carries an Open Source Maintenance Fee agreement: a monthly fee for users who use
the software in revenue-generating activity with annual gross revenue at or above
US$10,000.

That is the same move CLAUDE.md bans MediatR, AutoMapper, MassTransit v9+ and
FluentAssertions v8+ for. It is not a judgement about whether the model is fair —
it is a rule about what this repository will link against.

## Decision

**Pin 8.0.5, the last release published under a plain MIT expression**, which is
the fallback CLAUDE.md already prescribes: *"If a dependency later changes
license, pin the last free version and open an ADR."* This is that ADR.

The chain was checked to the bottom, because a permissive package with a
non-permissive dependency is the same problem one level down:

| Package | Version | Licence |
|---|---|---|
| JsonSchema.Net | 8.0.5 | MIT (expression) |
| JsonPointer.Net | 6.0.1 | MIT (expression) |
| Json.More.Net | 2.2.0 | MIT (expression) |
| Humanizer.Core | 2.14.1 | MIT (expression) |

The change happened exactly at 9.0.0: 8.0.0 is MIT and 9.0.0 is not, which is
worth recording because a minor bump looks harmless and this one is not.

**It is a test-only dependency.** The product does not validate its own manifest
at run time; a test does. That does not make the licence question smaller — the
package is still linked — but it does mean nothing shipped to a user contains it.

## Consequences

Renovate or Dependabot will offer 9.x. **The bump must be refused**, and this ADR
is why. A `dependabot.yml` ignore entry is the cheap enforcement and belongs with
the next dependency sweep.

Pinning an old version is a real cost: no fixes, no new draft support. It is
accepted because the alternative is worse in both directions — hand-rolling JSON
Schema 2020-12 with `$ref` resolution across 116 files, or paying a fee for a
test-time dependency in a project whose whole dependency policy exists to avoid
exactly that.

If 8.0.5 ever stops being enough, the next candidates are evaluated licence-first
rather than feature-first, which is the order this ADR exists to keep.

**The general lesson, and the reason this file is not just a version pin:** the
licence was reported as MIT from memory of the project, and it was wrong for the
version that mattered. ADR 0006 says to verify the exact version. This is the
second time that has paid — the first was `Aspire.Hosting.Elasticsearch`
declaring no licence at all — and both times the check took two minutes.
