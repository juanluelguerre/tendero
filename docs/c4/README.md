# Tendero in C4

> **Generated — do not edit.** Drawn from [`tendero.c4`](tendero.c4) by
> `node tools/c4.mjs`, which also fails when the model's containers and the
> AppHost's resources disagree (ADR 0035).

Levels 1 and 2 of the [C4 model](https://c4model.com): who uses Tendero, and
the processes and stores it runs on. Level 4 is the domain model, generated
from the code in [`../domain-model.md`](../domain-model.md). Containers tagged
`optional` start only when their capability is configured; the UCP agent is
tagged `planned` for phase 11.

```mermaid
---
title: "Tendero · system context"
---
graph TB
  Shopper@{ icon: "fa:user", shape: rounded, label: "Shopper" }
  Shopkeeper@{ icon: "fa:user", shape: rounded, label: "Shopkeeper" }
  BrowserAgent@{ icon: "fa:user", shape: rounded, label: "Agent in the shopper's tab" }
  McpClient@{ icon: "fa:user", shape: rounded, label: "External MCP client" }
  UcpAgent@{ icon: "fa:user", shape: rounded, label: "UCP agent" }
  Tendero@{ shape: rectangle, label: "Tendero" }
  Identity@{ shape: rectangle, label: "Identity provider" }
  Payments@{ shape: rectangle, label: "Payment provider" }
  Shopper -. "`shops with`" .-> Tendero
  Shopkeeper -. "`runs the shop with`" .-> Tendero
  BrowserAgent -. "`calls tools in`" .-> Tendero
  McpClient -. "`reads the catalogue over /mcp`" .-> Tendero
  UcpAgent -. "`will check out over UCP`" .-> Tendero
  Tendero -. "`validates tokens (JWKS)`" .-> Identity
  Tendero -. "`authorises, captures, refunds`" .-> Payments
```

```mermaid
---
title: "Tendero · containers"
---
graph TB
  Shopper@{ icon: "fa:user", shape: rounded, label: "Shopper" }
  Shopkeeper@{ icon: "fa:user", shape: rounded, label: "Shopkeeper" }
  BrowserAgent@{ icon: "fa:user", shape: rounded, label: "Agent in the shopper's tab" }
  McpClient@{ icon: "fa:user", shape: rounded, label: "External MCP client" }
  UcpAgent@{ icon: "fa:user", shape: rounded, label: "UCP agent" }
  subgraph Tendero["`Tendero`"]
    Tendero.Storefront@{ shape: rounded, label: "storefront" }
    Tendero.Backoffice@{ shape: rounded, label: "backoffice" }
    Tendero.Workers@{ shape: rectangle, label: "workers" }
    Tendero.Api@{ shape: rectangle, label: "api" }
    Tendero.Postgres@{ shape: disk, label: "postgres" }
    Tendero.Elasticsearch@{ shape: disk, label: "elasticsearch" }
    Tendero.Qdrant@{ shape: disk, label: "qdrant" }
    Tendero.Embeddings@{ shape: rectangle, label: "embeddings" }
    Tendero.Reranker@{ shape: rectangle, label: "reranker" }
  end
  Identity@{ shape: rectangle, label: "Identity provider" }
  Payments@{ shape: rectangle, label: "Payment provider" }
  Shopper -. "`shops with`" .-> Tendero.Storefront
  Shopkeeper -. "`runs the shop with`" .-> Tendero.Backoffice
  BrowserAgent -. "`calls tools in`" .-> Tendero.Storefront
  McpClient -. "`reads the catalogue over /mcp`" .-> Tendero.Api
  UcpAgent -. "`will check out over UCP`" .-> Tendero.Api
  Tendero.Storefront -. "`HTTP`" .-> Tendero.Api
  Tendero.Backoffice -. "`HTTP`" .-> Tendero.Api
  Tendero.Api -. "`reads and writes`" .-> Tendero.Postgres
  Tendero.Api -. "`searches`" .-> Tendero.Elasticsearch
  Tendero.Api -. "`nearest neighbours`" .-> Tendero.Qdrant
  Tendero.Api -. "`embeds the query`" .-> Tendero.Embeddings
  Tendero.Api -. "`reranks the head`" .-> Tendero.Reranker
  Tendero.Workers -. "`drains the outbox`" .-> Tendero.Postgres
  Tendero.Workers -. "`projects products`" .-> Tendero.Elasticsearch
  Tendero.Workers -. "`projects vectors`" .-> Tendero.Qdrant
  Tendero.Workers -. "`embeds products`" .-> Tendero.Embeddings
  Tendero.Api -. "`validates tokens (JWKS)`" .-> Identity
  Tendero.Api -. "`authorises, captures, refunds`" .-> Payments
```
