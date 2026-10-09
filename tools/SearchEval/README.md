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

Steward contra `Copilot/cases.json`: cada caso es una pregunta, la pantalla en
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
  --script tools/SearchEval/Copilot/dry-run.json

docker rm -f copilot-eval-pg
```

Se **mide, no se compara con un umbral**: S-7 la pasa por cada perfil y el
autor elige el de por defecto con los números (`docs/analysis/steward-models.md`).
Un caso que nombra un producto que el laboratorio no tiene detiene la suite
antes de preguntar a ningún modelo: ese error sería de la suite, no del modelo.

## La suite `assistant` (Tino, fase 12b)

Tino contra `Assistant/cases.json`: cada caso es una pregunta, quién la hace
(nadie, o uno de los compradores del laboratorio), la página desde la que se hace
(un producto, un pedido) y lo que una respuesta correcta tiene. **Sin modelo
juez**: se compara el tipo de respuesta, las lecturas hechas, los productos por su
código, **los datos citados** —el texto de la tienda, no las palabras del modelo—,
el pedido por su id y las palabras que ninguna frase puede llevar.

El turno es el que corre la API, compuesto en este proceso sobre un laboratorio
que la suite prepara: importa el catálogo semilla y lo **publica**, lo indexa en
un Elasticsearch para la búsqueda de la tienda, graba las descripciones y aprueba
los claims que las reglas proponen con soporte suficiente, le pone una instrucción
a la descripción de un producto y coloca cuatro pedidos de tres personas. Necesita
cuatro contenedores, todos desechables:

```bash
L="--label com.docker.compose.project=tendero"
docker run -d --name tino-eval-pg $L --memory 768m --cpus 2 \
  -e POSTGRES_PASSWORD=eval -e POSTGRES_DB=tino -p 39433:5432 postgres:17
docker run -d --name tino-eval-es $L --memory 2g --cpus 2 -p 39211:9200 \
  -e discovery.type=single-node -e xpack.security.enabled=false -e "ES_JAVA_OPTS=-Xms1g -Xmx1g" \
  docker.elastic.co/elasticsearch/elasticsearch:9.5.0
docker run -d --name tino-eval-nli $L --memory 3g --memory-swap 3g --cpus 8 -p 39484:80 \
  -v tendero-tei-models:/data ghcr.io/huggingface/text-embeddings-inference:cpu-1.9.3 \
  --model-id MoritzLaurer/bge-m3-zeroshot-v2.0 --revision 9abf1c8aaeb82a2447809c20753ed0b106b76652 \
  --max-batch-tokens 1024 --max-batch-requests 1 --auto-truncate --tokenization-workers 2
docker run -d --name tino-eval-model $L --gpus all --memory 6g --memory-swap 6g --cpus 12 -p 39480:8080 \
  -v tendero-extractor-models:/models:ro ghcr.io/ggml-org/llama.cpp:server-cuda-v0.5.0 \
  -m /models/qwen3.5-4b-q8_0.gguf --host 0.0.0.0 --port 8080 -c 8192 -np 1 -t 12 --jinja \
  --load-mode none --cache-ram 0 --chat-template-kwargs '{"enable_thinking":false}' -ngl 99

dotnet run --project tools/SearchEval -- --suite assistant \
  --database "Host=localhost;Port=39433;Database=tino;Username=postgres;Password=eval" \
  --elasticsearch http://localhost:39211 --tei http://localhost:39484 \
  --chat http://localhost:39480 --model Qwen/Qwen3.5-4B --report artifacts/assistant.json
# --only pan,order   solo los casos cuyo id contiene alguna de esas palabras

