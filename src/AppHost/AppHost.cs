// Orquestación completa de Tendero en local: nada aquí requiere darse de alta
// en ningún sitio ni pagar nada (initial-plan §1). Postgres es la fuente de
// verdad; Elasticsearch, Qdrant, Redis y Ollama son piezas reemplazables.

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()          // el catálogo importado sobrevive a un reinicio
    .WithPgAdmin();

var database = postgres.AddDatabase("tendero-db");

var redis = builder.AddRedis("redis")
    .WithRedisCommander();

var qdrant = builder.AddQdrant("qdrant")
    .WithDataVolume();

// Ollama sirve embeddings (bge-m3, multilingüe) y los LLM locales de la fase 2.
// Se descarga solo la primera vez; por eso el volumen no es opcional.
var ollama = builder.AddOllama("ollama")
    .WithDataVolume();

var embeddings = ollama.AddModel("embeddings", "bge-m3");

// Elasticsearch como recurso de contenedor: la integración de hosting publicada
// para Aspire arrastra el cliente 8.x y chocaría con el 9.x que usa Search
// (ver docs/adr/0006-dependency-baseline.md).
var elasticsearch = builder.AddContainer("elasticsearch", "docker.elastic.co/elasticsearch/elasticsearch", "9.1.0")
    .WithEnvironment("discovery.type", "single-node")
    .WithEnvironment("xpack.security.enabled", "false")
    .WithEnvironment("ES_JAVA_OPTS", "-Xms1g -Xmx1g")
    .WithHttpEndpoint(targetPort: 9200, name: "http")
    .WithLifetime(ContainerLifetime.Persistent);

var elasticsearchEndpoint = elasticsearch.GetEndpoint("http");

var api = builder.AddProject<Projects.Tendero_Api>("api")
    .WithReference(database).WaitFor(database)
    .WithReference(redis).WaitFor(redis)
    .WithEnvironment("ConnectionStrings__elasticsearch", elasticsearchEndpoint)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.Tendero_Workers>("workers")
    .WithReference(database).WaitFor(database)
    .WithReference(redis).WaitFor(redis)
    .WithReference(qdrant).WaitFor(qdrant)
    .WithReference(embeddings)
    .WithEnvironment("ConnectionStrings__elasticsearch", elasticsearchEndpoint)
    // El worker crea los índices y el esquema de desarrollo; que arranque
    // después de la API sólo evita ruido en el dashboard.
    .WaitFor(api);

builder.Build().Run();
