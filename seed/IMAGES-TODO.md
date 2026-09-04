# Imágenes por generar

Los 100 productos del catálogo de muestra, con el nombre y la descripción de
cada uno para poder generar su imagen. **Sólo seis existen ya** — las de
`seed/images/` — y están marcadas.

## La especificación, en corto

| | |
|---|---|
| **Tamaño** | **1400 × 1400 px**, cuadrado |
| **Formato** | **WebP**, calidad 82 · ilustración de línea |
| **Peso** | **≤ 120 KB** por imagen (la media real ronda 45 KB) |
| **Color** | sRGB, **sin canal alfa** — el fondo va pintado |
| **Color del producto** | el que diga su atributo en el catálogo, no el que salga |
| **Fondo** | plano, `#FAF9F7` o `#F3F1ED` (los de `design/tokens.css`) |
| **Zona segura** | el producto dentro del **75 % central en vertical** |
| **Texto** | ninguno, en ninguna parte de la imagen |
| **Nombre** | `<item_id>.<extensión>`, y la extensión ha de coincidir con `seed/products.sample.json` |

Todo lo de abajo es de dónde sale cada número, porque dentro de un año habrá que
volver a decidirlo y conviene no tener que medirlo otra vez.

### Por qué 1400 px

Porque **los derivados están diferidos** (`docs/initial-plan.md`): una tienda de
verdad genera miniaturas por tamaño y Tendero sirve el original. Así que este
único fichero se usa en todas partes, y tiene que dar la talla en el sitio más
exigente, no en el promedio.

Ese sitio es la ficha de producto. El marco de la aplicación mide 1440 px
(`--container-app`) y la ficha lo parte en dos columnas, así que la imagen
principal ocupa unos **660 px de CSS**. En una pantalla a 2× eso son **1320
píxeles reales**. 1400 los cubre con un margen pequeño.

Las seis que ya existen son de **640 × 640**, es decir que en la ficha se están
escalando hacia arriba. Se ve. No las voy a regenerar por eso —cumplen su papel
de muestra de arranque— pero las nuevas no deberían nacer con el mismo problema.

### Por qué el 75 % central

**Porque la tarjeta de resultados recorta.** La ficha usa la imagen en 1:1, pero
la tarjeta la mete en una caja **4:3 con `object-fit: cover`**: se escala al
ancho y se corta el alto. De una imagen cuadrada desaparece el **25 %** — un
12,5 % por arriba y otro tanto por abajo.

O sea que lo que toque el borde superior o inferior no se verá en el buscador,
que es donde más se mira. Deja aire: el producto centrado, ocupando como mucho
tres cuartos de la altura.

### Por qué ilustración y no fotografía

**Es la decisión importante de este fichero**, y de ella salen el formato y el
peso, así que va primero.

Tres razones, en orden de peso:

1. **Ya hay seis y son ilustración.** Cien imágenes con el mismo registro parecen
   un catálogo; seis ilustraciones y noventa y cuatro fotos parecen un accidente.
2. **Los productos son inventados**, y `seed/IMAGES.md` ya cerró este argumento:
   una fotografía que finge ser una «Pulse Runner» que no existe es *menos*
   honesta que un dibujo que no finge ser nada.
3. **La consistencia se sostiene sola.** Sobre cien imágenes, una fotografía
   generada deriva —la luz, el ángulo, la sombra, el punto de vista— y hay que
   volver a generar la mitad. Una ilustración plana con fondo liso sale igual la
   primera y la centésima.

### Por qué WebP, medido y no supuesto

**Esta sección decía PNG y estaba equivocada.** El razonamiento era que la
ilustración plana es lo que un PNG comprime bien, y es cierto — para ilustración
*de verdad* plana, de pocos colores y bordes duros. Las que genera un modelo no
lo son: llevan textura, sombra suave y un grano fino que PNG tiene que codificar
píxel a píxel.

Medido sobre las cinco primeras, convirtiendo la misma imagen a cada formato:

| | JPEG 1024 (original) | PNG 1024 | PNG 1400 | **WebP 1400 q82** |
|---|---:|---:|---:|---:|
| media | 400 KB | 750 KB | 1,3 MB | **43 KB** |
| peor caso | 519 KB | 1018 KB | 1,8 MB | **84 KB** |

PNG es aquí entre quince y treinta veces más pesado que WebP. Cien imágenes en
PNG a 1400 px serían **unos 125 MB** en un repositorio público; en WebP son
**unos 4 MB**, sin diferencia visible a este tamaño.

La canalización acepta **PNG, JPEG, WebP y AVIF** en los dos extremos
(`ExternalImageReader` y `FileSystemImageStore`), así que no hay nada que tocar
en el código — sólo la extensión en `seed/products.sample.json`.

**SVG también entra**, comprobado de punta a punta: el almacén lo mapea a
`image/svg+xml` y el API lo sirve tal cual, en 200 y con su tipo. Para dibujo
geométrico serían 2 KB por producto en vez de 45. No es el registro elegido,
pero la puerta está abierta.

