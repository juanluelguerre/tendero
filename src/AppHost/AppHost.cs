using System.Globalization;
// Orchestrating Tendero locally: nothing here requires signing up anywhere or
// paying for anything (initial-plan §1).
//
// Only what the code consumes is declared. Qdrant and Text Embeddings Inference
// (ADR 0031) are declared for phase 8's vector layer and START ON DEMAND, because
// on a CPU the embeddings container alone holds 12.7 GiB; Redis is still only in
// the target architecture (docs/architecture.md).

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
//
// And all of them BELOW 49152 (moved on 2026-09-26). Windows hands out
// 49152–65535 as its dynamic range, and Hyper-V and WinNAT reserve blocks of a
// hundred ports inside it that move on every restart: one day 55371–55470 and
// 52925–53024, covering Postgres, pgweb and Grafana, and the stack could not
// start — Aspire's DCP warns "a port in the ephemeral range" on every start for
// exactly this. The numbers kept their tails (55432 became 35432) so they still
// read as what they are.
const int postgresPort = 35432;
const int elasticsearchPort = 39200;
const int pgWebPort = 35433;
const int elasticvuePort = 39201;
const int keycloakPort = 38443;
const int qdrantGrpcPort = 36334;
const int qdrantHttpPort = 36333;
const int embeddingsPort = 38081;
const int rerankerPort = 38082;
const int extractorPort = 38083;
const int supportPort = 38084;
const int stewardModelPort = 38085;
const int grafanaPort = 33000;
const int collectorGrpcPort = 34317;
const int collectorHttpPort = 34318;
const int faroPort = 38027;

// NAMED VOLUMES, and the name is the fix.
//
// `WithDataVolume()` with no argument names the volume after a hash of the
// AppHost's PATH — `elguerre.tendero.apphost-4c82261971-postgres-data`. Renaming
// the folder on 2026-09-14 changed the hash, and the next start created an empty
// database beside the old one: the imported catalogue, the orders and the
// customers were all still on disk, attached to nothing. Nothing failed; the shop
// simply had no products. A fixed name survives a rename, a second clone and a
// move to another drive, which is what `tendero-tei-models` below already does.
// Every container carries a memory and CPU limit sized from a measurement (the
// rule since 2026-09-26): the peak under load, plus a margin, never a round guess.
//
// **The CPUs are capped at the ones the machine has**, because Docker refuses a
// container that asks for more — "range of CPUs is from 0.01 to 2.00" — and
// does not start it. GitHub's runner for a private repository has two, so from
// 2026-09-27 Elasticsearch's four kept it from starting, the API waited for it
// forever, and the end-to-end job hung for three days of merges. A laptop with
// fewer cores than the measurement would have met the same wall.
static string Cpus(int measured) =>
    "--cpus=" + Math.Min(measured, Environment.ProcessorCount).ToString(CultureInfo.InvariantCulture);

// Postgres peaked at 49 MB serving the catalogue and the claims layer at once.
var postgres = builder.AddPostgres("postgres", port: postgresPort)
    .WithDataVolume("tendero-postgres-data") // the imported catalogue survives a restart
    .WithContainerRuntimeArgs("--memory=512m", "--memory-swap=512m", Cpus(2))
    // What a query costs, kept by the server itself: the statistics the
    // Postgres MCP server reads for its top queries and its workload analysis
    // (README, "Profiling the database"). The library ships in the image and
    // has to be loaded when the server starts; the extension itself is created
    // once in the database, by hand, as the README says.
    .WithArgs("-c", "shared_preload_libraries=pg_stat_statements");

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
    // A 1 GB heap held 1.66 GB of container memory under load: the limit is 2.
    .WithContainerRuntimeArgs("--memory=2g", "--memory-swap=2g", Cpus(4))
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

// THE VECTOR LAYER, declared always and started on demand (phase 8, ADR 0031).
//
//   Tendero__Vectors=true dotnet run --project src/AppHost
//
// Off by default for the measurement, not for tidiness: on a CPU the embeddings
// container held 12.7 GiB of RAM in the spike that chose it, and a first start
// downloads 2.2 GB of weights. Pressing F5 to look at a product page should not
// cost either. Visible in the dashboard either way — the argument Keycloak
// already made — but starting the containers from there does NOT wire them to
// search: the API and the worker are handed the vector layer only when the
// switch is on at AppHost start, so restart with it to search by meaning.
//
// **Nothing waits for it, and that is invariant 8 rather than an omission.**
// Lexical search is the permanent fallback: with the switch off the API and the
// worker are not handed the vector layer at all, and with it on they get both
// references and no `WaitFor` — so a missing or stopped vector layer is a search
// that degrades to lexical, never a service that refuses to start.
var vectors = string.Equals(builder.Configuration["Tendero:Vectors"], "true",
    StringComparison.OrdinalIgnoreCase);

// Qdrant through its Aspire integration: MIT, and its only Aspire dependency is
// exactly the Aspire.Hosting 13.6.0 the AppHost resolves — the check Keycloak
// passed and Elasticsearch did not (ADR 0006). It pins the server image to
// v1.18.0 and brings Qdrant.Client 1.19.0 with it, which is the version Search
// has to use too.
//
// The API key is committed for the reason the Keycloak password is: a generated
// one lives in this machine's user secrets and nowhere a person looks, and the
// Qdrant dashboard asks for it. `secret: true` masks it on screen; it protects
// nothing, and a development collection has nothing to protect.
//
// **No data volume**, on purpose and like Elasticsearch: vectors are a disposable
// projection rebuilt from Postgres (ADR 0012), and a volume would only keep
// vectors from a model revision that may no longer be the configured one.
var qdrantKey = builder.AddParameter("qdrant-key", "tendero-dev", secret: true);

