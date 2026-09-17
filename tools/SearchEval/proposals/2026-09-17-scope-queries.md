# Scope queries — 2026-09-17

**Written before layer B was run on anything.** These are the queries the scope classifier — "does this shop sell anything that answers the query?" — is measured on. They were written in one pass, without reading the out-of-scope anchors (`src/Search/Answers/out-of-scope-anchors.json`) or the classifier's code, and without reading the query lists of the golden sets in `tools/SearchEval/golden`. A test (`ScopeSetTests.No_scope_query_repeats_a_golden_query`) is what guarantees none of them repeats a golden query; nothing was checked by eye. None of them was tried against any model, index or classifier before being committed.

The only inputs were the catalogue the shop actually sells (`seed/categories.sample.json` and the product names in `seed/products.sample.json`) and the shopper phrasing patterns recorded in `2026-09-15-queries.md`: lower case, few words, gifts and recipients, symptoms, negations with *sin* / *without*, and typos.

## Label rules

- **`in`** — the shop sells something that answers the query, even if no single product carries every word of it: a gift for a kind of person, a symptom ("no tengo sitio para las especias"), a use case ("para ir en bici al trabajo"), a negation ("sartén sin teflón"), or a product type spelled wrong.
- **`out`** — the shop sells nothing of the product type the query asks for. Words the catalogue happens to contain do not change that: "dishwasher safe" is in a description, and a dishwasher is still not sold.

Every edge case was decided against the seed files, and its reason is in `why`.

## Kinds — a closed set

| kind | scope | what it is |
|---|---|---|
| `gift` | in | a present for a kind of person, no product named |
| `symptom` | in | a problem to solve, no product named |
| `use-case` | in | an activity or occasion, no product named |
| `negation` | in | a product type without a feature (*sin* / *without*) |
| `typo` | in | a product type the shop sells, misspelt |
| `edge` | out | a product type next to something the shop does sell |
| `not-sold` | out | a product type clearly outside the shop |

A test holds every query to this set and to the scope its kind belongs to.

## Splits — a rule, not a choice

A pair whose id ends in an odd digit is `scope-dev`; an even digit, `scope-test`. Both cultures of a pair share its id, scope, kind and split, so one wording cannot leak what the other is tested on. Each split holds 10 `in` and 10 `out` pairs per culture.

## Counts

| | es | en |
|---|---:|---:|
| queries | 40 | 40 |
| `in` / `out` | 20 / 20 | 20 / 20 |
| `scope-dev` (in / out) | 10 / 10 | 10 / 10 |
| `scope-test` (in / out) | 10 / 10 | 10 / 10 |

By kind, per culture: gift 5, symptom 4, use-case 4, negation 3, typo 4, edge 12, not-sold 8.

## Every pair

Spanish carries its accents except in the deliberate typos (S17–S20), which are misspelt as a shopper would.