docker rm -f tino-eval-pg tino-eval-es tino-eval-nli tino-eval-model
```

En Git Bash, `MSYS_NO_PATHCONV=1` delante de los `docker run` que montan
`/models` o `/data`: sin él la ruta se reescribe y el modelo no se encuentra.

**El Elasticsearch es solo el suyo**: la suite recrea los índices al empezar.
Nunca el del stack ni otro con datos. Se **mide, no se compara con un umbral**:
los números están en `docs/analysis/tino-models.md`, y lo que varía de una pasada
a otra con el mismo modelo, también. Un caso que nombra un producto, un pedido,
una lectura o un comprador que el laboratorio no tiene detiene la suite antes de
preguntar a ningún modelo.

## La suite `implicit-ave` (fase 13a)

El modelo de la tienda contra un examen ajeno: los 1.610 ejemplos de test de
ImplicitAVE (foto real del producto, título, un atributo y el valor que anotó
una persona). Pregunta cerrada, respuesta sujeta por esquema a las opciones del
atributo, comparación exacta.

```bash
dotnet run --project tools/SearchEval -- --suite implicit-ave \
  --dataset <carpeta data/evaluation_data de ImplicitAVE> \
  --chat http://localhost:38085 --model qwen3.5-4b-q8_0 \
  [--modality both|image|text] [--format plain|json] [--take 80] [--report informe.json]
```

`--format plain` (por defecto) pregunta con `ClosedQuestion`: el modelo escribe
una de las opciones como texto y la respuesta trae la probabilidad que le dio,
así que el informe incluye qué se aceptaría a cada umbral de confianza y cuánto
de eso acierta. `--format json` sujeta la respuesta con un esquema, como se midió
la primera vez: 68,0 % frente a 80,5 % con solo la imagen.

**Los datos no están en el repositorio ni deben estarlo**: su repositorio no
declara licencia y derivan de MAVE y de datos de producto de Amazon. Se
descargan fuera del árbol. Es una medida, no una puerta: no tiene umbral y CI no
la ejecuta. Resultados y lectura en `docs/specs/images.md` (I-4b).

## Las imágenes, medidas solas (`--siglip`, fase 13a)

Con `--strategy hybrid --siglip <carpeta>` la suite `search` añade una cuarta
estrategia, `pictures`, **de diagnóstico y sin umbral**: la consulta se embebe
como texto con SigLIP 2 (nuestra exportación, `models/siglip2`, fuera de git) y
se ordena por las imágenes del catálogo más cercanas, cada producto una vez. Usa
el mismo Qdrant que los vectores de las palabras, en una colección propia.

```bash
L="--label com.docker.compose.project=tendero"
docker start tendero-eval-es                       # el Elasticsearch desechable, :39210
docker run -d --name tendero-eval-qdrant $L --memory 1g --cpus 2 \
  -p 39334:6334 -p 39333:6333 -e QDRANT__TELEMETRY_DISABLED=true qdrant/qdrant:v1.18.0
docker run -d --name tendero-eval-tei $L --gpus all --memory 4g --memory-swap 4g --cpus 4 \
  -p 39380:80 -v tendero-tei-models:/data \
  ghcr.io/huggingface/text-embeddings-inference:120-sha-4150561 \
  --model-id BAAI/bge-m3 --revision 5617a9f61b028005a4858fdac845db406aefb181

dotnet run --project tools/SearchEval -- --strategy hybrid \
  --elasticsearch http://localhost:39210 --qdrant http://localhost:39334 \
  --embeddings http://localhost:39380 --siglip models/siglip2 --report artifacts/pictures-eval.md
```

Además del informe de siempre escribe **qué separaría un umbral de parecido**:
lo cerca que queda la imagen más cercana cuando la respuesta existe y cuando no
(para una imagen y para palabras), y lo que cuesta embeber una imagen y un
texto en la CPU. No elige el umbral: lo imprime para que se elija.

Tarda unos tres minutos — embebe las imágenes de la semilla dos veces y cada
consulta una — y por eso esta ejecución tiene veinte minutos de presupuesto en
vez de cinco. Sin reranker (`--reranker`) la fila de `hybrid` no es comparable
con sus umbrales, que se fijaron con él: esta ejecución no es la puerta. Lectura
en `docs/search-evaluation.md`, *Words against the pictures, alone*.