var qdrant = builder.AddQdrant("qdrant", apiKey: qdrantKey, grpcPort: qdrantGrpcPort, httpPort: qdrantHttpPort)
    // 80 to 88 MiB measured on 2026-09-28, after three hours of a walkthrough
    // and TrafficSim over this catalogue: a hundred 1,024-dimension points. The
    // limit leaves room for the optimiser and a reindex, not for the host's 31 GB.
    .WithContainerRuntimeArgs("--memory=512m", "--memory-swap=512m", Cpus(2))
    .WithLifetime(ContainerLifetime.Persistent);

// The embedding model, served by Hugging Face Text Embeddings Inference and
// pinned to the revision whose licence was checked (ADR 0031). A tag or `main`
// would name something nobody checked. The container reports the revision it
// loaded from its info endpoint, and that is what the startup check compares
// against the configured descriptor (P8-3).
//
// ON THE GPU WHEN THIS MACHINE HAS ONE, on the CPU when it does not, decided here
// at start-up rather than written into a file somebody forgets to change.
//
//   Tendero__Inference__Device=auto|cpu|gpu   (auto when unset)
//
// The numbers are why it matters (bge-m3 on TEI commit 4150561, 2026-09-15,
// RTX 5070 Laptop): twenty product texts in 0.375 s and a query in 17 ms on the
// GPU, with 1.3 GB of VRAM and 1.1 GiB of RAM — against 9.7 s and ~100 ms on the
// CPU. And the CPU's memory depends on sizing the batch to this catalogue: TEI's
// defaults (16,384 batch tokens, 15 tokenisation workers) held 12.6 GiB of RAM,
// where the arguments below hold 3.7 GiB at the same speed.
//
// TEI is PINNED TO A COMMIT, not to its latest release, and the reason is a bug:
// the 1.9.3 images read the driver's CUDA version by matching "CUDA Version" in
// nvidia-smi's header, NVIDIA drivers 6xx print "CUDA UMD Version", the match
// comes back empty, the entrypoint prepends CUDA's forward-compatibility library
// — which cannot drive the device on these drivers — and TEI runs on the CPU with
// a warning and nothing else. Upstream fixed it (huggingface/text-embeddings-
// inference#870, merged 2026-07-08) after the last release; `sha-4150561` is the
// commit that carries the fix, and it moves to a release tag when one exists.
//
// The model cache IS a volume, unlike Qdrant: it holds downloaded weights, not a
// projection, and losing it costs 2.2 GB and four minutes on the next start. The
// reranker below shares it.
const string embeddingModel = "BAAI/bge-m3";
const string embeddingModelRevision = "5617a9f61b028005a4858fdac845db406aefb181";
const string textEmbeddingsInferenceCommit = "sha-4150561";

var inference = InferenceHardware.Detect(builder.Configuration["Tendero:Inference:Device"]);

var embeddings = builder.AddContainer("embeddings", "ghcr.io/huggingface/text-embeddings-inference",
        inference.ImagePrefix + textEmbeddingsInferenceCommit)
    .WithArgs("--model-id", embeddingModel, "--revision", embeddingModelRevision)
    .WithArgs(InferenceHardware.ServerArguments)
    .WithHttpEndpoint(targetPort: 80, port: embeddingsPort, name: "http")
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithVolume("tendero-tei-models", "/data")
    // What was chosen and why, visible in the dashboard's environment list —
    // TEI ignores it, a person reading the resource does not.
    .WithEnvironment("TENDERO_INFERENCE_DEVICE", inference.Description)
    // Measured on 2026-09-28 on the GPU: 1.2 GiB of RAM fresh, 3.27 GiB after
    // three hours of a walkthrough and TrafficSim; on the CPU, with the batch
    // sized below, 3.7 GiB (P8-3). Without a limit Docker offered it the host's
    // 31 GB, which is the over-provisioning the rule exists to stop.
    .WithContainerRuntimeArgs(inference.UsesGpu
        ? ["--memory=4g", "--memory-swap=4g", Cpus(4)]
        : ["--memory=4608m", "--memory-swap=4608m", Cpus(8)])
    .WithLifetime(ContainerLifetime.Persistent);

if (inference.UsesGpu)
    embeddings.WithContainerRuntimeArgs("--gpus", "all");

if (!vectors)
{
    qdrant.WithExplicitStart();
    embeddings.WithExplicitStart();
}

var embeddingsEndpoint = embeddings.GetEndpoint("http");

// THE RERANKER (P8-6), same server, same pinning, and it starts on its own only
// where it can keep its budget.
//
// bge-reranker-v2-m3 has no ONNX export at the pinned revision, so TEI runs it on
// candle, and on this laptop's CPU that measured 9.8 to 12.3 seconds for twenty
// pairs — against a search budget of 800 ms. On a CPU it would fail every single
// time, and a stage that always degrades is a cost with no benefit. So it starts
// with the vector layer when the machine has a GPU, and otherwise sits in the
// dashboard unstarted.
//
// It is one decision now, not two (ADR 0023 amendment): `rerank`, below, decides
// both whether the container starts AND whether the API is ever handed its
// address — `WithReference(rerankerEndpoint)` is conditional on it too, beside
// the API's own declaration. Reranking has no flag of its own any more, so this
// is the only place that decides it. Handing the API an address nothing answers
// would register a real `IReranker` that fails every call, and every search
// would pay a failed connection and count as degraded; leaving the reference out
// entirely gives the API `NoReranker` (`IsConfigured => false`), which
// `HybridSearchStrategy` skips without asking and without counting.
const string rerankerModel = "BAAI/bge-reranker-v2-m3";
const string rerankerModelRevision = "953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e";

var rerank = vectors && inference.UsesGpu;

var reranker = builder.AddContainer("reranker", "ghcr.io/huggingface/text-embeddings-inference",
        inference.ImagePrefix + textEmbeddingsInferenceCommit)
    .WithArgs("--model-id", rerankerModel, "--revision", rerankerModelRevision)
    .WithArgs(InferenceHardware.ServerArguments)
    .WithHttpEndpoint(targetPort: 80, port: rerankerPort, name: "http")
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithVolume("tendero-tei-models", "/data")
    .WithEnvironment("TENDERO_INFERENCE_DEVICE", inference.Description)
    // 2.1 to 2.3 GiB measured on 2026-09-28 on the GPU, the only device it
    // starts on by itself (below); the limit is that and a quarter more.
    .WithContainerRuntimeArgs("--memory=3g", "--memory-swap=3g", Cpus(4))
    .WithLifetime(ContainerLifetime.Persistent);

