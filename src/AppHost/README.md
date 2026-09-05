# AppHost

`dotnet run --project src/AppHost` levanta toda la pila y abre el dashboard de
Aspire en http://localhost:15130.

## Qué arranca hoy

| Recurso | Puerto | Para qué |
|---|---|---|
| **Postgres 18** (`tendero-db`) | 55432 | Fuente de verdad. Con volumen: el catálogo importado sobrevive a un reinicio. |
| **Elasticsearch 9.5.0** | 59200 | Búsqueda léxica BM25, un índice por idioma. Contenedor persistente y **sin volumen** a propósito: el índice es una proyección que `POST /api/search/reindex` reconstruye (ADR 0012). |
| **api** | 5130 | Los endpoints de los slices (Carter), el documento OpenAPI y, en Development, el emisor OIDC de desarrollo bajo `/dev-issuer`. |
| **workers** | — | Procesador del outbox, creación de índices, migraciones. |
| **storefront** · **backoffice** | 4200 · 4201 | Las dos apps Angular, servidas por Vite con la URL de la API inyectada: el proxy de desarrollo es el único sitio con un `localhost` de reserva. |

Y tres recursos **declarados siempre y arrancados a demanda** (`WithExplicitStart`,
botón *Start* en el dashboard), porque un recurso creado sólo detrás de una
variable de entorno es invisible para quien lanza desde Visual Studio o Rider:

| Recurso | Puerto | Para qué |
|---|---|---|
| **Keycloak** | 58443 | El segundo emisor OIDC, con el realm de `keycloak/` importado. La API lo usa cuando `Authentication:Authority` apunta a él — dos variables de entorno, ningún flag de compilación (`P7-8`). Con volumen y persistente. |
| **pgweb** | 55433 | Mirar la base de datos sin un gigabyte de consola. |
| **elasticvue** | 59201 | Mirar los índices. Corre en el navegador y habla con Elasticsearch directamente, así que el CORS de Elasticsearch nombra su origen y no `*`. |

Todos los puertos fijos están **fuera de su valor por defecto** a propósito: 5432
y 9200 son los servicios de trabajo de alguien. Y todos los contenedores llevan
la etiqueta de proyecto de Compose, para que Docker Desktop los agrupe bajo
`tendero` en vez de listar ocho contenedores sueltos.

El primer arranque descarga las imágenes (unos 2,5 GB entre Postgres y
Elasticsearch); a partir de ahí es instantáneo.

## Qué NO arranca, y por qué

Qdrant, Ollama (con bge-m3) y la pila de Grafana están en la arquitectura
objetivo (`docs/architecture.md`) pero **no se declaran aquí todavía**: ninguna
línea de código los lee. Entran en el mismo PR que los consuma — la fase 8
(embeddings y búsqueda híbrida) y la fase 12 (observabilidad). No hay flag para
activarlos: un flag protege comportamiento que existe, y hasta entonces no
existe (artículo 07).

## Elasticsearch no es una integración de Aspire

Es un `AddContainer` normal a propósito: el paquete de hosting publicado no
declara licencia y fija el cliente Elastic 8.x, que chocaría con el 9.x que usa
`ElGuerre.Tendero.Search`. Keycloak, en cambio, sí es la integración oficial:
pasó las dos comprobaciones que aquella no pasó (`P7-5`). Está razonado en
`docs/adr/0006-dependency-baseline.md`.
