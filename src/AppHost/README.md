# AppHost

`dotnet run --project src/AppHost` levanta todo lo que la fase 1 necesita y
abre el dashboard de Aspire.

## Qué arranca hoy

| Recurso | Para qué |
|---|---|
| **Postgres** (`tendero-db`) | Fuente de verdad. Con volumen: el catálogo importado sobrevive a un reinicio. |
| **Elasticsearch** | Búsqueda léxica BM25, un índice por idioma. Contenedor persistente. |
| **api** | Endpoints de los slices (Carter). |
| **workers** | Procesador del outbox, creación de índices y, en Development, del esquema. |

El primer arranque descarga las dos imágenes; a partir de ahí es instantáneo.

## Qué NO arranca, y por qué

Qdrant, Ollama (con bge-m3) y Redis están en la arquitectura objetivo
(`docs/architecture.md`) pero **no se declaran aquí todavía**: ninguna línea de
código los lee. Declararlos añadía unos 5 GB al primer `dotnet run` y recursos
que arrancaban para no hacer nada.

Entran en el mismo PR que los consuma, en la fase 3 (worker de embeddings y
búsqueda híbrida). No hay flag que activarlos: un flag protege comportamiento
que existe, y hasta entonces no existe.

Tampoco hay pgAdmin ni Redis Commander. Para mirar la base:

```bash
docker exec -it <contenedor-postgres> psql -U postgres -d tendero
```

## Elasticsearch no es una integración de Aspire

Es un `AddContainer` normal a propósito: el paquete de hosting publicado fija el
cliente Elastic 8.x y chocaría con el 9.x que usa `ElGuerre.Tendero.Search`. Está
razonado en `docs/adr/0006-dependency-baseline.md`.