if (inference.UsesGpu)
    reranker.WithContainerRuntimeArgs("--gpus", "all");

if (!rerank)
    reranker.WithExplicitStart();

var rerankerEndpoint = reranker.GetEndpoint("http");

// THE CLAIMS LAYER, declared always and started on demand (phase 10, ADR 0018).
//
//   Tendero__Knowledge=true dotnet run --project src/AppHost
//
// Off by default for the vector layer's reason: the extractor holds ~5 GB and a
// first start downloads, converts and quantizes a 9 GB model. With the switch off
// the worker gets no model at all, and claims are proposed by the rules alone —
// the path with no model (invariant 8), exact about the little it sees.
//
// Every figure below is P10-0's measurement, not a guess, and every container has
// a memory and CPU limit sized from it (the rule since the machine ran out of
// memory on 2026-09-26). Nothing waits for these: a slow or missing model is
// fewer claims proposed, never a worker that refuses to start.
var knowledge = string.Equals(builder.Configuration["Tendero:Knowledge"], "true",
    StringComparison.OrdinalIgnoreCase);

// The model, prepared once into its own volume: downloaded at the checked
// revision, converted and quantized with llama.cpp's own tools, and held to the
// SHA-256 P10-0 measured (scripts/prepare-extractor.sh). No maker publishes a
// GGUF of Qwen3.5, and ADR 0031 refuses a third party's. Conversion peaked at
// 4.0 GB and quantization at 3.2: the limit is the peak and a gigabyte, no more.
const string llamaCppRelease = "v0.5.0";

var extractorModel = builder.AddContainer("extractor-model", "ghcr.io/ggml-org/llama.cpp", "full-" + llamaCppRelease)
    .WithEntrypoint("sh")
    .WithArgs("/scripts/prepare-extractor.sh")
    .WithBindMount(Path.Combine(builder.AppHostDirectory, "scripts"), "/scripts", isReadOnly: true)
    .WithVolume("tendero-extractor-models", "/models")
    .WithContainerRuntimeArgs("--memory=5g", "--memory-swap=5g", Cpus(8));

// Qwen3.5-4B at 8 bits, the extractor P10-0 chose: precision 0.92, recall 0.80,
// ~30 s a product on the CPU — a worker's pace, never a request's. Served with
// the settings it was measured under, because the same file gave different
// claims under another configuration: one slot of 4,096 tokens, weights read
// into memory rather than mapped (so its limit is honest), thinking off. It held
// 5.3 GB; the limit is 6 — the minimum that fits, not a comfortable round number.
var extractor = builder.AddContainer("extractor", "ghcr.io/ggml-org/llama.cpp", "server-" + llamaCppRelease)
    .WithArgs("-m", "/models/qwen3.5-4b-q8_0.gguf", "--host", "0.0.0.0", "--port", "8080",
        "-c", "4096", "-np", "1", "-t", "12", "--jinja", "--load-mode", "none",
        "--chat-template-kwargs", "{\"enable_thinking\":false}")
    .WithHttpEndpoint(targetPort: 8080, port: extractorPort, name: "http")
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithVolume("tendero-extractor-models", "/models", isReadOnly: true)
    .WithContainerRuntimeArgs("--memory=6g", "--memory-swap=6g", Cpus(12))
    .WaitForCompletion(extractorModel);

// The support scorer: an NLI model on the model server the stack already runs.
// As good as the best generative yes/no on the forty support pairs (AUC 1.00,
// ECE 0.03), at 0.07 s: 2.8 GB loaded in the spike, 1.8 under load in the stack —
// limited to 3. Its batches are sized to the
// pairs it scores: TEI warming up for its defaults died at 4 GB twice.
const string supportModel = "MoritzLaurer/bge-m3-zeroshot-v2.0";
const string supportModelRevision = "9abf1c8aaeb82a2447809c20753ed0b106b76652";

var support = builder.AddContainer("support", "ghcr.io/huggingface/text-embeddings-inference", "cpu-1.9.3")
    .WithArgs("--model-id", supportModel, "--revision", supportModelRevision,
        "--max-batch-tokens", "1024", "--max-batch-requests", "1", "--auto-truncate", "--tokenization-workers", "2")
    .WithHttpEndpoint(targetPort: 80, port: supportPort, name: "http")
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithVolume("tendero-tei-models", "/data")
    .WithContainerRuntimeArgs("--memory=3g", "--memory-swap=3g", Cpus(8));

// STEWARD'S LOCAL MODEL, declared always and started on demand (phase 12).
//
//   Tendero__Steward=true dotnet run --project src/AppHost
//
// The extractor's GGUF from the extractor's volume — the same file, so nothing is
// downloaded twice — served by a container of its own: the extractor's one slot of
// 4,096 tokens is taken, and Steward's tool definitions alone are 1,000–1,500. Its
// window is 8,192 — measured in S-6: the copilot suite's largest call was 5,996
// tokens, 27 % under it. Over the suite's 24 turns the container peaked at 5.32 GiB;
// the limit is 6 GB (docs/analysis/steward-model-sizing.md).
//
// ON THE GPU WHEN THERE IS ONE, by the same switch as the embeddings
// (Tendero__Inference__Device): llama.cpp's CUDA 12 image and every layer offloaded.
// Measured on 2026-10-03 on the RTX 5070 Laptop, the same model, the same 24 cases:
// 22 of 24 on both, and a median turn of 1.9 s against 22.5 s on the CPU, the
// slowest 12 s against 186. It holds 4.8 GB of the card's 8 and 1.1 GiB of RAM. The
// `cuda13` image is not used: it asks for a driver reporting CUDA 13.4, and this
// machine's 610.88 does not.
var steward = string.Equals(builder.Configuration["Tendero:Steward"], "true",
    StringComparison.OrdinalIgnoreCase);