### Sin alfa, con el fondo pintado

Las seis actuales son PNG **tipo 2 (RGB), sin canal alfa**: el fondo va dentro
de la imagen. Conviene seguir igual. Un fondo transparente se vería sobre
`--bg-surface`, y aunque hoy el storefront es siempre claro, es una dependencia
entre la imagen y el tema que no hace falta contraer — y los generadores de
imagen dejan halos en los bordes con frecuencia.

Usa un gris muy claro de la paleta: `#FAF9F7` o `#F3F1ED`. Blanco puro también
vale, pero la tarjeta ya es blanca y el producto se queda flotando sin borde.

### Nada de texto

La tienda es **es y en**. Una imagen con una palabra dentro es una imagen que
está mal en uno de los dos idiomas, y no hay forma de traducirla. Tampoco
logotipos, ni etiquetas, ni marcas de agua: las marcas de este catálogo son
inventadas y una imagen con un logotipo inventado encima es una falsificación de
algo que no existe.

### El nombre del fichero manda

El producto referencia la ruta **literalmente** en `seed/products.sample.json`:

```json
"images": ["images/B11COO0301.png"]
```

Las cien rutas dicen hoy `.png`, así que **pasar a WebP obliga a cambiarlas**.
La importación no adivina la extensión, y una ruta que no existe no rompe nada
— que es la forma más silenciosa posible de que esto salga mal. Cámbialas de una
vez con un script, no una a una.

## Cómo se conecta

Cada fichero va en `seed/images/`. La importación lee esa ruta, guarda la imagen
bajo el **hash de su contenido** y el producto se queda con esa clave
(ADR 0011). Reimportar la misma foto no la duplica; cambiarla crea una clave
nueva.

Una imagen que falte **no rompe nada**: el producto se importa igual y la ficha
muestra el tile de reserva. Se pueden ir añadiendo de una en una y reimportando.

El modelo admite **varias imágenes por producto** — `images` es una lista y la
principal es la de menor orden — pero hoy todos llevan una. Si generas dos
vistas de algo, añádelas a la lista.

## Qué han de parecer

Lo que dice `seed/IMAGES.md`, y conviene releerlo antes de empezar: estas
imágenes son **del proveedor de mentira**, no de Tendero, y los productos son
inventados. Una fotografía real de la zapatilla de otra marca haciéndose pasar
por «Pulse Runner» sería *menos* honesta que una ilustración que no finge ser
nada. Las seis que existen son ilustraciones planas con los colores de
`design/tokens.css`; mantener ese registro hace que las cien se vean como un
catálogo y no como un collage.

**El registro está elegido y no se mezcla**: ilustración plana, como las seis que
ya hay. Cien imágenes con el mismo encuadre, el mismo fondo y la misma luz
parecen un catálogo; cincuenta ilustraciones y cincuenta fotos parecen un
accidente.

### Plantilla de instrucción

Para pegar en el generador, cambiando sólo la última línea:

```
Flat vector-style product illustration for an online shop catalogue.
Square 1:1 composition, 1400 x 1400 pixels, PNG.
Single object, centred, front three-quarter view, occupying at most 75% of the
frame height, with clear empty margin at the top and the bottom.
Flat uniform background, very light warm grey (#FAF9F7). No transparency.
Clean even lighting, minimal soft shadow, no gradients on the background.
Limited warm palette: terracotta #D85A30, canvas #FAECE7, ink #2C2C2A,
olive #5C7F38, plus the object's own colour.
No text, no logos, no labels, no watermarks, no packaging, no hands, no props.
sRGB.

The object is <color>: <descripción en inglés, de la lista de abajo>
```

**El color no es opcional.** La ficha muestra el atributo `color` del catálogo,
así que una mochila naranja en un producto que dice «azul marino» es una
contradicción visible en pantalla. De las cinco primeras, dos no coincidían. El
color de cada producto está en `seed/products.sample.json`, en
`attributes.color`.

La descripción en inglés de cada producto está en su ficha, en cursiva. Es la
que conviene usar: describe el objeto sin nombre de marca inventado, que es lo
que un generador entiende mejor.

## Bolsas y mochilas > Bolsas y rinoneras

### `B13BAG0707.png` — Bolsa de lona Mercado

Bolsa de lona de algodon con asas largas y fondo reforzado. Aguanta la compra de la semana.

*Mercado canvas tote* — Cotton canvas tote with long handles and a reinforced base. It takes a week of shopping.

### `B13BAG0708.png` — Rinonera Cinturon 3L

Rinonera de tres litros con cremallera estanca y correa que se ajusta sin hebilla suelta.

*Cinturon 3L hip pack* — Three-litre hip pack with a water-resistant zip and a strap that adjusts without a loose buckle.

### `B13BAG0710.png` — Funda para portatil de 14 pulgadas

Funda acolchada de fieltro de lana con cierre magnetico y bolsillo exterior para el cargador.

