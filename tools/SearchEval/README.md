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
4. **Refresca** los índices. Elasticsearch tarda hasta un segundo en hacer
   visible lo indexado, y sin refrescar la nota depende de esa carrera.
5. Lanza cada consulta del golden set por `ILexicalProductSearch` y puntúa.

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

## La suite `copilot` (Steward, fase 12)

Steward contra `copilot/cases.json`: cada caso es una pregunta, la pantalla en
que se hace, las lecturas que debería hacer —con los argumentos que importan— y
las propuestas que debería guardar. **Sin modelo juez**: una propuesta es un
comando y se compara exacta. Mide herramienta correcta, argumentos válidos,
propuestas exactas, latencia p50/p95, tokens y € por vuelta, y **la llamada más
grande** (entrada y salida), que es lo que la ventana de contexto debe caber.

La vuelta es la misma que corre la API —el agente, las lecturas de cada
contexto, las propuestas en Assist— compuesta en este proceso sobre un
**Postgres desechable** que la suite migra y, si está vacío, llena con el
catálogo semilla y su stock:

```bash
docker run -d --name copilot-eval-pg --memory 768m --cpus 2 \
  -e POSTGRES_PASSWORD=eval -e POSTGRES_DB=copilot -p 39432:5432 postgres:17

# Contra el modelo local de Steward (Tendero__Steward=true lo levanta en :38085)
dotnet run --project tools/SearchEval -- --suite copilot \
  --database "Host=localhost;Port=39432;Database=copilot;Username=postgres;Password=eval" \
  --chat http://localhost:38085 --model Qwen/Qwen3.5-4B --report artifacts/copilot.json

# Ensayo de la propia suite, sin modelo: un guion que responde lo esperado
dotnet run --project tools/SearchEval -- --suite copilot --database "…" \
  --script tools/SearchEval/copilot/dry-run.json

docker rm -f copilot-eval-pg
```

Se **mide, no se compara con un umbral**: S-7 la pasa por cada perfil y el
autor elige el de por defecto con los números (`docs/analysis/steward-models.md`).
Un caso que nombra un producto que el laboratorio no tiene detiene la suite
antes de preguntar a ningún modelo: ese error sería de la suite, no del modelo.