// TINO, THE STOREFRONT'S ASSISTANT (phase 12b), on the same container and the same
// model, by its own switch:
//
//   Tendero__Tino=true dotnet run --project src/AppHost
//
// ONLY ON A GPU. A shopper waits seconds: a turn has twenty, and on the CPU this
// model takes minutes (the measurement above), so every turn would end as "not
// answering" after making the shopper wait. Without a GPU the switch starts
// nothing and the API is told nothing, and the storefront says Tino is not
// available — the search is what there is (the Tino spec, *When no model answers*).
// It shares the one slot with Steward until a second is measured.
var tino = inference.UsesGpu && string.Equals(builder.Configuration["Tendero:Tino"], "true",
    StringComparison.OrdinalIgnoreCase);

// THE LOCAL MODEL'S EYES (phase 13a, I-2), by their own switch:
//
//   Tendero__Vision=true dotnet run --project src/AppHost
//
// Qwen3.5-4B reads pictures, and llama.cpp serves that half from a second file,
// the projector, which no maker publishes: `scripts/prepare-vision.sh` converts it
// from the same pinned revision and holds it to its SHA-256 (ADR 0031, amended
// 2026-10-06). The same container then serves text and pictures — two models do
// not fit the card — and the API is told the profile reads them.
//
// ONLY ON A GPU, as Tino: it was measured there (5.7 GB of the card's 8 against
// 4.8 without, 1.4 s a question about a picture) and not on a CPU, where a
// picture is about a thousand tokens of prompt before a word is asked. Without a
// GPU the switch starts nothing, the profile says it reads no pictures, and what
// needs one read says so and proposes nothing (the images spec, *Degraded path*).
var vision = inference.UsesGpu && string.Equals(builder.Configuration["Tendero:Vision"], "true",
    StringComparison.OrdinalIgnoreCase);
var localModel = steward || tino || vision;

// The projector, prepared once into the model's own volume. Its download is the
// whole revision (8.8 GB) for a 672 MB file, so it is its own step and only a
// stack that reads pictures takes it. The conversion peaked at 2.6 GB of its own memory (sampled; the page cache of an 8.8 GB download fills whatever the limit leaves); the limit is
// the peak and a gigabyte and a little, because a sample every two seconds can miss the top.
var visionProjector = builder.AddContainer("vision-projector", "ghcr.io/ggml-org/llama.cpp", "full-" + llamaCppRelease)
    .WithEntrypoint("sh")
    .WithArgs("/scripts/prepare-vision.sh")
    .WithBindMount(Path.Combine(builder.AppHostDirectory, "scripts"), "/scripts", isReadOnly: true)
    .WithVolume("tendero-extractor-models", "/models")
    .WithContainerRuntimeArgs("--memory=4g", "--memory-swap=4g", Cpus(8));

if (!vision)
    visionProjector.WithExplicitStart();

var stewardModel = builder.AddContainer("steward-model", "ghcr.io/ggml-org/llama.cpp",
        (inference.UsesGpu ? "server-cuda-" : "server-") + llamaCppRelease)
    .WithArgs("-m", "/models/qwen3.5-4b-q8_0.gguf", "--host", "0.0.0.0", "--port", "8080",
        "-c", "8192", "-np", "1", "-t", "12", "--jinja", "--load-mode", "none",
        // No prompt cache in RAM (S-6): llama.cpp keeps up to 8 GiB of past prompts by
        // default, and under this container's limit it grew turn after turn until the
        // kernel killed it — four of the copilot suite's cases in. One slot already
        // reuses the prefix every turn shares (the instructions and the tools).
        "--cache-ram", "0",
        "--chat-template-kwargs", "{\"enable_thinking\":false}")
    .WithHttpEndpoint(targetPort: 8080, port: stewardModelPort, name: "http")
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithVolume("tendero-extractor-models", "/models", isReadOnly: true)
    .WithContainerRuntimeArgs("--memory=6g", "--memory-swap=6g", Cpus(12))
    .WaitForCompletion(extractorModel);

if (inference.UsesGpu)
    stewardModel.WithArgs("-ngl", "99").WithContainerRuntimeArgs("--gpus", "all");

if (vision)
    stewardModel.WithArgs("--mmproj", "/models/qwen3.5-4b-mmproj-f16.gguf").WaitForCompletion(visionProjector);

if (!localModel)
    stewardModel.WithExplicitStart();

if (!knowledge)
    extractor.WithExplicitStart();

// The support scorer is the claims pipeline's and Tino's: an answer is held to its
// facts by it (the Tino spec, *Grounding*), so Tino's switch starts it too.
if (!knowledge && !tino)
    support.WithExplicitStart();

// The model is prepared for whichever of the two needs it: Steward's container
// waits for the same preparation, which is idempotent (it checks the SHA-256).
if (!knowledge && !localModel)
    extractorModel.WithExplicitStart();

// THE GRAFANA STACK, declared always and started on demand (phase 9b, ADR 0036).
//
//   Tendero__Observability=true dotnet run --project src/AppHost
//
// or the `https-observability` launch profile, which is the same switch as a
// choice in Visual Studio's drop-down. Two modes, and the Aspire dashboard is in
// both: by default every service exports to it, as always; with the switch on
// they export to this container's collector instead, which fans the telemetry
// out to Tempo, Prometheus and Loki AND back to the dashboard. The full mode
// adds a persistent view with exemplars and dashboards of its own; it never takes
// away the one a developer already has open.
//
// **A start-time switch, not a button**, for the reason the vector layer gave
// (ADR 0023): each service's OTLP endpoint is fixed when the AppHost starts it,
// so starting this container by hand from the dashboard redirects nothing. It
// shows Grafana over whatever reaches it, which with the switch off is nothing.
//
// One container, `grafana/otel-lgtm`, rather than five: the image is meant for
// development, its Prometheus already stores exemplars and native histograms,
// and its Grafana ships with the datasources linked — exemplar to trace, trace to
// logs, logs back to the trace. Its own code is Apache-2.0 and what runs inside
// it is AGPLv3 and unmodified: tier 2 (CLAUDE.md, *Licensing policy*). Pinned to
// the release the P9b-2 spike ran, whose collector is contrib 0.160.0.
//
// The data is a named volume, unlike Elasticsearch: the point of this mode is a
// view that outlives a restart, which the dashboard's memory does not.
var observability = string.Equals(builder.Configuration["Tendero:Observability"], "true",
    StringComparison.OrdinalIgnoreCase);

