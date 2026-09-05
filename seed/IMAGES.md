# Imágenes del catálogo de muestra

Estas imágenes **no son de Tendero**: son del **proveedor de mentira**. El
conector `seed` simula un origen de catálogo externo igual que Shopify o
PrestaShop; un proveedor real tiene sus fotos en sus servidores, y el nuestro las
tiene aquí — igual que tiene sus nombres y precios en `products.sample.json`.

La importación las **ingiere** exactamente igual que ingeriría las de Shopify o
las del dataset completo de Amazon Berkeley Objects: se leen, se guardan en el
almacén de imágenes bajo el hash de su contenido, y el producto guarda esa
clave. Ver `docs/adr/0011-product-images.md`.

## Origen y licencia

Ilustraciones propias, generadas para este repositorio. **Sin dependencias de
terceros y sin restricciones de licencia**: se distribuyen bajo la misma
licencia que el proyecto.

Se intentó primero usar fotografía libre (Openverse, Wikimedia Commons, CC0 y
CC BY). La licencia era verificable por API, pero los bancos libres tienen
fotografía **de contexto**, no **de producto**: la búsqueda de "running shoes"
devolvía unas zapatillas colgando de un cable de la luz. Dos de seis resultados
eran usables.

Son marcas deliberadas, no intentos fallidos de foto: los productos de la
muestra son inventados ("Pulse Runner", "CityPack"), así que una foto real de la
zapatilla de otra marca haciéndola pasar por "Pulse Runner" sería *menos*
honesta que una marca que no finge ser nada.

Colores tomados de `design/tokens.css`. Cuando entre el dataset real de ABO,
estas se quedan como muestra de arranque sin descarga.

**El catálogo son 100 productos desde 2026-09-04 y aquí hay 8 imágenes.**
No es un olvido: una imagen que no se puede leer no aborta nada — el producto se
importa igual y la ficha muestra el tile de reserva, que en este repositorio es
la ruta por defecto desde el primer día. El seed ya apunta a
`images/<item_id>.webp` para los cien, así que una foto nueva es un fichero y
nada más. Las que faltan están listadas, con su nombre y su descripción en los
dos idiomas y la especificación (1400 × 1400, WebP, ≤ 120 KB), en
[`IMAGES-TODO.md`](IMAGES-TODO.md).

| fichero | producto |
|---|---|
| `B13BAG0703.webp` | Mochila de viaje Trayecto 40L |
| `B13BAG0706.webp` | Bolsa de deporte Vestuario 35L |
| `B13BAG0707.webp` | Bolsa de lona Mercado |
| `B13BAG0708.webp` | Rinonera Cinturon 3L |
| `B13BAG0709.webp` | Neceser de viaje Aseo |
| `B13BAG0710.webp` | Funda para portatil de 14 pulgadas |
| `B13BAG0711.webp` | Alforja de bicicleta impermeable 20L |
| `B13BAG0712.webp` | Organizadores de equipaje, juego de 3 |