*14-inch laptop sleeve* — Padded wool felt sleeve with a magnetic closure and an outer pocket for the charger.

### `B13BAG0711.png` — Alforja de bicicleta impermeable 20L

Alforja de cierre enrollable totalmente estanca, con gancho rapido y asa para llevarla en la mano.

*20L waterproof bike pannier* — Roll-top pannier that is fully waterproof, with a quick hook and a handle to carry it by hand.

## Bolsas y mochilas > Equipaje y viaje

### `B13BAG0703.png` — Mochila de viaje Trayecto 40L

Mochila de apertura frontal completa, con correas ocultables y medida de equipaje de mano.

*Trayecto 40L travel backpack* — Front-loading pack with stowable straps, sized to go in the cabin.

### `B13BAG0706.png` — Bolsa de deporte Vestuario 35L

Bolsa de deporte con compartimento separado para el calzado y bandolera acolchada desmontable.

*Vestuario 35L gym duffel* — Gym bag with a separate shoe compartment and a detachable padded shoulder strap.

### `B13BAG0709.png` — Neceser de viaje Aseo

Neceser con gancho para colgar, interior impermeable y espejo desmontable.

*Aseo travel wash bag* — Wash bag with a hanging hook, a waterproof lining and a removable mirror.

### `B13BAG0712.png` — Organizadores de equipaje, juego de 3

Tres cubos de malla de tamanos distintos que comprimen la ropa y dejan ver lo que hay dentro.

*Packing cubes, set of 3* — Three mesh cubes in different sizes that compress clothes and let you see what is inside.

## Bolsas y mochilas > Mochilas

### `B08JKLM202.png` — Mochila urbana impermeable 25L CityPack · **ya existe**

Mochila de dia con compartimento acolchado para portatil de 15 pulgadas, tejido ripstop con tratamiento DWR y bolsillo antirrobo en la espalda.

*CityPack waterproof urban backpack 25L* — Daypack with padded 15-inch laptop compartment, DWR-treated ripstop fabric and anti-theft back pocket.

### `B13BAG0701.png` — Mochila urbana CityPack 18L

Version compacta de la CityPack, con compartimento acolchado para tablet y espalda ventilada.

*CityPack 18L urban backpack* — The compact CityPack, with a padded tablet sleeve and a ventilated back panel.

### `B13BAG0702.png` — Mochila de senderismo Cumbre 30L

Mochila de montana con cinturon lumbar, salida para bolsa de hidratacion y funda de lluvia integrada.

*Cumbre 30L hiking backpack* — Mountain pack with a hip belt, a hydration port and a rain cover in its own pocket.

### `B13BAG0704.png` — Mochila para portatil Oficina 22L

Mochila de trabajo con compartimento acolchado de 16 pulgadas, bolsillo antirrobo y pasador para trolley.

*Oficina 22L laptop backpack* — Work backpack with a padded 16-inch compartment, a hidden pocket and a trolley sleeve.

### `B13BAG0705.png` — Mochila de running Ligera 8L

Chaleco de running con dos bolsillos frontales para botellines blandos y ajuste elastico en el pecho.

*Ligera 8L running vest pack* — Running vest with two front pockets for soft flasks and an elastic chest adjustment.

## Hogar > Almacenaje y orden

### `B14HOM0804.png` — Cesta de almacenaje de fibra natural

Cesta tejida a mano de 40 cm de diametro con asas. Para lena, mantas o juguetes.

*Natural fibre storage basket* — Hand-woven basket, 40 cm across, with handles. For firewood, blankets or toys.

### `B14ORG0901.png` — Estante especiero de bambu de dos alturas

Especiero de bambu de dos niveles que deja ver la fila de atras. Cabe dentro de un armario estandar.

*Two-tier bamboo spice rack* — Two-tier bamboo rack that lets you see the back row. It fits inside a standard cupboard.

### `B14ORG0902.png` — Organizador de cubiertos extensible

Bandeja de cubiertos de bambu que se ensancha de 33 a 55 cm para ajustarse al cajon.

*Expandable cutlery tray* — Bamboo cutlery tray that widens from 33 to 55 cm to fit the drawer.

### `B14ORG0903.png` — Escurreplatos de acero con bandeja

Escurridor de acero inoxidable con bandeja de goteo orientable y soporte para cubiertos.

*Steel dish rack with tray* — Stainless steel rack with a tilting drip tray and a cutlery holder.

### `B14ORG0904.png` — Cubo de reciclaje de tres compartimentos

Cubo de 3 x 20 litros con pedal y cubetas extraibles que se lavan por separado.

*Three-compartment recycling bin* — A 3 by 20 litre bin with a pedal and removable buckets that wash separately.

## Hogar > Cocina > Cafeteras

### `B09PQRS303.png` — Cafetera de goteo programable Aroma 12 tazas · **ya existe**

Cafetera de filtro con jarra de vidrio, temporizador de 24 horas, funcion pausa y sirve, y placa calefactora con apagado automatico.