var grafanaStack = builder.AddContainer("observability", "grafana/otel-lgtm", "0.33.1")
    .WithBindMount(Path.Combine(builder.AppHostDirectory, "observability", "otelcol-config.yaml"),
        "/otel-lgtm/otelcol-config.yaml", isReadOnly: true)
    // The Shop pulse and whatever follows it: dashboards are files in the
    // repository, provisioned read-only, never state kept in the volume.
    .WithBindMount(Path.Combine(builder.AppHostDirectory, "observability", "dashboards.yaml"),
        "/otel-lgtm/grafana/conf/provisioning/dashboards/tendero.yaml", isReadOnly: true)
    .WithBindMount(Path.Combine(builder.AppHostDirectory, "observability", "dashboards"),
        "/otel-lgtm/tendero-dashboards", isReadOnly: true)
    .WithVolume("tendero-observability-data", "/data")
    .WithHttpEndpoint(targetPort: 3000, port: grafanaPort, name: "grafana")
    .WithEndpoint(targetPort: 4317, port: collectorGrpcPort, name: "otlp-grpc", scheme: "http")
    .WithHttpEndpoint(targetPort: 4318, port: collectorHttpPort, name: "otlp-http")
    .WithHttpEndpoint(targetPort: 8027, port: faroPort, name: "faro")
    .WithHttpHealthCheck("/api/health", endpointName: "grafana")
    // Nobody signs in to a laptop's Grafana: anonymous, and an admin, like the
    // committed Keycloak password — a development stack with a secret worth
    // keeping is a secret that should not be here.
    .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
    .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Admin")
    // A counter born with its first increments already in it — two orders in
    // one export interval — has no zero before it, and `increase()` over it
    // reads 0: the Shop pulse showed two orders placed and a conversion of 0.
    // With this, Prometheus writes a zero at each series' start time, which
    // OTLP carries on every cumulative point.
    .WithEnvironment("PROMETHEUS_EXTRA_ARGS", "--enable-feature=created-timestamp-zero-ingestion")
    // The dashboard's OTLP address and key, as Aspire hands them to anything it
    // exports for, then renamed for the collector's configuration: it reads
    // single values, and the key arrives as a `name=value` header list.
    .WithOtlpExporter()
    .WithEnvironment(AspireDashboardForCollector.Copy);

if (!observability)
    grafanaStack.WithExplicitStart();

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
    // A JVM sizes its heap from the memory it is shown (Keycloak's image sets
    // MaxRAMPercentage=70), so with no limit it read the host's 31 GB and grew to
    // 985 MiB measured on 2026-09-28. Keycloak's own sizing guide starts a
    // single node at 1,250 MB; this is that, with the heap following it down.
    .WithContainerRuntimeArgs("--memory=1280m", "--memory-swap=1280m", Cpus(2))
    // Named for the reason the Postgres volume is: an unnamed one is keyed on
    // this project's path and is left behind by a folder rename.
    .WithDataVolume("tendero-keycloak-data")
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

// THE AI STAGES REACH THE API ONLY WHERE THEIR CONTAINERS RUN, and there is no
// flag any more to say otherwise (ADR 0023, amended 2026-09-17): SearchVector met
// its removal condition and hybrid answers every search. What decides whether the
// vector side exists is whether the API is handed its addresses at all — the same
// `vectors` the containers' own start decision reads, so the two cannot disagree.
//
// Handed an address nothing answers, the API would register the real vector
// layer against containers that were never started, and every search would pay a
// failed call and count as degraded. Left out, the API gets `NoVectorLayer`,
// which hybrid skips as "not configured" (`SearchStageNotConfiguredException`)
// and answers with lexical, byte for byte — the path with no AI in it, which is
// exactly what a default F5 should be.
//
// Referenced and never waited for, where they are referenced: a vector layer that
// is started but slow is still a search that degrades to lexical, never a service
// that refuses to start (invariant 8).
if (vectors)
    api.WithReference(qdrant).WithReference(embeddingsEndpoint);

// THE PICTURES' VECTORS (phase 13a, P13-3). The model is not a container: SigLIP 2
// runs inside the API and the worker, from the folder our own export leaves
// outside git (`src/AppHost/scripts/prepare-siglip.py`, 1.5 GB). So there is no
// switch of its own and nothing to start — **the folder is the opt-in**: where
// the vector store runs and the export is whole, both processes are told where it
// is; where either is missing they are told nothing, get the named stand-in, and
// the shop has no visual search and says so. A folder half written is "missing":
// handing it over would be a model that fails on its first picture.
var pictureModels = Path.Combine(repositoryRoot, "models", "siglip2");
var pictures = vectors && new[] { "vision.onnx", "text.onnx", "tokenizer.model" }
    .All(file => File.Exists(Path.Combine(pictureModels, file)));

// And where the pictures are, they are a third leg of the search (the author's
// ruling, 2026-10-09, on the measurement in `docs/search-evaluation.md`): ten
// products from the pictures fused with the words and their meaning, +0.005 /
// +0.016 of NDCG@10 and +0.024 / +0.060 of recall@50, for 100 to 150 ms a
// search. Zero in the code, which is what a shop without the picture model
// runs and what the gate's floors are held against; ten here, where the model
// is already loaded for visual search.
if (pictures)
    api.WithEnvironment("Search__Images__Models", pictureModels)
        .WithEnvironment("Search__Hybrid__PictureDepth", "10");

