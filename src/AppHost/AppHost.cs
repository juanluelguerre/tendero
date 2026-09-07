using System.Globalization;
// Orchestrating Tendero locally: nothing here requires signing up anywhere or
// paying for anything (initial-plan §1).
//
// Only what the code consumes TODAY is declared. Qdrant, Ollama and Redis are in
// the target architecture (docs/architecture.md) and arrive in the same PR that
// brings the embeddings worker: declaring them earlier is five gigabytes of
// download on first start and resources nobody reads.

var builder = DistributedApplication.CreateBuilder(args);

// The repository root, for the file paths the API needs. Without this,
// "seed/products.sample.json" resolves against the API's content root (src/Api/)
// and the import fails with a 500 on a freshly cloned repo.
var repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));

// The browsing tools, and they are OFF by default for the reason stated at the
// top of this file: only what is used today is declared, because everything else
// is download nobody asked for on somebody's first clone.
//
//   Tendero__Tools=true dotnet run --project src/AppHost
//
// Both are chosen for weight rather than power. pgweb is a single Go binary of
// about 30 MB that is, essentially, a SQL box — against pgAdmin's half a
// gigabyte of admin console nobody here administers. elasticvue is a static web
// app against Kibana's gigabyte and its own configuration. If somebody
// eventually wants dashboards, Kibana is the answer and this is not.
var tools = string.Equals(builder.Configuration["Tendero:Tools"], "true",
    StringComparison.OrdinalIgnoreCase);

// FIXED HOST PORTS, everywhere something outside Aspire has to reach.
//
// Aspire assigns a random host port per run, which is right for a service only
// its siblings talk to and wrong for everything a PERSON connects to: DataGrip,
// a browser bookmark, a saved pgweb connection, an `elasticvue` cluster entry.
// Configuring a tool once and having it work tomorrow is worth pinning a number.
//
// They are deliberately NOT the defaults — 5432, 9200, 8080 — because those are
// the ones already taken on a developer's machine by the Postgres and
// Elasticsearch they use for work. That collision is the reason `docs/` warns
// never to point SearchEval at localhost:9200.
const int postgresPort = 55432;
const int elasticsearchPort = 59200;
const int pgWebPort = 55433;
const int elasticvuePort = 59201;
const int keycloakPort = 58443;

var postgres = builder.AddPostgres("postgres", port: postgresPort)
    .WithDataVolume(); // the imported catalogue survives a restart

// Aspire's own helper — no new package, it ships with Aspire.Hosting.PostgreSQL,
// which the AppHost already references. It wires the connection for you, so
// there is no password to look up.
//
// Declared always and started on demand, for the reason Keycloak is: a database
// browser you cannot see in the dashboard is one you will open DataGrip instead
// of. `Tendero:Tools=true` starts it with everything else.
postgres.WithPgWeb(pgWeb =>
{
    pgWeb.WithHttpEndpoint(port: pgWebPort, targetPort: 8081, name: "http");
    if (!tools) pgWeb.WithExplicitStart();
});

var database = postgres.AddDatabase("tendero-db");

// Elasticsearch as a plain container resource: the hosting integration published
// for Aspire drags in the 8.x client and would clash with the 9.x that Search
// uses (see docs/adr/0006-dependency-baseline.md).
var elasticsearch = builder.AddContainer("elasticsearch", "docker.elastic.co/elasticsearch/elasticsearch", "9.5.0")
    .WithEnvironment("discovery.type", "single-node")
    .WithEnvironment("xpack.security.enabled", "false")
    .WithEnvironment("ES_JAVA_OPTS", "-Xms1g -Xmx1g")
    .WithHttpEndpoint(targetPort: 9200, port: elasticsearchPort, name: "http")
    // Without a health check, WaitFor(elasticsearch) has nothing to wait for.
    .WithHttpHealthCheck("/_cluster/health", endpointName: "http")
    .WithLifetime(ContainerLifetime.Persistent);