| pair | es | en | scope | kind | split | why |
|---|---|---|---|---|---|---|
| S01 | regalo para un padre cocinillas | gift for a dad who loves cooking | in | gift | scope-dev | cookware, knives and aprons are sold: a cook has plenty to choose from |
| S02 | regalo para alguien que viaja mucho | gift for someone who travels a lot | in | gift | scope-test | a cabin-sized travel backpack, packing cubes and a travel wash bag are sold |
| S03 | regalo para alguien que teletrabaja | gift for someone who works from home | in | gift | scope-dev | desk lamps, a lamp with wireless charging and laptop backpacks and sleeves are sold |
| S04 | regalo para una casa nueva | housewarming gift | in | gift | scope-test | tableware, glass tumblers, a wool blanket and linen cushion covers are sold |
| S05 | detalle para mi suegra que adora el café | present for my mother in law who loves coffee | in | gift | scope-dev | moka pots, a french press, a drip coffee maker and a burr grinder are sold |
| S06 | no tengo sitio para las especias | no room for my spices | in | symptom | scope-test | a two-tier bamboo spice rack that fits inside a cupboard is sold |
| S07 | me duelen los pies de andar todo el día | my feet hurt after walking all day | in | symptom | scope-dev | everyday walking shoes with a removable insole are sold |
| S08 | no veo bien para coser por la noche | can't see well enough to sew at night | in | symptom | scope-test | an architect lamp with a magnifier and dimmable desk lamps are sold |
| S09 | se me mojan los pies cuando llueve | my feet get wet when it rains | in | symptom | scope-dev | short wellington boots and waterproof mountain shoes are sold |
| S10 | algo para llevar la ropa al gimnasio | something to carry my gym clothes in | in | use-case | scope-test | a gym duffel with a shoe compartment is sold, and gym trainers too |
| S11 | para una ruta por la montaña | for a day hike in the mountains | in | use-case | scope-dev | a hiking backpack, convertible hiking trousers, mountain shoes and a waterproof jacket are sold |
| S12 | para ir en bici al trabajo | commuting to work by bike | in | use-case | scope-test | a waterproof bike pannier, a cycling jersey and a packable windbreaker are sold |
| S13 | desayuno en familia | family breakfast at home | in | use-case | scope-dev | a set of breakfast bowls, a wide-slot toaster, moka pots and a drip coffee maker are sold |
| S14 | cafetera sin cápsulas | coffee maker without pods | in | negation | scope-test | no capsule machine is sold, and every coffee maker that is answers the query: moka pots, french press, drip |
| S15 | sartén sin teflón | frying pan without teflon | in | negation | scope-dev | the cast iron pan and the carbon steel wok have no non-stick coating |
| S16 | chaqueta sin mangas | jacket without sleeves | in | negation | scope-test | a padded gilet is sold; the query names a jacket and wants the sleeveless kind |
| S17 | cazuela acero inoxidabe | stainless steel casserol | in | typo | scope-dev | a 24 cm stainless steel casserole with lid is sold; the typo is in the product words |
| S18 | molinilo de cafe | cofee grinder | in | typo | scope-test | a conical burr coffee grinder is sold |
| S19 | tostadroa | toastr | in | typo | scope-dev | a two wide-slot toaster is sold |
| S20 | delantal de cocna | kitchen aporn | in | typo | scope-test | a linen bib apron is sold |
| S21 | balón de baloncesto talla 7 | basketball size 7 | out | edge | scope-dev | sportswear and sports shoes are sold; balls are sports equipment, and no equipment is |
| S22 | lavavajillas integrable 60 cm | built in dishwasher 60 cm | out | edge | scope-test | small kitchen appliances are sold (kettle, toaster, blender); large appliances are not, even though "dishwasher safe" appears in descriptions |
| S23 | colchón viscoelástico 150 | memory foam mattress queen size | out | edge | scope-dev | home textiles are sold (blanket, cushion covers, bath mat, napkins); bedding and mattresses are not |
| S24 | funda para iphone | iphone case | out | edge | scope-test | bags, a laptop sleeve and a wash bag are sold; phone cases are not |
| S25 | raqueta de pádel | padel racket | out | edge | scope-dev | padel shoes are sold; rackets are sports equipment, which is not |
| S26 | sofá de dos plazas | two seater sofa | out | edge | scope-test | linen cushion covers and a storage basket are sold; furniture is not |
| S27 | maleta rígida con ruedas | hard shell suitcase with wheels | out | edge | scope-dev | the luggage category holds a cabin-sized travel backpack, packing cubes and a wash bag; no wheeled suitcase is sold |
| S28 | bicicleta de montaña | mountain bike | out | edge | scope-test | a bike pannier and a cycling jersey are sold; bicycles are not |
| S29 | freidora de aire | air fryer | out | edge | scope-dev | small appliances are sold, but only a kettle, a toaster and a blender; no air fryer is |
| S30 | gafas de sol para correr | running sunglasses | out | edge | scope-test | running apparel and accessories (gloves, beanie, scarf) are sold; eyewear is not |
| S31 | cortinas opacas | blackout curtains | out | edge | scope-dev | home textiles are sold, but curtains are not among them |
| S32 | portátil para estudiantes | laptop for students | out | edge | scope-test | laptop backpacks and a laptop sleeve are sold; computers are not |
| S33 | pienso para perro | dog food | out | not-sold | scope-dev | no pet products of any kind are sold |
| S34 | crema solar factor 50 | sunscreen spf 50 | out | not-sold | scope-test | no cosmetics or personal care products are sold (a tee with UPF fabric is clothing, not sunscreen) |
| S35 | neumáticos de coche | car tyres | out | not-sold | scope-dev | no automotive products are sold |
| S36 | cámara de fotos instantánea | instant camera | out | not-sold | scope-test | no cameras or photography products are sold; the only lens in the catalogue is the magnifier on an architect lamp |
| S37 | taladro percutor | hammer drill | out | not-sold | scope-dev | no tools or DIY products are sold |
| S38 | perfume de mujer | womens perfume | out | not-sold | scope-test | no fragrances are sold |
| S39 | auriculares inalámbricos | wireless earbuds | out | not-sold | scope-dev | no consumer electronics are sold (the lamps with USB and wireless charging are lighting) |
| S40 | vitaminas para el cansancio | vitamins for tiredness | out | not-sold | scope-test | no health or supplement products are sold |

## The edge decisions, in short

The twelve `edge` pairs are the ones that make the measurement mean something, because each sits next to a category the shop really has:

- **Sports equipment is not sportswear**: a basketball (S21) and a padel racket (S25) are out, although sportswear and padel shoes are in.
- **Large appliances are not small ones**: a dishwasher (S22) and an air fryer (S29) are out; the shop's small appliances are a kettle, a toaster and a blender, and nothing else.
- **Home textiles stop at what is sold**: a mattress (S23) and blackout curtains (S31) are out, although blankets, cushion covers, napkins and a bath mat are in.
- **Carrying a thing is not selling it**: an iPhone case (S24) and a laptop (S32) are out, although bags, a laptop sleeve and laptop backpacks are in.
- **Around the rest**: a sofa (S26) beside cushion covers, a wheeled suitcase (S27) beside a luggage category that holds only a travel backpack, packing cubes and a wash bag, a mountain bike (S28) beside a bike pannier and a cycling jersey, and running sunglasses (S30) beside running accessories.

Two `in` labels are closer calls, and were kept `in` on the label rule: **S14** ("cafetera sin cápsulas") is answered by every coffee maker the shop sells, none of which takes capsules; **S16** ("chaqueta sin mangas") is answered by the padded gilet.