// The reranker by the same rule, one step narrower: `rerank` is `vectors` on a
// GPU. It has no equivalent of the vector layer's read-time check before its
// first call, which is one more reason never to hand over an address nothing
// answers.
if (rerank)
    api.WithReference(rerankerEndpoint);

// Steward's local profile by the same rule: its address only where its container
// runs. Without it the API registers no model profile, and Steward has no model —
// S-3's "unavailable" path, never a call to a container nobody started.
if (localModel)
    api.WithEnvironment("Steward__Models__local__Endpoint",
        ReferenceExpression.Create($"{stewardModel.GetEndpoint("http")}/v1/"));

// And that it reads pictures only where it was started with its projector: a
// profile that says so and cannot is a call that fails; one that does not say so
// is a feature that takes its path without a model.
if (vision)
    api.WithEnvironment("Steward__Models__local__ReadsPictures", "true");

// Tino by the same rule, one step further: it is on only where its model runs, and
// off it does not answer and says so (GET /api/assistant/status).
if (tino)
    api.WithEnvironment("Tino__Enabled", "true").WithEnvironment("Tino__Model", "local")
        .WithReference(support.GetEndpoint("http"));

// A Scripted Steward (phase 12, S-6): CI's browser spec drives the panel, the card,
// the review and the audit row with a script, because no model fits a runner —
// `Tendero__StewardScript=<file>`, and the scripted profile answers instead.
if (builder.Configuration["Tendero:StewardScript"] is { Length: > 0 } stewardScript)
{
    api.WithEnvironment("Steward__Model", "scripted")
        .WithEnvironment("Steward__Models__scripted__Kind", "Scripted")
        .WithEnvironment("Steward__Models__scripted__Model", "scripted")
        .WithEnvironment("Steward__Models__scripted__Script", Path.GetFullPath(stewardScript, repositoryRoot));
}

// A Scripted Tino (phase 12b, T-6), for the storefront's browser spec in CI as
// Steward's is for the backoffice's: `Tendero__TinoScript=<file>`. No model and no
// scorer fit a runner, so the profile answers from the script and every sentence it
// writes is scored the same — what the spec proves is the wiring, on any machine.
if (builder.Configuration["Tendero:TinoScript"] is { Length: > 0 } tinoScript)
{
    api.WithEnvironment("Tino__Enabled", "true")
        .WithEnvironment("Tino__Model", "tino")
        .WithEnvironment("Steward__Models__tino__Kind", "Scripted")
        .WithEnvironment("Steward__Models__tino__Model", "scripted")
        .WithEnvironment("Steward__Models__tino__Script", Path.GetFullPath(tinoScript, repositoryRoot))
        .WithEnvironment("Support__Scripted", "0.95");
}

// THE SHOP'S EAR (phase 14, ADR 0061), by its own switch:
//
//   Tendero__Voice=true dotnet run --project src/AppHost
//
// whisper.cpp's server (v1.9.5, its own image pinned by commit) with Whisper
// large-v3-turbo and Silero VAD, both converted by us into `models/whisper`
// outside git (`scripts/prepare-whisper.sh`, 1.6 GB). The VAD is not optional:
// without it the model writes "Gracias." for silence. **The folder is the opt-in**
// as SigLIP's is: with the switch and both files, the API is told the engine and
// its address; with either missing it is told nothing, and the shop has no
// microphone and says nothing about one (invariant 8). Measured on 2026-10-16:
// 1.73 GiB after a question on the CPU; the limit is 3 GB for a thirty-second one.
// On the GPU when there is one, by the same switch as the rest of the inference.
var voiceModels = Path.Combine(repositoryRoot, "models", "whisper");
var voice = string.Equals(builder.Configuration["Tendero:Voice"], "true", StringComparison.OrdinalIgnoreCase)
    && new[] { "ggml-large-v3-turbo-f16.bin", "ggml-silero-v6.2.0.bin" }.All(file => File.Exists(Path.Combine(voiceModels, file)));
const string whisperCppCommit = "d1be6fde11ac6e0407606b4e42fe72d34add8037";
if (voice)
{
    var speechModel = builder.AddContainer("speech-model", "ghcr.io/ggml-org/whisper.cpp",
            (inference.UsesGpu ? "main-cuda-" : "main-") + whisperCppCommit)
        .WithArgs("whisper-server --model /models/ggml-large-v3-turbo-f16.bin --vad --vad-model /models/ggml-silero-v6.2.0.bin"
            + " --host 0.0.0.0 --port 8080 --threads 8")
        .WithHttpEndpoint(targetPort: 8080, port: 38090, name: "http")
        .WithHttpHealthCheck("/", endpointName: "http")
        .WithBindMount(voiceModels, "/models", isReadOnly: true)
        .WithContainerRuntimeArgs("--memory=3g", "--memory-swap=3g", Cpus(8));
    if (inference.UsesGpu)
        speechModel.WithContainerRuntimeArgs("--gpus", "all");

    api.WithEnvironment("Speech__Engine", "whisper-cpp")
        .WithEnvironment("Speech__WhisperCpp__Url", speechModel.GetEndpoint("http"))
        .WaitFor(speechModel);
}

// A scripted ear (phase 14), for the storefront's browser spec in CI as Tino's is:
// `Tendero__SpeechScript=<file>`. No speech model fits a runner, so the scripted
// engine answers every question from the script; what the spec proves is the
// wiring — the microphone, the WAV, the words landing unsent — on any machine.
if (builder.Configuration["Tendero:SpeechScript"] is { Length: > 0 } speechScript)
{
    api.WithEnvironment("Speech__Engine", "scripted")
        .WithEnvironment("Speech__Script", Path.GetFullPath(speechScript, repositoryRoot));
}

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

