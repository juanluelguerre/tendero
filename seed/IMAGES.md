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

**El catálogo son 100 productos desde 2026-09-04 y aquí hay seis imágenes.** No
es un olvido: una imagen que no se puede leer no aborta nada — el producto se
importa igual y la ficha muestra el tile de reserva, que en este repositorio es
la ruta por defecto desde el primer día. Las noventa y cuatro que faltan están
listadas, con su nombre y su descripción en los dos idiomas, en
[`IMAGES-TODO.md`](IMAGES-TODO.md).

| fichero | producto |
|---|---|
| `B073WXYZ01.png` | Zapatillas de running Pulse Runner |
| `B08JKLM202.png` | Mochila urbana CityPack 25L |
| `B09PQRS303.png` | Cafetera de goteo Aroma 12 tazas |
| `B07TUVW404.png` | Camiseta técnica de trail |
| `B06GHIJ505.png` | Lámpara de escritorio LED |
| `B05DEFG606.png` | Set de 3 sartenes antiadherentes |