*Aroma programmable drip coffee maker, 12 cups* — Filter coffee maker with glass carafe, 24-hour timer, pause-and-serve function and auto shut-off warming plate.

### `B11COF0401.png` — Cafetera italiana de 6 tazas

Cafetera de aluminio de toda la vida, con junta de silicona recambiable y valvula de seguridad.

*6-cup stovetop coffee maker* — The classic aluminium moka pot, with a replaceable silicone gasket and a safety valve.

### `B11COF0402.png` — Cafetera italiana de induccion de 4 tazas

Version en acero inoxidable de la cafetera italiana, con base ferromagnetica para induccion.

*4-cup induction stovetop coffee maker* — The stainless steel version of the moka pot, with a ferromagnetic base for induction hobs.

### `B11COF0403.png` — Prensa francesa de cristal de 1 litro

Cafetera de embolo con jarra de borosilicato y filtro de acero de doble malla. Sin papel y sin plastico.

*1 litre glass french press* — Plunger coffee maker with a borosilicate jug and a double stainless mesh filter. No paper, no plastic.

### `B11COF0404.png` — Cafetera de goteo Aroma Compacta

Version de 6 tazas de la Aroma, con jarra de cristal y placa que mantiene el calor treinta minutos.

*Aroma Compact drip coffee maker* — The 6-cup version of the Aroma, with a glass jug and a hotplate that holds heat for thirty minutes.

## Hogar > Cocina > Cuchillos

### `B11COO0309.png` — Cuchillo de chef de 20 cm

Cuchillo de chef en acero forjado con mango remachado y filo de 15 grados por lado.

*20 cm chef knife* — Forged steel chef knife with a riveted handle and a 15-degree edge per side.

### `B11COO0310.png` — Juego de 3 cuchillos de cocina

Cuchillo de chef, puntilla y cuchillo de pan en un bloque de bambu. Los tres del mismo acero.

*Set of 3 kitchen knives* — Chef knife, paring knife and bread knife in a bamboo block. All three from the same steel.

### `B11COO0311.png` — Afilador de cuchillos de dos etapas

Afilador manual con una etapa de diamante para reperfilar y otra de ceramica para pulir el filo.

*Two-stage knife sharpener* — Manual sharpener with a diamond stage to reprofile and a ceramic stage to polish the edge.

## Hogar > Cocina > Menaje de cocina

### `B05DEFG606.png` — Set de 3 sartenes antiadherentes aptas para induccion · **ya existe**

Sartenes de 20, 24 y 28 cm con revestimiento antiadherente libre de PFOA, base de acero para induccion y mangos de baquelita.

*Set of 3 induction-ready non-stick frying pans* — 20, 24 and 28 cm pans with PFOA-free non-stick coating, induction steel base and bakelite handles.

### `B11COO0301.png` — Sarten de hierro fundido de 26 cm

Sarten de hierro fundido precurada, apta para induccion, horno y fuego directo. Gana antiadherencia con el uso.

*26 cm cast iron frying pan* — Pre-seasoned cast iron pan for induction, oven and open flame. It gets more non-stick the more you use it.

### `B11COO0302.png` — Cazuela de acero inoxidable de 24 cm con tapa

Cazuela de fondo triple difusor que reparte el calor sin puntos calientes. Tapa de cristal templado con salida de vapor.

*24 cm stainless steel casserole with lid* — Triple-base casserole that spreads heat without hot spots. Tempered glass lid with a steam vent.

### `B11COO0303.png` — Olla a presion de 6 litros

Olla a presion con dos niveles de coccion, valvula de seguridad y cierre que no abre bajo presion.

*6 litre pressure cooker* — Pressure cooker with two cooking levels, a safety valve and a lid that will not open under pressure.

### `B11COO0304.png` — Wok de acero al carbono de 30 cm

Wok de acero al carbono con fondo plano para induccion y mango de madera que no se calienta.

*30 cm carbon steel wok* — Carbon steel wok with a flat base for induction and a wooden handle that stays cool.

### `B11COO0305.png` — Cazo lechero de 16 cm

Cazo pequeno con pico vertedor a ambos lados y asa fria. Para salsas, leche y una racion de pasta.

*16 cm milk pan* — Small pan with a pouring lip on both sides and a cool handle. For sauces, milk and one portion of pasta.

### `B11COO0306.png` — Bandeja de horno de ceramica

Fuente de ceramica esmaltada de 33 x 22 cm apta para horno, microondas y lavavajillas.

*Ceramic oven dish* — Glazed ceramic dish, 33 by 22 cm, for the oven, the microwave and the dishwasher.

### `B11COO0307.png` — Tabla de cortar de bambu con canal

Tabla de bambu de 38 x 28 cm con canal perimetral para los jugos. Ligera y facil de secar de pie.

*Bamboo chopping board with juice groove* — 38 by 28 cm bamboo board with a groove around the edge for juices. Light, and easy to dry standing up.

### `B11COO0308.png` — Juego de 5 utensilios de bambu

Cinco utensilios de bambu prensado que no rayan el antiadherente: cuchara, espatula, esparragos, cucharon y espumadera.