// elasticvue runs in the BROWSER and talks to Elasticsearch directly, so the
// node has to allow it. Without these two the tool loads, looks fine and cannot
// connect to anything — a worse failure than not being there at all.
//
// **The origin is named, not starred, and that is a correction.** The first
// version sent `*`, which was wrong twice over. Elasticsearch writes its
// environment into an `overrides.yml`, and in YAML a bare `*` opens an ALIAS —
// the node died on startup with exit 70 and a parse error about "scanning an
// alias", which says nothing about CORS at all. Quoting it fixed the crash and
// left the real problem: `*` on a node bound to 127.0.0.1 lets ANY page the
// browser visits read and delete these indexes, because a web page can reach
// localhost. Naming elasticvue's own origin closes both, and it is only possible
// because the port is fixed rather than assigned.
//
// Unconditional, so that starting elasticvue from the dashboard works. Two
// environment variables cost nothing; a tool that connects to nothing costs an
// afternoon.
// Built as a variable and not interpolated in place: `WithEnvironment` has an
// overload taking a ReferenceExpression, and an interpolated string binds to
// that one, where an `int` is not a value provider.
var elasticvueOrigin = "http://localhost:" + elasticvuePort.ToString(CultureInfo.InvariantCulture);

elasticsearch
    .WithEnvironment("http.cors.enabled", "true")
    .WithEnvironment("http.cors.allow-origin", elasticvueOrigin);

var elasticsearchEndpoint = elasticsearch.GetEndpoint("http");

// A plain container, like Elasticsearch itself: there is no hosting integration
// for it, and ADR 0006 already established that a container resource is the
// honest answer when there is no package worth trusting.
var elasticvue = builder.AddContainer("elasticvue", "cars10/elasticvue", "1.6.2")
    .WithHttpEndpoint(targetPort: 8080, port: elasticvuePort, name: "http")
    .WithExternalHttpEndpoints()
    .WaitFor(elasticsearch);

if (!tools) elasticvue.WithExplicitStart();

// WHICH ISSUER SIGNS THE TOKENS, and the default is the development one.
//
// `Tendero:Issuer=keycloak` (or `Tendero__Issuer` in the environment) starts
// Keycloak with everything else and points the API at it. Anything else —
// including nothing — leaves the in-process development issuer in charge.
//
// **The resource is declared either way, and that is deliberate.** It used to be
// created only when the flag was set, so launching from Visual Studio — which
// sets no such variable — produced a dashboard with no Keycloak in it at all,
// and the only way to discover that the shop HAS a real identity provider was to
// read this file. A resource nobody can see is a resource nobody uses.
//
// What the flag actually decides is two things: whether it starts on its own,
// and whether the API trusts it. Without the flag it sits in the dashboard
// unstarted, one click from running — which is exactly what you want when the
// reason to reach for it is editing the realm or reading the admin console
// rather than signing a token.
//
// It stays opt-in for startup because of the measurement: the frontends wait for
// the API, which would wait for Keycloak, which takes the better part of a
// minute to become healthy and half a gigabyte to pull the first time. Running
// the stack to look at a product page should not require an identity provider.
//
// (The second reason this was opt-in is gone. The sign-in screens used to ask
// the development issuer for identities no matter who signed, so pointing the
// API at Keycloak produced a 401 on every call and a sign-out on every
// navigation. They follow `GET /api/auth/config` now.)
var issuer = builder.Configuration["Tendero:Issuer"] ?? "dev-issuer";
var useKeycloak = string.Equals(issuer, "keycloak", StringComparison.OrdinalIgnoreCase);

// Keycloak, the real identity provider, beside the development issuer rather
// than instead of it.
//
// **Both exist**, and that is the point of the phase rather than an intermediate
// state: `IdentityProviderContractTests` is abstract, and the swap is only
// honest if the same suite passes against both. The dev issuer stays as the
// thing a fresh clone can use without pulling a container, and Keycloak is what
// proves the abstraction was real.
//
// The integration is `Aspire.Hosting.Keycloak`, MIT and authored by Microsoft,
// checked at its exact version per ADR 0006 — unlike Elasticsearch above, which
// is a plain container precisely because its integration failed that check.
//
// The realm is IMPORTED from a committed file, which is the whole "clone and
// run" promise: users, roles and clients exist on first start, and nobody has to
// click through an admin console to get a token.
// The admin console's credentials, committed on purpose.
//
// Aspire generates this when you do not supply it and stores it in the AppHost's
// user secrets, which means it is DIFFERENT on every machine and findable only
// by opening a JSON file under %APPDATA%. That is a fine default for something
// that might reach production and the wrong one here: the realm beside it
// already seeds users whose password is their username, on the argument that a
// development realm with a password worth protecting is a password that should
// not have been committed. The admin account is the same argument.
//
// It is `secret: true` so the dashboard masks it rather than printing it in a
// resource's environment list, which is a courtesy to whoever is screen-sharing,
// not a security claim.
var keycloakPassword = builder.AddParameter("keycloak-password", "admin", secret: true);