// `.WithNpm(install: false)`, and the numbers are the argument.
//
// Leaving `.WithNpm()` out is NOT enough: in Aspire.Hosting.JavaScript 13.5.3
// `AddViteApp` adds an installer resource by default, one per app. This line used
// to say "NOT `.WithNpm()`" and a fresh clone of the public repository still got
// `storefront-installer` and `backoffice-installer` running npm over the same
// folder at the same time — one finished, its dev server started, and the other
// was still rewriting `node_modules`, so `nx` was not there:
// `'nx' is not recognized as an internal or external command`.
//
// An installer reinstalls on every start, and with two apps over ONE `node_modules` — this
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
var storefront = builder.AddViteApp("storefront", frontend, "serve:storefront")
    .WithNpm(install: false)
    .WithHttpEndpoint(targetPort: 4200, port: 4200, name: "http", isProxied: false)
    // Referenced but NOT waited on. The dev server has nothing to ask the API at
    // boot — it serves a page that calls it afterwards — so waiting only couples
    // a four-second start to the backend's. The URL arrives through the
    // reference, which is what the proxy actually needs.
    .WithReference(api)
    .WithExternalHttpEndpoints();

// UCP's checkout links a person back to the storefront — an order's permalink,
// and the page an agent's checkout continues on when it needs a person (P11-1).
// An address, not a dependency: nothing waits on anything.
api.WithEnvironment("Ucp__StorefrontUrl", storefront.GetEndpoint("http"));

var backoffice = builder.AddViteApp("backoffice", frontend, "serve:backoffice")
    .WithNpm(install: false)
    .WithHttpEndpoint(targetPort: 4201, port: 4201, name: "http", isProxied: false)
    .WithReference(api)
    .WithExternalHttpEndpoints();

var workers = builder.AddProject<Projects.ElGuerre_Tendero_Workers>("workers")
    .WithReference(database).WaitFor(database)
    .WithEnvironment("ConnectionStrings__elasticsearch", elasticsearchEndpoint)
    // THIS one is a real dependency, and it was the one missing:
    // CreateSearchIndexesAtStartupWorker creates products_es/products_en during startup, so
    // if Elasticsearch is not ready the hosted service throws and the process
    // dies without retrying.
    .WithSeedFiles(repositoryRoot)
    .WaitFor(elasticsearch);

// The worker writes the vectors (P8-4), and only where the vector layer was
// started — the API's rule above, for the same reason: an address nothing answers
// would fail the vector projection on every product. Referenced, not waited for:
// a slow vector layer must not hold the outbox — and the lexical index behind it
// — hostage.
if (vectors)
    workers.WithReference(qdrant).WithReference(embeddingsEndpoint);

// And the pictures' (P13-3): the worker is the one that embeds the catalogue's,
// on the CPU and in the background.
if (pictures)
    workers.WithEnvironment("Search__Images__Models", pictureModels);

// The worker proposes claims (P10-4) and is handed the models only when the
// switch started them — the same rule: an address nothing answers would fail
// every product, where no address at all is the rules alone.
if (knowledge)
    workers.WithReference(extractor.GetEndpoint("http")).WithReference(support.GetEndpoint("http"));

// The full observability mode points both services at the collector, and only
// then — the vector layer's rule: an address is handed over exactly when the
// switch that starts its container is on. Aspire has already written the
// dashboard's address into these variables; this writes over it, and the
// collector sends everything on to the dashboard itself.
//
// **Not waited for**, on purpose: telemetry never holds the shop hostage. A
// collector that is slow to start costs the first few seconds of spans, which the
// OTLP exporter drops and retries, never a service that refuses to start.
if (observability)
{
    var collector = grafanaStack.GetEndpoint("otlp-http");
    api.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", collector);
    workers.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", collector);

    // The dev servers too: their proxy forwards the browser's `/otlp` to
    // whatever this variable names, so the browser reaches the collector
    // without ever knowing its address.
    storefront.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", collector);
    backoffice.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", collector);

    // And the Faro receiver, for the second browser adapter (P9b-8): only this
    // mode has one, so only this mode hands its address to the dev servers.
    var faroReceiver = grafanaStack.GetEndpoint("faro");
    storefront.WithEnvironment("FARO_ENDPOINT", faroReceiver);
    backoffice.WithEnvironment("FARO_ENDPOINT", faroReceiver);
}

// WHICH BROWSER TELEMETRY ADAPTER the pages load (ADR 0036): `otel` by default,
// `faro` for the second adapter — which reaches a receiver only in the full
// observability mode — or `noop`. The API answers it at /api/telemetry/config,
// so both apps agree without either being rebuilt; changing it is
// `Parameters:browser-telemetry` and a restart, which is how the declared
// comparison switches between its two runs (docs/analysis/otel-vs-faro.md).
//
// Read from configuration here rather than left to `AddParameter`: given a value,
// that overload makes it a CONSTANT, and `Parameters:browser-telemetry` on the
// command line was silently ignored — the first Faro run of the comparison
// loaded OTel.
var browserTelemetry = builder.AddParameter("browser-telemetry",
    builder.Configuration["Parameters:browser-telemetry"] ?? "otel");
api.WithEnvironment("Telemetry__Browser__Adapter", browserTelemetry);

// SIMULATED TRAFFIC (ADR 0037), listed and never started by itself.
//
// A shop with no customers gets simulated ones: people in headless browsers
// through the storefront, agents over MCP and UCP, from one declared model and
// one seed. Start it from the dashboard; stop it the same way. It is sized for
// plausibility, not load — a few sessions a minute at the simulated day's peak —
// and every request it makes says it is synthetic.
//
// The seed is a parameter with a fixed default, so a run is reproducible out of
// the box and another is one configuration value away
// (`Parameters:traffic-sim-seed`). A day lasts an hour of real time.
var trafficSeed = builder.AddParameter("traffic-sim-seed",
    builder.Configuration["Parameters:traffic-sim-seed"] ?? "20260925");

var trafficSim = builder.AddProject<Projects.ElGuerre_Tendero_TrafficSim>("traffic-sim")
    .WithReference(api)
    .WithReference(storefront.GetEndpoint("http"))
    .WithReference(backoffice.GetEndpoint("http"))
    .WithEnvironment("TrafficSim__Seed", trafficSeed)
    .WithEnvironment("TrafficSim__DayLength", "01:00:00")
    .WithExplicitStart();

