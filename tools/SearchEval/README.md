# SearchEval

La puerta de calidad de la búsqueda: corre las consultas anotadas contra un
Elasticsearch real, por los **mismos puertos que usa la aplicación**, y compara
NDCG@10 y recall@50 con los umbrales comprometidos.

```bash
# Informe contra un Elasticsearch ya levantado
dotnet run --project tools/SearchEval -- --elasticsearch http://localhost:9200

# Modo puerta: sale con código 1 si alguna cultura cae bajo su umbral
dotnet run --project tools/SearchEval -- --ci --report artifacts/relevance.md
```

## Cómo funciona

1. **Borra** `products_es` y `products_en`. Sin esto la medida no es
   reproducible: los `ProductId` son GUID v7 nuevos en cada ejecución, así que
   los documentos de la pasada anterior se quedarían compitiendo en el ranking.
2. Los recrea con sus analizadores (`SearchIndexInitializer`).
3. Lee el catálogo semilla por `ICatalogSourceConnector`, construye los
   agregados en memoria, los publica y los indexa por `IProductIndexer`.
   **No toca Postgres**: lo que se mide es relevancia, y meter la persistencia
   por medio sólo añade formas de fallar que no son la que se está midiendo.
4. Lanza cada consulta del golden set por `ILexicalProductSearch` y puntúa.

## El golden set

`golden/{es,en}.json`. Las anotaciones se identifican por **`externalId`**
(el `item_id` del origen, p. ej. `B073WXYZ01`) y no por el id interno: ese es un
GUID v7 que se regenera en cada importación, así que un golden set que lo usara
caducaría solo. El del origen es estable y se lee en un PR.

El campo `why` es obligatorio de facto: sin él nadie puede revisar si la nota
está bien puesta, y la puerta acaba midiendo los prejuicios de quien anotó.

## Umbrales

`eval.thresholds.json` — **medidos, no inventados**, con ~1% de margen sobre la
línea base real. Bajarlos exige justificarlo en el cuerpo del PR.