*Set of 5 bamboo utensils* — Five pressed bamboo utensils that will not scratch a non-stick coating: spoon, spatula, tongs, ladle and skimmer.

### `B11COO0312.png` — Colador de acero de 24 cm

Colador con base elevada y dos asas que apoyan en el fregadero. Perforado fino para pasta y arroz.

*24 cm stainless steel colander* — Colander with a raised base and two handles that rest on the sink. Fine perforation for pasta and rice.

### `B15KIT1203.png` — Molde de horno desmontable de 24 cm

Molde desmontable con base antiadherente y cierre lateral que se abre sin romper el bizcocho.

*24 cm springform cake tin* — Springform tin with a non-stick base and a side clasp that opens without breaking the cake.

### `B15KIT1204.png` — Juego de 3 boles de acero para mezclar

Tres boles apilables de 1, 2 y 3 litros con base de silicona para que no bailen.

*Set of 3 stainless mixing bowls* — Three stackable bowls of 1, 2 and 3 litres with a silicone base so they do not wander.

## Hogar > Cocina > Mesa y mantel

### `B11TAB0501.png` — Juego de 4 platos llanos de gres

Cuatro platos de gres esmaltado a mano de 27 cm. Aptos para horno, microondas y lavavajillas.

*Set of 4 stoneware dinner plates* — Four hand-glazed stoneware plates, 27 cm across. Oven, microwave and dishwasher safe.

### `B11TAB0502.png` — Juego de 4 tazones de desayuno

Cuatro tazones de gres de 450 ml con base sin esmaltar para que no resbalen.

*Set of 4 breakfast bowls* — Four 450 ml stoneware bowls with an unglazed base so they do not slide.

### `B11TAB0503.png` — Jarra de agua de cristal de 1,2 litros

Jarra de borosilicato con tapa de bambu y pico antigoteo. Resiste el cambio de temperatura.

*1.2 litre glass water jug* — Borosilicate jug with a bamboo lid and a drip-free lip. It takes a temperature change.

### `B11TAB0504.png` — Juego de 6 vasos de cristal reciclado

Seis vasos de 350 ml soplados en cristal reciclado. Cada uno sale ligeramente distinto.

*Set of 6 recycled glass tumblers* — Six 350 ml tumblers blown from recycled glass. Each one comes out slightly different.

### `B11TAB0505.png` — Botes de cocina hermeticos, juego de 3

Tres botes de cristal de 0,5, 1 y 1,5 litros con tapa de bambu y junta de silicona.

*Airtight kitchen jars, set of 3* — Three glass jars of 0.5, 1 and 1.5 litres with a bamboo lid and a silicone seal.

### `B11TAB0506.png` — Mantel de lino lavado de 240 cm

Mantel de lino lavado a la piedra que no necesita plancha. Encoge lo justo en el primer lavado.

*240 cm washed linen tablecloth* — Stone-washed linen tablecloth that needs no ironing. It shrinks a little on the first wash, and no more.

### `B14HOM0801.png` — Juego de 2 panos de cocina de lino

Dos panos de lino de 50 x 70 cm que secan sin dejar pelusa y ganan absorcion con los lavados.

*Set of 2 linen tea towels* — Two 50 by 70 cm linen towels that dry without leaving lint and absorb better with every wash.

### `B14HOM0802.png` — Delantal de lino con peto

Delantal de lino lavado con tirantes cruzados que no aprietan el cuello y dos bolsillos frontales.

*Linen bib apron* — Washed linen apron with crossed straps that do not pull on the neck, and two front pockets.

### `B14HOM0803.png` — Manoplas de horno acolchadas, par

Par de manoplas con relleno de algodon y exterior de lino, resistentes hasta 220 grados.

*Padded oven mitts, pair* — A pair of mitts with cotton wadding and a linen outer, rated to 220 degrees.

### `B14HOM0808.png` — Juego de 4 servilletas de lino

Cuatro servilletas de 45 cm con dobladillo en inglete. Se lavan con el mantel y a la misma temperatura.

*Set of 4 linen napkins* — Four 45 cm napkins with mitred hems. They wash with the tablecloth, at the same temperature.

## Hogar > Cocina > Pequeno electrodomestico

### `B11COF0405.png` — Molinillo de cafe de muelas conicas

Molinillo de muelas conicas con 15 puntos de molienda, de espresso a prensa francesa.

*Conical burr coffee grinder* — Conical burr grinder with 15 grind settings, from espresso to french press.

### `B11COF0406.png` — Hervidor de agua con control de temperatura

Hervidor de 1,7 litros con cinco temperaturas fijas y cuello de cisne para vertido lento.

*Temperature-controlled kettle* — A 1.7 litre kettle with five fixed temperatures and a gooseneck spout for slow pouring.

### `B11COF0407.png` — Tostadora de dos ranuras anchas

Tostadora con ranuras anchas para pan de hogaza, siete niveles y bandeja recogemigas extraible.

