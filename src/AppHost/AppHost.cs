// Orquestación de Tendero en local: nada aquí requiere darse de alta en ningún
// sitio ni pagar nada (initial-plan §1).
//
// Sólo se declara lo que el código consume HOY. Qdrant, Ollama y Redis están en
// la arquitectura objetivo (docs/architecture.md) y entrarán en el mismo PR que
// traiga el worker de embeddings, en la fase 3: declararlos antes son cinco
// gigas de descarga en el primer arranque y recursos que nadie lee.

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();          // el catálogo importado sobrevive a un reinicio

var database = postgres.AddDatabase("tendero-db");

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
    .WithEnvironment("ConnectionStrings__elasticsearch", elasticsearchEndpoint)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.Tendero_Workers>("workers")
    .WithReference(database).WaitFor(database)
    .WithEnvironment("ConnectionStrings__elasticsearch", elasticsearchEndpoint)
    // El worker crea los índices y el esquema de desarrollo; que arranque
    // después de la API sólo evita ruido en el dashboard.
    .WaitFor(api);

builder.Build().Run();
