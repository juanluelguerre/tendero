# AppHost

`dotnet run --project src/AppHost` brings up the whole stack and opens the Aspire
dashboard at http://localhost:15130. What each resource is for, which switch
starts it and which switches to use for which job are in the root
[README](../../README.md#the-local-stack-aspire); this page keeps the reasons
behind how `AppHost.cs` declares them.

## Declared always, started by a switch

Every resource is in the dashboard on every run, including the ones that do not
start: a resource created only behind an environment variable is invisible to
whoever launches from Visual Studio or Rider, and a resource nobody can see is
one nobody uses. The optional ones carry `WithExplicitStart`, so a *Start*
button is one click away.

What a switch adds is the wiring. `Tendero__Vectors`, `Tendero__Knowledge`,
`Tendero__Observability` and `Tendero__Issuer=keycloak` decide, at AppHost start,
both whether their containers start **and** whether the API and the workers are
handed their addresses — one value, so the two cannot disagree. Starting a
container by hand gives you its own UI and wires nothing: each service's
configuration was fixed when it started.

Nothing waits for an optional layer (invariant 8). A missing or slow vector
layer is a search that degrades to lexical, a missing model is claims proposed by
rules alone, a missing collector is a few seconds of dropped spans — never a
service that refuses to start.

## Ports, volumes and limits

- **Fixed ports, off their defaults, below 49152.** A person connects to these
  (a database client, a bookmark, a saved elasticvue cluster), so they cannot
  change per run; 5432 and 9200 are somebody's work services; and Windows
  reserves blocks inside 49152–65535 that move on every restart, which once left
  the stack unable to start. The numbers kept their tails (55432 became 35432).
- **Named volumes.** `WithDataVolume()` with no name hashes the AppHost's path,
  and renaming the folder once left the catalogue attached to nothing.
  Elasticsearch and Qdrant have no volume on purpose: both are projections
  rebuilt from Postgres, and a stale vector volume would keep points from a model
  revision that may no longer be the configured one.
- **Every container has a memory and CPU limit** sized from a measurement — the
  peak under load plus a margin — with the figure beside it in `AppHost.cs`.
- **One group in Docker Desktop.** Every container carries the Compose project
  label `tendero`. Use the group to see them; start and stop them from the Aspire
  dashboard, because Docker Desktop's project actions assume a compose file that
  does not exist.

## The frontends are not reinstalled on every start

Both Angular apps are Vite resources with `.WithNpm(install: false)`. With the
installer on, two `npm install`s over one `node_modules` contended for the same
lock: the backoffice answered after 312 s, where the dev server alone starts in
4. `npm ci` in `frontend/` is a prerequisite, like `dotnet restore`; the
`*-installer` resources stay listed and unstarted.

## Elasticsearch is not an Aspire integration

It is a plain `AddContainer` on purpose: the published hosting package declared
no licence and pinned the 8.x Elastic client, which would clash with the 9.x
that `ElGuerre.Tendero.Search` uses. Keycloak, Postgres and Qdrant, on the other
hand, are the official integrations, each checked at its exact version — MIT, and
depending on exactly the `Aspire.Hosting` the AppHost resolves. The reasoning is
in `docs/adr/0006-dependency-baseline.md`.

## What is not declared yet

Ollama and Redis are in the target architecture (`docs/architecture.md`) and not
here: no line of code reads them. They arrive with the change that consumes
them, as the vector layer (phase 8), the Grafana stack (phase 9b) and the claims
models (phase 10) did.

## Upgrading Aspire

The SDK in `ElGuerre.Tendero.AppHost.csproj` and the `Aspire.Hosting.*` versions
in `Directory.Packages.props` move together. Check each package's licence and
its Aspire dependency at the exact version (ADR 0006), and what each integration
pins: `Aspire.Hosting.Qdrant` pins `Qdrant.Client`, and Search must use exactly
that version or `-warnaserror` fails on the downgrade. `Aspire.Hosting.Keycloak`
is only ever published as a preview.