*Two wide-slot toaster* — Toaster with wide slots for sourdough, seven levels and a removable crumb tray.

### `B11COF0408.png` — Batidora de vaso de 1,5 litros

Batidora de vaso de cristal con cuchilla de seis aspas y dos velocidades mas pulso.

*1.5 litre jug blender* — Glass jug blender with a six-blade assembly, two speeds and a pulse setting.

### `B15KIT1201.png` — Bascula de cocina digital de 5 kg

Bascula de precision de 1 g con funcion de tara y pantalla que se lee con el bol encima.

*5 kg digital kitchen scale* — Precision scale to 1 g with a tare function and a display you can read with the bowl on top.

### `B15KIT1202.png` — Termometro de cocina de sonda

Termometro digital de lectura instantanea con sonda plegable y rango de -50 a 300 grados.

*Probe kitchen thermometer* — Instant-read digital thermometer with a folding probe and a range from -50 to 300 degrees.

## Hogar > Iluminación > Flexos y lámparas de escritorio

### `B06GHIJ505.png` — Lampara de escritorio LED regulable con puerto USB · **ya existe**

Flexo LED con tres temperaturas de color, brazo articulado y puerto de carga USB-A integrado en la base.

*Dimmable LED desk lamp with USB port* — LED desk lamp with three color temperatures, articulated arm and built-in USB-A charging port.

### `B12LAM0601.png` — Flexo LED articulado Taller

Flexo de brazo doble con pinza de mesa, giro de 350 grados y luz regulable en tres temperaturas.

*Taller articulated LED desk lamp* — Double-arm lamp with a desk clamp, 350 degrees of rotation and dimmable light at three temperatures.

### `B12LAM0602.png` — Lampara de escritorio con carga inalambrica

Lampara de mesa con base de carga inalambrica de 10 W y puerto USB-C adicional en el lateral.

*Desk lamp with wireless charging* — Desk lamp with a 10 W wireless charging base and an extra USB-C port on the side.

### `B12LAM0603.png` — Lampara de pie de lectura Butaca

Lampara de pie de 150 cm con cabezal orientable y regulador de intensidad en el propio cuello.

*Butaca reading floor lamp* — A 150 cm floor lamp with an adjustable head and a dimmer on the neck itself.

### `B12LAM0604.png` — Lampara de sobremesa de ceramica Cantaro

Lampara de mesa con base de ceramica torneada y pantalla de lino. Casquillo E27 estandar.

*Cantaro ceramic table lamp* — Table lamp with a thrown ceramic base and a linen shade. Standard E27 fitting.

### `B12LAM0605.png` — Aplique de pared orientable Rincon

Aplique de pared con brazo plegable, interruptor propio y cable textil de dos metros.

*Rincon adjustable wall light* — Wall light with a folding arm, its own switch and a two-metre fabric cable.

### `B12LAM0606.png` — Lampara portatil recargable Vela

Lampara sin cable con doce horas de autonomia y tres niveles. Se carga por USB-C.

*Vela rechargeable portable lamp* — Cordless lamp with twelve hours of battery and three levels. Charges over USB-C.

### `B12LAM0607.png` — Tira LED regulable de 3 metros

Tira LED de tres metros con adhesivo, mando y fuente incluida. Se puede cortar cada 5 cm.

*3 metre dimmable LED strip* — Three-metre LED strip with adhesive backing, a remote and a power supply. Cuts every 5 cm.

### `B12LAM0608.png` — Bombillas LED E27 calidas, pack de 4

Cuatro bombillas LED de 9 W equivalentes a 60 W, con indice de reproduccion cromatica alto.

*Warm E27 LED bulbs, pack of 4* — Four 9 W LED bulbs equivalent to 60 W, with a high colour rendering index.

### `B12LAM0609.png` — Lampara de noche con sensor de movimiento

Luz de paso que se enciende al detectar movimiento en la oscuridad y se apaga sola a los treinta segundos.

*Night light with motion sensor* — A corridor light that comes on when it detects movement in the dark and turns itself off after thirty seconds.

### `B12LAM0610.png` — Lampara de arquitecto con lupa

Flexo de brazo largo con lente de aumento de tres dioptrias y anillo LED alrededor.

*Architect lamp with magnifier* — Long-arm lamp with a three-dioptre magnifying lens and an LED ring around it.

## Hogar > Textil de hogar

### `B14HOM0805.png` — Manta de lana de 130 x 180 cm

Manta de lana virgen tejida en telar con fleco cosido a mano. Pesa 1,4 kg.

*130 by 180 cm wool blanket* — Loom-woven virgin wool blanket with a hand-sewn fringe. It weighs 1.4 kg.

### `B14HOM0806.png` — Juego de 2 cojines de lino de 45 cm

Dos fundas de cojin de lino con cierre oculto de cremallera. Relleno no incluido.

*Set of 2 45 cm linen cushion covers* — Two linen cushion covers with a hidden zip. Inner pad not included.

### `B14HOM0807.png` — Alfombra de bano de algodon

