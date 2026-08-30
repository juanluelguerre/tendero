// Orquestación de Tendero en local: nada aquí requiere darse de alta en ningún
// sitio ni pagar nada (initial-plan §1).
//
// Sólo se declara lo que el código consume HOY. Qdrant, Ollama y Redis están en
// la arquitectura objetivo (docs/architecture.md) y entrarán en el mismo PR que
// traiga el worker de embeddings, en la fase 3: declararlos antes son cinco
// gigas de descarga en el primer arranque y recursos que nadie lee.

var builder = DistributedApplication.CreateBuilder(args);

// La raiz del repo, para las rutas de ficheros que la API necesita. Sin esto,
// "seed/products.sample.json" se resuelve contra el content root de la API
// (src/Api/) y la importacion falla con 500 en un repo recien clonado.
var repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));

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
    // Sin health check, WaitFor(elasticsearch) no tiene nada que esperar.
    .WithHttpHealthCheck("/_cluster/health", endpointName: "http")
    .WithLifetime(ContainerLifetime.Persistent);

var elasticsearchEndpoint = elasticsearch.GetEndpoint("http");

var api = builder.AddProject<Projects.Tendero_Api>("api")
    // Sin esto, WaitFor(api) espera para siempre: la API expone /health, pero
    // Aspire solo considera "healthy" lo que se le declara aqui. El worker se
    // quedo bloqueado en "Waiting" con 81 mensajes de outbox sin procesar.
    .WithHttpHealthCheck("/health")
    .WithReference(database).WaitFor(database)
    .WithEnvironment("ConnectionStrings__elasticsearch", elasticsearchEndpoint)
    .WithEnvironment(
        "Catalog__Connectors__Seed__FilePath",
        Path.Combine(repositoryRoot, "seed", "products.sample.json"))
    .WithExternalHttpEndpoints();

// Los dos Angular los levanta Aspire tambien: un solo comando arranca todo, y
// la URL de la API llega inyectada (services__api__http__0) en vez de escrita a
// mano en un environment.ts. El proxy del dev-server la lee de ahi.
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

builder.AddProject<Projects.Tendero_Workers>("workers")
    .WithReference(database).WaitFor(database)
    .WithEnvironment("ConnectionStrings__elasticsearch", elasticsearchEndpoint)
    // ESTA si es una dependencia real, y era la que faltaba: SearchIndexInitializer
    // crea products_es/products_en durante el arranque, asi que si Elasticsearch
    // no esta listo el hosted service lanza y el proceso muere sin reintentar.
    .WaitFor(elasticsearch);

// El worker NO espera a la API: no depende de ella. Lo tenia por ordenar el
// dashboard, y esa dependencia inventada lo dejaba bloqueado en "Waiting" con
// la outbox sin drenar. Una dependencia declarada que no existe es un deadlock
// esperando su turno.

builder.Build().Run();
