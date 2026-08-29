# 0006 — Dependency and toolchain baseline

**Status**: accepted

## Context
The licensing policy demands a permissive OSI license verified for the exact
version of every package, and the plan demands .NET 11 preview. Both had to be
turned into concrete pins before any code could build.

## Decision
Central Package Management (`Directory.Packages.props`) is the single file where
versions live, so a licence review is one file to read. Verified at pin time:
Carter 10.0.0 (MIT), FluentValidation 12.1.1 (Apache-2.0), Elastic.Clients
.Elasticsearch 9.5.1 (Apache-2.0), EF Core 11.0.0-preview.6 (MIT), Npgsql EF
provider 11.0.0-preview.6 (PostgreSQL License — OSI-approved, BSD-style),
OpenTelemetry 1.18.0 (Apache-2.0), Aspire 13.5.3 and CommunityToolkit Ollama
13.5.0 (MIT), xunit.v3 4.0.0 (Apache-2.0), NetArchTest.Rules 1.3.2 (MIT,
declared upstream but not in the nuspec).

Two consequences of the preview line are load-bearing:

- **EF Core is pinned one preview behind the SDK.** The Npgsql provider takes an
  exact dependency on the EF version, so EF moves when Npgsql moves.
- **`global.json` names a prerelease floor** (`11.0.100-preview.1.0`). Roll-forward
  only rolls up and a preview sorts below its own release, so `11.0.100` plus
  `allowPrerelease` resolves nothing on a preview-only machine.

**Elasticsearch is not an Aspire hosting integration.** `Aspire.Hosting
.Elasticsearch` moved to elastic/elastic-aspire-dotnet, declares no licence
expression in the package, targets net8.0 and pins the 8.x Elastic client, which
would fight the 9.x client Search uses. It is a plain container resource in the
AppHost instead.

Not added, because nothing uses them yet: NSubstitute, Bogus, Verify, CsCheck,
Testcontainers, Respawn, EF Core Design. Each enters with the first test that
needs it, and gets its licence checked then.

## Consequences
Adding a package means editing one reviewed file. A monthly preview bump is
expected to break something; that is accepted and bloggable. If Npgsql's licence
or the Elastic packaging changes, this ADR is the place that gets amended.
