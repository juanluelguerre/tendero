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
| `B13BAG0711-BLACK.webp` | Alforja de bicicleta impermeable 20L, en negro: la imagen de su variante, hecha a partir de la oliva cambiando solo el color (`recolour`) |
| `B13BAG0711-NAVY_BLUE.webp` | La misma alforja en azul marino: la imagen de su tercera variante, hecha igual (`generate --variants-of`) |
| `B13BAG0711-BACK.webp` | La misma alforja vista por detrás (`view`): una imagen de la galería que no es de ninguna variante |
| `B13BAG0712.webp` | Organizadores de equipaje, juego de 3 |

## Las veintisiete últimas, aceptadas por el autor el 2026-10-09

Con ellas **los 120 productos tienen imagen**. Mismo modelo y mismo prompt:
28 llamadas en la tanda (una repetida por el propio generador, porque la tinta
llegaba a los cuatro bordes) y cuatro más para tres redibujos — unos **0,96 $**,
bajo el tope de 1,50 $ que se fijó para esta tanda. La hoja de contacto está en
`docs/blog/reports/2026-10-06-phase-13a-images/screens/28-the-last-twenty-seven-pictures.png`.

**Tres se redibujaron** antes de enseñarlas, cada una con una indicación:

| Producto | Qué salió | Indicación |
|---|---|---|
| `B15KIT1201` báscula | un botón con la palabra *TARE* | botones y pantalla sin letras ni números |
| `B14HOM0807` alfombrilla de baño | un cordón enrollado en un nudo | plana, rectangular, de algodón de rizo, vista desde arriba |
| `B15KIT1204` cuencos de acero | tres cuencos de colores | acero cepillado, gris plata, sin color |

**Lo que queda para mirar**, por el modelo (Qwen3.5-4B con su proyector: 20 de
27 pasan todas las comprobaciones) y a ojo:

| Producto | Aviso |
|---|---|
| `B14HOM0808` servilletas, juego de 4 | el modelo ve cuatro cojines: dobladas así lo parecen |
| `B14HOM0807` alfombrilla de baño | redibujada; el modelo ve una bandeja de huevos en el rizo |
| `B12LAM0608` bombillas, pack de 4 | el modelo cuenta cinco; a ojo son cuatro |
| `B12LAM0610` flexo con lupa | el modelo no ve la lupa ni el aro de luz; a ojo están |
| `B15SHO1104` alpargatas | el catálogo dice crema y se ven blancas |
| `B15KIT1201` báscula | lleva un cuenco encima, que el modelo cuenta como otra cosa |
| `B14ORG0903`, `B14ORG0904` | la falsa alarma de escena de siempre: lista las partes del propio objeto |
| `B12LAM0608`, `B12LAM0610`, `B14HOM0802`, `B15APP1001`, `B15APP1004`, `B15APP1005`, `B15APP1006`, `B15APP1008` | fuera de la zona segura: ocupan más del 75 % del alto, como la ropa y los muebles altos de la tanda anterior |

El tablero de defectos (`known-bad/baseline.json`) **se regrabó el 2026-10-09**,
ya aceptadas, sobre las 123 imágenes del catálogo (antes 96): unos 85 minutos
de GPU.

| Defecto | Imágenes | Lo ve su pregunta | No lo ve ninguna | Antes (de 96) |
|---|---|---|---|---|
| Rótulo | 123 | 121 (texto) | 1 | 94, 1 |
| Emblema | 123 | 116 (marca) | 4 | 91, 3 |
| Escena | 122 | 100 (escena); 110 el registro | 0 | 76; 87, 0 |
| Color | 32 | 21 | 11 | 16 de 21, 5 |
| Número de piezas | 25 | 18 | 6 | 17 de 20, 2 |
| Fondo | 122 | 119 (registro) | 3 | 93, 2 |

Falsas alarmas sobre las 122 imágenes tal cual: escena 22, color 7,
descripción 7, marca 3, texto 2, número 2 — las mismas proporciones que con
96, y con la misma salvedad: varias son dudas acertadas sobre imágenes
aceptadas con un defecto conocido.

**Lo que empeora es el color y el número.** De las once imágenes nuevas con
color declarado, la pregunta de color caza el cambio en cinco y ninguna
pregunta lo ve en seis; de las cinco nuevas con número de piezas, una. Son
ropa y menaje de esta tanda, y es donde hay que mirar antes de fiarse de
esas dos preguntas con el catálogo entero.