var keycloak = builder.AddKeycloak("keycloak", port: keycloakPort, adminPassword: keycloakPassword)
    .WithDataVolume()
    .WithRealmImport("../../keycloak/realms")
    // Aspire issues Keycloak a certificate from its own development authority
    // and publishes 8443, so the browser reaches it over HTTPS without a
    // warning — which is the part that matters, because the sign-in screen
    // fetches the token endpoint and a certificate a browser refuses would fail
    // before the request left.
    .WithLifetime(ContainerLifetime.Persistent);

if (!useKeycloak)
{
    // Visible, listed, and not started. The Start button in the dashboard is
    // the whole feature: it turns "you would have to know this exists" into
    // something a person finds by looking.
    //
    // Starting it from there does NOT make the API trust it — the API was
    // configured at launch and this is a container, not a negotiation. What it
    // gives you is the admin console, which is how the committed realm gets
    // edited and re-exported.
    keycloak.WithExplicitStart();
}

var api = builder.AddProject<Projects.ElGuerre_Tendero_Api>("api")
    // Without this, WaitFor(api) waits forever: the API exposes /health, but
    // Aspire only treats as "healthy" what is declared here. The worker sat
    // blocked on "Waiting" with 81 unprocessed outbox messages.
    .WithHttpHealthCheck("/health")
    .WithReference(database).WaitFor(database)
    .WithEnvironment("ConnectionStrings__elasticsearch", elasticsearchEndpoint)
    // The API reads from Elasticsearch on every search, so the dependency is as
    // real as the worker's and was equally undeclared. Without this the API
    // declares itself healthy while the node is still starting, the storefront
    // accepts searches already, and the first one comes back as a connection
    // refused against a container that is up but not yet listening.
    .WaitFor(elasticsearch)
    // The issuer's URL, injected rather than hand-written. Pointing
    // `AddJwtBearer` at a different issuer is meant to be a configuration
    // change and nothing else (P7-8) — if it needs more, phase 0 leaked.
    // THE SWAP, and it is one line of configuration (P7-8).
    //
    // `Program.cs` defaults the authority to its own development issuer with
    // `??=`, so setting it here is all it takes for the API to trust Keycloak
    // instead — no code path changes, no second scheme, no branch. That was the
    // claim phase 0 made when it called the identity provider a port, and this
    // is the line that either proves it or does not.
    //
    // AllowHttpMetadata stays true because the realm is served over plain HTTP
    // on a laptop. It is tied to the environment and not to a constant, exactly
    // as it was for the development issuer.
    .WithSeedFiles(repositoryRoot)
    .WithExternalHttpEndpoints();

if (useKeycloak)
{
    api.WithReference(keycloak).WaitFor(keycloak)
        // `Program.cs` defaults the authority to its own development issuer with
        // `??=`, so setting it here is the entire swap — no code path changes, no
        // second scheme, no branch. That was the claim phase 0 made when it called
        // the identity provider a port (ADR 0017), and this is the line that
        // either proves it or does not.
        //
        // AllowHttpMetadata stays true because the realm is served over plain HTTP
        // inside the compose network. It is tied to the environment, not a constant.
        .WithEnvironment("Authentication__Authority", $"{keycloak.GetEndpoint("http")}/realms/tendero")
        .WithEnvironment("Authentication__AllowHttpMetadata", "true");
}

// Aspire brings up both Angular apps too: one command starts everything, and the
// API's URL arrives injected (services__api__http__0) instead of hand-written in
// an environment.ts. The dev server's proxy reads it from there.
var frontend = Path.Combine("..", "..", "frontend");

// NOT `.WithNpm()`, and the numbers are the argument.
//
// It reinstalls on every start, and with two apps over ONE `node_modules` — this
// is a single-package Nx workspace — they contend for the same lock. Measured on
// a warm cache with Keycloak off: the API was healthy at 8s, the storefront
// answered at 77s and the backoffice at **312s**. The same two dev servers
// started on their own take **4 seconds each**, and `npm install` by hand takes
// nine.
//
// So the install is a prerequisite, exactly as `dotnet restore` is: the README
// says to run it, and a fresh clone runs it once. Paying five minutes on every
// F5 to re-check a lockfile that has not changed is the trade this line was
// making, and it is why the frontends "often did not load" — they did, several
// minutes after anybody had stopped waiting.
builder.AddViteApp("storefront", frontend, "serve:storefront")
    .WithHttpEndpoint(targetPort: 4200, port: 4200, name: "http", isProxied: false)
    // Referenced but NOT waited on. The dev server has nothing to ask the API at
    // boot — it serves a page that calls it afterwards — so waiting only couples
    // a four-second start to the backend's. The URL arrives through the
    // reference, which is what the proxy actually needs.
    .WithReference(api)
    .WithExternalHttpEndpoints();

