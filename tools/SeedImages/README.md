# SeedImages

Dibuja las ilustraciones de producto de `seed/images/`. Se ejecuta **en la
máquina de quien desarrolla**, una vez para las que faltan y ocasionalmente para
rehacer alguna. No entra en CI: no hay contenedor de servicio con GPU, y esto es
una herramienta de autor, como `dotnet-ef`.

## Estado

En construcción. Hoy hace dos cosas y ninguna dibuja nada:

```bash
# Los prompts de los que faltan, sin cargar ningún modelo. Instantáneo.
dotnet run --project tools/SeedImages -- dry-run --take 5
dotnet run --project tools/SeedImages -- dry-run --only B073WXYZ01

# Lo que los grafos ONNX declaran de verdad: nombres de tensores, tipos, formas.
dotnet run --project tools/SeedImages -- inspect --models <carpeta> --ep dml
```

`inspect` existe porque un export de SDXL son cinco grafos cuyos nombres de
entrada y salida **cambian entre exportadores**, y una tubería escrita contra los
equivocados no falla: produce ruido. Ejecutarlo contra un modelo nuevo cuesta dos
minutos y ahorra dos días.

## El modelo

No se descarga solo y no está en el repositorio: son casi 10 GB y `models/` está
en el `.gitignore`. Hace falta un export ONNX de **SDXL base 1.0 optimizado para
DirectML**, con la estructura de carpetas de diffusers:

```
<carpeta>/
  scheduler/scheduler_config.json
  tokenizer/{vocab.json, merges.txt, special_tokens_map.json}
  tokenizer_2/{...}
  text_encoder/model.onnx
  text_encoder_2/model.onnx (+ .data)
  unet/model.onnx (+ .data)
  vae_decoder/model.onnx
```

**Comprueba su licencia antes de descargarlo** (ADR 0006). El criterio no es el
de un paquete: los pesos no se distribuyen —corren una vez aquí— y lo que sí sale
son las cien imágenes, así que **la licencia se lee mirando qué dice sobre las
salidas**. `openrail++` renuncia expresamente a derechos sobre ellas y entra;
un modelo no comercial no, porque esa restricción alcanza a los ficheros que
acaban en el repositorio.

## Por qué DirectML y no CUDA

Porque en una GPU Blackwell —la serie RTX 50— **el proveedor CUDA de ONNX Runtime
no trae kernels compilados**. El issue [26177] de onnxruntime está cerrado y su
solución es compilar ONNX Runtime desde fuente con
`CMAKE_CUDA_ARCHITECTURES=120`, parcheando dos cabeceras por el camino. El export
que publica el equipo de ONNX Runtime para CUDA lo dice además en su propia
ficha: *"It cannot run in other execution providers like CPU or DirectML"*.

DirectML acelera sobre cualquier GPU con DX12, no necesita instalar nada y no
sabe qué es una arquitectura CUDA. Es entre 1,5 y 2,5 veces más lento, y más
lento gana a CPU.

**CUDA queda apuntado como opcional para el futuro.** La cuenta que lo difiere:
noventa y dos imágenes son entre hora y media y tres horas desatendidas con
DirectML, y entre media y una con CUDA. Ahorrar una hora de una tarea que corre
sola no paga un build desde fuente — es la regla de escala de `CLAUDE.md`, con su
número apuntado. Si algún día se hace, el mismo `inspect --ep cuda` dice en dos
minutos si ata.

## Los prompts

Se construyen desde el catálogo, no desde la lista en prosa de
`seed/IMAGES-TODO.md`: los dos dicen lo mismo hoy y el JSON es el que importa la
tienda. El color sale de `attributes["color"]` —una etiqueta en español— y se
traduce con las opciones de `seed/attributes.sample.json`, que es donde ya vive
la pareja es/en.

**Treinta y cinco productos no declaran color**, y ése es el caso que se olvida:
producen una línea sin cláusula de color en lugar de un `The object is :` cojo.
Un test lo comprueba, y otro comprueba que el bloque de estilo del código sigue
publicado en `IMAGES-TODO.md`.
