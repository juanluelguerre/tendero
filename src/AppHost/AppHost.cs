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

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();          // the imported catalogue survives a restart

var database = postgres.AddDatabase("tendero-db");

// Elasticsearch as a plain container resource: the hosting integration published
// for Aspire drags in the 8.x client and would clash with the 9.x that Search
// uses (see docs/adr/0006-dependency-baseline.md).
var elasticsearch = builder.AddContainer("elasticsearch", "docker.elastic.co/elasticsearch/elasticsearch", "9.5.0")
    .WithEnvironment("discovery.type", "single-node")
    .WithEnvironment("xpack.security.enabled", "false")
    .WithEnvironment("ES_JAVA_OPTS", "-Xms1g -Xmx1g")
    .WithHttpEndpoint(targetPort: 9200, name: "http")
    // Without a health check, WaitFor(elasticsearch) has nothing to wait for.
    .WithHttpHealthCheck("/_cluster/health", endpointName: "http")
    .WithLifetime(ContainerLifetime.Persistent);

var elasticsearchEndpoint = elasticsearch.GetEndpoint("http");

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
    .WithSeedFiles(repositoryRoot)
    .WithExternalHttpEndpoints();

// Aspire brings up both Angular apps too: one command starts everything, and the
// API's URL arrives injected (services__api__http__0) instead of hand-written in
// an environment.ts. The dev server's proxy reads it from there.
var frontend = Path.Combine("..", "..", "frontend");

builder.AddViteApp("storefront", frontend, "serve:storefront")
    .WithNpm()
    .WithHttpEndpoint(targetPort: 4200, port: 4200, name: "http", isProxied: false)
    .WithReference(api).WaitFor(api)
    .WithExternalHttpEndpoints();

builder.AddViteApp("backoffice", frontend, "serve:backoffice")
    .WithNpm()
    .WithHttpEndpoint(targetPort: 4201, port: 4201, name: "http", isProxied: false)
    .WithReference(api).WaitFor(api)
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
