# Voice recordings for the measurement (phase 14, V-5)

The recordings both adapters are measured on (`docs/specs/voice.md`, ruling 3):
the author's own voice, about twenty questions a language. Each is read once,
naturally, as a shopper would say it to the shop. Short searches from the golden
sets are mixed with the longer, spoken questions voice is for. What each one
should be heard as is written beside it: that is what the word error rate is
counted against.

**How to record.** Any app (the phone's voice memos, Windows' Sound Recorder),
in a quiet room, one file per line, named by its id (`es-01.m4a`, `en-07.wav`…).
The format does not matter: the measurement redraws each one as the 16 kHz mono
WAV the shop listens to, with the same browser code the storefront uses. Then
put the files in `tools/voice-eval/recordings/`.

**They go in the public repository with the tool**, as the golden sets do: a
measurement a reader cannot repeat proves nothing. If the author would rather
not publish their voice, the recordings stay private under
`design/unpublished/`, and the article shows the table without them.

## Español

| id | Say | Heard as |
|---|---|---|
| es-01 | Zapatillas para correr por montaña | zapatillas para correr por montaña |
| es-02 | Zapatillas running mujer | zapatillas running mujer |
| es-03 | Una mochila impermeable para la bici | una mochila impermeable para la bici |
| es-04 | Mochila para portátil de quince pulgadas | mochila para portátil de 15 pulgadas |
| es-05 | Cafetera de doce tazas | cafetera de 12 tazas |
| es-06 | Una lámpara de escritorio que no ocupe sitio | una lámpara de escritorio que no ocupe sitio |
| es-07 | Flexo con USB | flexo con usb |
| es-08 | Sartenes para inducción | sartenes para inducción |
| es-09 | ¿Qué puedo regalar a un corredor? | qué puedo regalar a un corredor |
| es-10 | Algo para no mojarme en la bici | algo para no mojarme en la bici |
| es-11 | Una luz para leer en la cama | una luz para leer en la cama |
| es-12 | Ropa de abrigo para el frío | ropa de abrigo para el frío |
| es-13 | Una sartén que no se pegue | una sartén que no se pegue |
| es-14 | Tetera eléctrica | tetera eléctrica |
| es-15 | Mochila por menos de setenta euros | mochila por menos de 70 euros |
| es-16 | ¿Cuánto cuestan los gastos de envío? | cuánto cuestan los gastos de envío |
| es-17 | Quiero devolver un pedido | quiero devolver un pedido |
| es-18 | Una planta que no sea tóxica para gatos | una planta que no sea tóxica para gatos |
| es-19 | Un kit de cocina para inducción por menos de doscientos euros | un kit de cocina para inducción por menos de 200 euros |
| es-20 | ¿Tenéis alguna oferta para hoy? | tenéis alguna oferta para hoy |

## English

| id | Say | Heard as |
|---|---|---|
| en-01 | Running shoes for trail running | running shoes for trail running |
| en-02 | Women's running shoes | women's running shoes |
| en-03 | A waterproof backpack for the bike | a waterproof backpack for the bike |
| en-04 | Laptop backpack for fifteen inches | laptop backpack for 15 inches |
| en-05 | Twelve cup coffee maker | 12 cup coffee maker |
| en-06 | A desk lamp that takes no space | a desk lamp that takes no space |
| en-07 | Reading light with USB | reading light with usb |
| en-08 | Induction frying pans | induction frying pans |
| en-09 | What can I get a runner as a gift? | what can i get a runner as a gift |
| en-10 | Something to stay dry on the bike | something to stay dry on the bike |
| en-11 | A light for reading in bed | a light for reading in bed |
| en-12 | Warm clothes for cold weather | warm clothes for cold weather |
| en-13 | A pan food won't stick to | a pan food won't stick to |
| en-14 | Electric kettle | electric kettle |
| en-15 | Backpack under seventy euros | backpack under 70 euros |
| en-16 | How much is shipping? | how much is shipping |
| en-17 | I want to return an order | i want to return an order |
| en-18 | A plant that is safe for cats | a plant that is safe for cats |
| en-19 | A kitchen kit for induction under two hundred euros | a kitchen kit for induction under 200 euros |
| en-20 | Any offers today? | any offers today |

**How the count is made.** Both texts lowercased, punctuation removed and numbers
written as figures before the words are compared (*doce* and *12* are the same
word heard), so the rate measures what a search would see.