if (observability)
    trafficSim.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", grafanaStack.GetEndpoint("otlp-http"));

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
/// Which Text Embeddings Inference image this machine can run, chosen at start-up.
///
/// TEI publishes one GPU image per NVIDIA architecture — a binary built for one
/// compute capability does not run on another — plus a CPU image. So the choice
/// needs two facts about THIS machine: whether a GPU is there (nvidia-smi answers
/// with its compute capability) and whether containers can reach it (Docker has
/// the nvidia runtime; a Linux box without the container toolkit has the GPU and
/// not the runtime). Either missing is the CPU image, never a container that
/// fails to start.
///
/// A GPU whose architecture TEI publishes no image for is also the CPU, with the
/// reason in the description, rather than a guess at the nearest image.
/// </summary>
internal sealed record InferenceHardware(string ImagePrefix, bool UsesGpu, string Description)
{
    /// <summary>
    /// Sized to this catalogue rather than to TEI's defaults: product texts are a
    /// few hundred tokens, and a 16,384-token batch reserved 12.6 GiB of RAM on the
    /// CPU for inputs that never arrive. Truncation is explicit so a text longer
    /// than a batch is cut, not refused.
    /// </summary>
    public static readonly string[] ServerArguments =
        ["--max-batch-tokens", "4096", "--auto-truncate", "--tokenization-workers", "2"];

    private static readonly InferenceHardware Cpu = new("cpu-", false, "cpu");

    public static InferenceHardware Detect(string? preference)
    {
        var wanted = string.IsNullOrWhiteSpace(preference) ? "auto" : preference.Trim().ToLowerInvariant();

        if (wanted == "cpu")
            return Cpu with { Description = "cpu (forced by Tendero:Inference:Device)" };

        if (wanted is not ("auto" or "gpu"))
            throw new InvalidOperationException(
                $"Tendero:Inference:Device is '{preference}'; it accepts auto, cpu or gpu.");

        var capability = Run("nvidia-smi", "--query-gpu=compute_cap --format=csv,noheader")?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        var refusal = capability is null
            ? "no NVIDIA GPU answered nvidia-smi"
            : ImagePrefixFor(capability) is null
                ? $"TEI publishes no image for compute capability {capability}"
                // Quoted, and the quotes are load-bearing: unquoted, the space
                // inside the template splits it in two, docker answers "'docker
                // info' accepts no arguments", and a machine with a working GPU
                // would have been sent to the CPU in silence.
                : Run("docker", "info --format \"{{json .Runtimes}}\"") is not { } runtimes || !runtimes.Contains("nvidia", StringComparison.Ordinal)
                    ? "Docker has no nvidia runtime, so containers cannot reach the GPU"
                    : null;

        if (refusal is null)
            return new InferenceHardware(ImagePrefixFor(capability!)!, true, $"gpu (compute capability {capability})");

        if (wanted == "gpu")
            throw new InvalidOperationException($"Tendero:Inference:Device is gpu, but {refusal}.");

        return Cpu with { Description = $"cpu ({refusal})" };
    }

    /// <summary>TEI's image prefix per compute capability, from its build matrix.</summary>
    internal static string? ImagePrefixFor(string computeCapability) => computeCapability switch
    {
        "7.5" => "turing-",
        "8.0" => "",
        "8.6" => "86-",
        "8.9" => "89-",
        "9.0" => "hopper-",
        "10.0" => "100-",
        "12.0" => "120-",
        _ => null
    };

    private static string? Run(string fileName, string arguments)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            return process.WaitForExit(TimeSpan.FromSeconds(10)) && process.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The tool is not installed: that is an answer, not a failure.
            return null;
        }
    }
}

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
        ("Pricing__Campaigns__FilePath", "campaigns.sample.json"),
        ("Kits__Seed__TemplatesPath", "kits.sample.json"),
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

/// <summary>
/// Hands the collector in the Grafana stack the Aspire dashboard's OTLP address
/// and API key, under names its configuration can read.
///
/// Aspire writes both for anything it exports for — `WithOtlpExporter()` on the
/// container — as `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_EXPORTER_OTLP_HEADERS`.
/// The collector's configuration reads one value per variable, and the key
/// arrives inside a `name=value` header list, so it is taken out of the list here
/// rather than parsed in YAML. The key changes on every AppHost run, which is why
/// it cannot be written into the file. The standard variables are then removed
/// from the container, so that nothing else inside it exports to the dashboard.
/// </summary>
internal static class AspireDashboardForCollector
{
    private const string ApiKeyHeader = "x-otlp-api-key";

    public static void Copy(EnvironmentCallbackContext context)
    {
        var environment = context.EnvironmentVariables;

        if (environment.TryGetValue("OTEL_EXPORTER_OTLP_ENDPOINT", out var endpoint))
            environment["ASPIRE_DASHBOARD_OTLP_ENDPOINT"] = endpoint;

        if (environment.TryGetValue("OTEL_EXPORTER_OTLP_HEADERS", out var headers) && headers is string list)
            environment["ASPIRE_DASHBOARD_OTLP_API_KEY"] = ApiKeyFrom(list);

        // And then every OTEL_ variable goes, because the container is not one
        // process. Grafana, Tempo, Loki, Prometheus and Pyroscope run beside the
        // collector and read the same standard variables: left in place, each of
        // them exported its own telemetry straight at the dashboard, which
        // rejected hundreds of requests a minute with "invalid wire type". Only
        // the collector talks to the dashboard, through the two names above.
        foreach (var name in environment.Keys.Where(key => key.StartsWith("OTEL_", StringComparison.Ordinal)).ToArray())
            environment.Remove(name);
    }

    internal static string ApiKeyFrom(string headerList) =>
        headerList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(header => header.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(pair => pair.Length == 2 && string.Equals(pair[0], ApiKeyHeader, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair[1])
            .FirstOrDefault() ?? "";
}