Alfombra de bano de algodon de rizo doble de 50 x 80 cm con base antideslizante.

*Cotton bath mat* — Double-loop cotton bath mat, 50 by 80 cm, with a non-slip backing.

## Ropa > Abrigos y chaquetas

### `B10SHI0209.png` — Cortavientos plegable Racha

Cortavientos de 140 g que cabe en su propio bolsillo. Costuras selladas en los hombros.

*Racha packable windbreaker* — A 140 g windbreaker that folds into its own pocket. Sealed shoulder seams.

### `B15APP1001.png` — Camisa de lino de manga larga

Camisa de lino lavado con cuello suave y un solo bolsillo. Se arruga, y esa es la idea.

*Long-sleeve linen shirt* — Washed linen shirt with a soft collar and a single pocket. It creases, and that is the point.

### `B15APP1002.png` — Jersey de lana merino de cuello redondo

Jersey de merino de 250 g con cuello, punos y bajo acanalados. Fino pero abrigado.

*Merino wool crew neck jumper* — A 250 g merino jumper with a ribbed collar, cuffs and hem. Thin, and still warm.

### `B15APP1003.png` — Chaleco acolchado Refugio

Chaleco acolchado con relleno reciclado y bolsillos con cremallera. Se pliega al tamano de un libro.

*Refugio padded gilet* — Padded gilet with recycled filling and zipped pockets. It folds to the size of a book.

### `B15APP1004.png` — Chaqueta impermeable Aguacero 3 capas

Chaqueta de tres capas con capucha ajustable, cremalleras de ventilacion y costuras termoselladas.

*Aguacero 3-layer waterproof jacket* — Three-layer jacket with an adjustable hood, pit zips and taped seams.

## Ropa > Calzado > Zapatillas deportivas

### `B073WXYZ01.png` — Zapatillas de running amortiguadas Pulse Runner · **ya existe**

Zapatillas de running neutras con mediasuela de espuma reactiva, upper de malla transpirable y refuerzo en el talon. Drop de 8 mm.

*Pulse Runner cushioned running shoes* — Neutral running shoes with responsive foam midsole, breathable mesh upper and reinforced heel counter. 8 mm drop.

### `B10SHO0101.png` — Zapatillas de running de trail Pulse Runner Trail

Version de trail de la Pulse Runner: zapatilla de running con taco de 4 mm, placa de roca bajo el antepie y upper con refuerzo antiabrasion. Drop de 8 mm.

*Pulse Runner Trail running shoes* — The trail version of the Pulse Runner: 4 mm lugs, a rock plate under the forefoot and an abrasion-resistant upper. 8 mm drop.

### `B10SHO0102.png` — Zapatillas de running de competicion Pulse Runner Carbon

Zapatilla de running para competir, con placa de carbono y espuma de alto retorno. Pesa 218 g en talla 42. Drop de 6 mm.

*Pulse Runner Carbon racing shoes* — Racing shoe with a carbon plate and high-rebound foam. 218 g in a size 42. 6 mm drop.

### `B10SHO0103.png` — Zapatillas de andar Sendero

Zapatilla de paseo con suela de goma flexible y plantilla extraible. Pensada para caminar por ciudad todo el dia.

*Sendero everyday walking shoes* — Everyday walking shoe with a flexible rubber sole and a removable insole. Made for a whole day on pavement.

### `B10SHO0104.png` — Zapatillas de lona Puerto

Zapatilla de lona de algodon con suela vulcanizada y ojales metalicos. Un clasico de verano que se lava en frio.

*Puerto canvas trainers* — Cotton canvas trainer with a vulcanised sole and metal eyelets. A summer classic that washes cold.

### `B10SHO0105.png` — Zapatillas de montana Roca 3

Zapatilla de montana con horma ancha, drenaje lateral y proteccion en la puntera. Para terreno tecnico y humedo.

*Roca 3 mountain shoes* — Mountain shoe with a wide fit, side drainage and a protected toe box. For technical, wet ground.

### `B10SHO0106.png` — Zapatillas de casa Hogar

Zapatilla de estar por casa forrada en lana con suela antideslizante. Se lava a mano.

*Hogar house slippers* — Wool-lined house slipper with a non-slip sole. Hand washable.

### `B10SHO0107.png` — Zapatillas de gimnasio Estable

Zapatilla deportiva de suela plana y firme para levantar peso. Sujecion en el mediopie y talon estable. Drop de 4 mm.

*Estable gym trainers* — Flat, firm-soled trainer for lifting. Midfoot hold and a stable heel. 4 mm drop.

### `B10SHO0108.png` — Sandalias de trekking Vado

Sandalia de tres tiras regulables con suela de agarre para rio y roca mojada. Se seca en una hora.

*Vado trekking sandals* — Three-strap adjustable sandal with a grippy sole for rivers and wet rock. Dries in an hour.

### `B15SHO1101.png` — Botas de agua cortas Charco

Botas de agua de caucho natural con forro de algodon y suela con dibujo profundo.