## Cuarenta imágenes más, aceptadas por el autor el 2026-10-08

Veinte de las cuarenta y siete que faltaban y las veinte de los productos nuevos
(`docs/specs/catalogue-growth.md`), dibujadas por el mismo modelo con el mismo
prompt: unos 1,23 $, con un redibujo. **Van 2,88 $ de los 3 € aprobados**; las
veintisiete que quedan y cualquier redibujo de estas necesitan subir el techo.

El pase de visión (`verify`, el 4B de la tienda) da 23 de 40 sin ningún aviso.
Lo que hay que mirar, con lo que dice el modelo y lo que se ve a ojo:

| Imagen | Aviso |
|---|---|
| `B10SHI0209` cortavientos plegable | dibujado como su bolsa: el modelo ve una mochila verde |
| `B11COO0302` cacerola de acero inoxidable | dibujada en azul |
| `B11COF0408` batidora | lleva texto bajo los mandos; fuera de la zona segura |
| `B11COF0405` molinillo | el modelo ve texto o un símbolo |
| `B11COO0309` cuchillo de chef | fuera de la zona segura por poco |
| `B12LAM0602` lámpara con carga | el modelo ve un símbolo impreso |
| `B16FUR1307` mesa auxiliar | un tambor negro, no una bandeja con patas: el modelo ve un carrete |
| `B16FUR1312` cómoda | cinco cajones donde dice cuatro, y un icono del anclaje a la pared |
| `B16PLA1401` monstera | tres iconos en la maceta |
| `B16DEC1502` alfombra | blanca con dibujo de ladrillo; el catálogo dice crema |
| `B11TAB0506`, `B12LAM0606` | el catálogo dice crema y se ven blancos |
| Muebles altos (`1301`–`1304`, `1309`, `1310`), `B16PLA1402` | fuera de la zona segura: ocupan más del 75 % del alto |

El resto de avisos del modelo son la falsa alarma de siempre de la pregunta de
escena, que lista las partes del propio objeto (la tapa de una cacerola, los
platos de unas macetas).

El tablero de defectos (`known-bad/baseline.json`) **se regrabó el 2026-10-08**,
ya aceptadas, sobre las 96 imágenes (antes 28), con Qwen3.5-4B y su proyector
de visión en la GPU. Cada imagen buena se estropea a propósito de cada manera y
se cuenta qué pregunta lo ve:

| Defecto | Imágenes | Lo ve su pregunta | No lo ve ninguna | Antes (de 28) |
|---|---|---|---|---|
| Rótulo | 96 | 94 (texto) | 1 | 28, 0 |
| Emblema | 96 | 91 (marca) | 3 | 27, 0 |
| Escena | 95 | 76 (escena); 87 el registro | 0 | 23 |
| Color | 21 | 16 | 5 | — |
| Número de piezas | 20 | 17 | 2 | 3 de 3, 0 |
| Fondo | 95 | 93 (registro) | 2 | — |

Falsas alarmas sobre las 95 imágenes tal cual: escena 19 (eran 5 de 28, la
misma proporción), color 6, descripción 4, marca 3, texto 2, número 1.

**Buena parte no son falsas.** De las seis de color, tres son las imágenes que
la tabla de arriba ya señala como blancas donde el catálogo dice crema
(`B11TAB0506`, `B12LAM0606`, `B16DEC1502`); las dos de texto (`B11COF0405`,
`B11COF0408`), dos de las tres de marca (`B12LAM0602`, `B16PLA1401`) y una de
descripción (`B16FUR1307`) también están en ella. Se aceptaron tal cual, así
que el tablero cuenta como falsa alarma una duda acertada: no sabe decir
«aceptada con un defecto conocido». Falsas de verdad quedan tres de color
(`B11TAB0501`, `B13BAG0702`, `B13BAG0710`), una de marca, tres de descripción,
una de número y las diecinueve de escena.

Con el triple de imágenes el tablero es más exigente, no peor: aparecen los
primeros fallos de texto y de marca (uno y tres), que con 28 imágenes no
cabían, y la pregunta de escena sigue siendo la que más duda de una imagen
buena. La herramienta avisa de que varias líneas quedan *por debajo del
tablero grabado*: compara cuentas de 96 con cuentas de 28, y por eso se
regraba y no se da por regresión.