builder.AddViteApp("backoffice", frontend, "serve:backoffice")
    .WithHttpEndpoint(targetPort: 4201, port: 4201, name: "http", isProxied: false)
    .WithReference(api)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.ElGuerre_Tendero_Workers>("workers")
    .WithReference(database).WaitFor(database)
    .WithEnvironment("ConnectionStrings__elasticsearch", elasticsearchEndpoint)
    // THIS one is a real dependency, and it was the one missing:
    // SearchIndexInitializer creates products_es/products_en during startup, so
    // if Elasticsearch is not ready the hosted service throws and the process
    // dies without retrying.
    .WithSeedFiles(repositoryRoot)
    .WaitFor(elasticsearch);

// The worker does NOT wait for the API: it does not depend on it. That was there
// to tidy the dashboard, and the invented dependency left it blocked on
// "Waiting" with the outbox undrained. A declared dependency that does not exist
// is a deadlock waiting its turn.

// ONE GROUP IN DOCKER DESKTOP, instead of eight loose containers.
//
// Aspire labels what it starts with `com.microsoft.developer.usvc-dev.*`, and
// Docker Desktop does not group on those — it groups on the Compose project
// label, which is the only grouping key its container list understands. So a
// machine running Tendero beside two other projects shows one flat list where
// `postgres` and `elasticsearch` could belong to any of them.
//
// Adding the label is the whole fix: the containers collapse under `tendero`,
// and stopping or removing the group acts on this project alone. The service
// label is what puts a readable name on each row rather than Aspire's
// name-plus-hash.
//
// **They are not Compose containers and Docker Desktop will treat them as if
// they were**, which is the honest cost: it offers actions that assume a
// compose file — and there is none, so a "restart project" from there restarts
// containers Aspire believes it owns. Start and stop from the Aspire dashboard;
// use the group to SEE them, not to drive them.
//
// It runs over the model rather than at each call site because three of these
// containers are created by integrations (postgres, pgweb, keycloak) and adding
// it by hand would be a list to keep in step — the same argument
// `SeedFileEnvironment` below makes about the seed files.
const string dockerDesktopGroup = "tendero";

foreach (var container in builder.Resources.OfType<ContainerResource>().ToArray())
{
    builder.CreateResourceBuilder(container)
        .WithContainerRuntimeArgs(
            "--label", $"com.docker.compose.project={dockerDesktopGroup}",
            "--label", $"com.docker.compose.service={container.Name}");
}

builder.Build().Run();

/// <summary>
/// Every committed data file, resolved against the repository root.
///
/// This exists because of a failure that only shows up when the stack actually
/// runs. Each path defaults to something like "seed/attributes.sample.json",
/// relative to the CONTENT ROOT — which is src/Api/ for the API and src/Workers/
/// for the worker, and neither of them has a seed folder. The readers are
/// deliberately forgiving: a missing file means no attribute definitions, no
/// categories, no tariffs and no promotions, and everything carries on working
/// with the previous behaviour.
///
/// So nothing fails. The catalogue imports, search answers, quotes come back —
/// and phase 2's localized attributes, phase 2's category branch and the whole
/// of phase 3's pricing are silently absent. Searching "cocina" found nothing
/// and every quote answered at catalogue price with no promotions, with a green
/// build and 241 passing tests behind it.
///
/// One line per project instead of five: the sixth file is the one that gets
/// forgotten, and this is the only place that has to know the list.
/// </summary>
internal static class SeedFileEnvironment
{
    private static readonly (string Key, string File)[] Files =
    [
        ("Catalog__Connectors__Seed__FilePath", "products.sample.json"),
        ("Catalog__Attributes__FilePath", "attributes.sample.json"),
        ("Catalog__Categories__FilePath", "categories.sample.json"),
        ("Pricing__Seed__PriceListsPath", "pricelists.sample.json"),
        ("Pricing__Seed__PromotionsPath", "promotions.sample.json"),
        ("Inventory__Warehouses__FilePath", "warehouses.sample.json"),
        ("Inventory__Stock__FilePath", "stock.sample.json")
    ];

    public static IResourceBuilder<ProjectResource> WithSeedFiles(
        this IResourceBuilder<ProjectResource> project, string repositoryRoot)
    {
        foreach (var (key, file) in Files)
            project = project.WithEnvironment(key, Path.Combine(repositoryRoot, "seed", file));

        return project;
    }
}