*Charco short wellington boots* — Natural rubber boots with a cotton lining and a deep-tread sole.

### `B15SHO1102.png` — Botines de piel Adoquin

Botin de piel curtida al vegetal con suela cosida, que se puede recambiar en un zapatero.

*Adoquin leather ankle boots* — Vegetable-tanned leather boot with a stitched sole that a cobbler can replace.

### `B15SHO1103.png` — Zapatillas de padel Pista

Zapatilla de pista con suela de espiga para tierra batida y refuerzo en el lateral del arrastre.

*Pista padel shoes* — Court shoe with a herringbone sole for clay and a reinforced drag panel.

### `B15SHO1104.png` — Alpargatas de esparto Verano

Alpargata de lona con suela de esparto trenzado a mano y puntera reforzada.

*Verano esparto espadrilles* — Canvas espadrille with a hand-braided esparto sole and a reinforced toe.

## Ropa > Complementos

### `B10SHI0210.png` — Calcetines de lana merino, pack de 3

Tres pares de calcetines de merino con puntera sin costura y refuerzo en el talon.

*Merino wool socks, pack of 3* — Three pairs of merino socks with a seamless toe and a reinforced heel.

### `B15APP1006.png` — Bufanda de lana de doble cara

Bufanda de 180 x 30 cm tejida en dos colores, uno por cara, sin costura de union.

*Double-faced wool scarf* — A 180 by 30 cm scarf woven in two colours, one per face, with no joining seam.

### `B15APP1007.png` — Gorro de punto de lana merino

Gorro de punto fino con banda doble en la frente. Abriga sin dar calor de mas.

*Merino wool knitted beanie* — Fine-knit beanie with a doubled band at the forehead. Warm without overheating.

### `B15APP1008.png` — Guantes tecnicos tactiles

Guantes finos con punta conductiva en indice y pulgar y silicona en la palma.

*Touchscreen technical gloves* — Thin gloves with conductive index and thumb tips and silicone on the palm.

## Ropa > Pantalones

### `B10SHI0207.png` — Mallas largas de running Kilometro

Mallas largas con cintura alta, bolsillo lateral para el telefono y reflectantes en el gemelo.

*Kilometro running tights* — Full-length tights with a high waist, a side phone pocket and reflective detail at the calf.

### `B10SHI0208.png` — Pantalon corto de running de doble capa

Pantalon corto con malla interior, bolsillo con cremallera y tiro de 13 cm.

*Two-layer running shorts* — Running shorts with an inner brief, a zipped pocket and a 13 cm inseam.

### `B15APP1005.png` — Pantalon de montana desmontable

Pantalon de montana con perneras desmontables por cremallera y tejido elastico en cuatro direcciones.

*Convertible hiking trousers* — Hiking trousers with zip-off legs and four-way stretch fabric.

## Ropa > Ropa deportiva > Camisetas

### `B07TUVW404.png` — Camiseta tecnica de trail manga corta · **ya existe**

Camiseta ligera de secado rapido con costuras planas y tejido con proteccion UV UPF 30.

*Short-sleeve trail running tech tee* — Lightweight quick-dry tee with flat seams and UPF 30 sun-protective fabric.

### `B10SHI0201.png` — Camiseta tecnica de manga larga Trail

Camiseta de manga larga en tejido reciclado con costuras planas y pulgareras. Secado rapido y control de olor.

*Trail long-sleeve technical tee* — Long-sleeve tee in recycled fabric with flat seams and thumb loops. Fast drying, odour controlled.

### `B10SHI0202.png` — Camiseta de tirantes Ligera

Camiseta sin mangas de 92 g con espalda perforada. Pensada para correr en verano sin que se pegue.

*Ligera running vest* — 92 g sleeveless vest with a perforated back. Made to run in summer without clinging.

### `B10SHI0203.png` — Camiseta de lana merino de manga corta

Camiseta de lana merino de 150 g. Regula la temperatura, no coge olor y aguanta varios dias seguidos.

*Merino wool short-sleeve tee* — 150 g merino wool tee. Regulates temperature, resists odour and lasts several days running.

### `B10SHI0204.png` — Camiseta de algodon organico Diario

Camiseta de algodon organico de 180 g con cuello reforzado. Corte recto que no se deforma al lavar.

*Diario organic cotton tee* — 180 g organic cotton tee with a reinforced collar. A straight cut that holds its shape in the wash.

### `B10SHI0205.png` — Maillot de ciclismo Cadencia

Maillot con tres bolsillos traseros, cremallera completa y banda siliconada en la cintura.

*Cadencia cycling jersey* — Jersey with three rear pockets, a full-length zip and a silicone waist gripper.

### `B10SHI0206.png` — Sudadera de algodon cepillado Taller

Sudadera de interior cepillado con punos elasticos y cuello acanalado. Gramaje de 320 g.

*Taller brushed cotton sweatshirt* — Brushed-inside sweatshirt with elastic cuffs and a ribbed collar. 320 g weight.
