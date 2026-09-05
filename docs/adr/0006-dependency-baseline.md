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

Not added at the time, because nothing used them yet: NSubstitute, Bogus, Verify,
CsCheck, Testcontainers, Respawn, EF Core Design. Each enters with the first test
that needs it, and gets its licence checked then.

**Added since, each checked at its version** (kept here so the review stays one
file): `Microsoft.EntityFrameworkCore.Design` and `Testcontainers.PostgreSql`
4.14.0 (both MIT) with the migrations (phase 0); `Microsoft.AspNetCore.OpenApi`,
`Microsoft.AspNetCore.Authentication.JwtBearer` and
`Microsoft.AspNetCore.Mvc.Testing` (MIT, the framework's own line) with the
contract test and the identity port (phase 0); `CsCheck` 4.8.0 (MIT) with the
promotion properties (phase 3); `Aspire.Hosting.JavaScript` 13.5.3 (MIT) for the
two Angular apps and `Aspire.Hosting.Keycloak` 13.5.3-preview.1 (MIT, its only
Aspire dependency being exactly the 13.5.3 the AppHost resolves — `P7-5`) with
the issuer swap (phase 7); and on npm, `angular-oauth2-oidc` 22.0.2 (MIT) for the
redirect sign-in. **Respawn was declined**: a fresh database per test is cheaper
than a reset. NSubstitute, Bogus and Verify are still not in, for the original
reason. The CommunityToolkit Ollama hosting package verified above is no longer
referenced — it left with the flag article 07 is about, and comes back with
phase 8.

## Known friction with the preview line

Two things bite in day-to-day work and are worth writing down once:

- **Rider (2026.2) reports "\.NET SDK 11.0.100 is not fully supported"** and
  degrades some analysis. Expected: no IDE supports an SDK that has not shipped.
  Build and run work — Rider picks up the preview MSBuild correctly. Nothing to
  fix; it resolves itself at GA. `LangVersion` is deliberately *not* set, so the
  language stays at the SDK default (C# 14, which is what CLAUDE.md prescribes)
  rather than opting into preview language features nothing uses.
- **Rider rewrites `global.json` on solution load and drops the `test` section.**
  Without it `dotnet test` fails with "Testing with VSTest target is no longer
  supported", because xUnit v3 runs on Microsoft.Testing.Platform and the .NET
  10+ SDK needs the opt-in. If that error appears out of nowhere, the section is
  what went missing — `dotnet.config` is not an alternative, the preview SDK
  ignores it. Restore it and, ideally, report the rewrite to JetBrains.

## Consequences
Adding a package means editing one reviewed file. A monthly preview bump is
expected to break something; that is accepted and bloggable. If Npgsql's licence
or the Elastic packaging changes, this ADR is the place that gets amended.
