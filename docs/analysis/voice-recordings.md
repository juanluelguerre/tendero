# Voice recordings for the measurement (phase 14, V-5)

The recordings the engines are measured on (`docs/specs/voice.md`, ruling 3):
the author's own voice, about twenty questions a language. Each is read once,
naturally, as a shopper would say it to the shop. Short searches from the golden
sets are mixed with the longer, spoken questions voice is for. What is said is
also what the word error rate is counted against, once normalised (below).

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

| id | Say |
|---|---|
| es-01 | Zapatillas para correr por montaña |
| es-02 | Zapatillas running mujer |
| es-03 | Una mochila impermeable para la bici |
| es-04 | Mochila para portátil de quince pulgadas |
| es-05 | Cafetera de doce tazas |
| es-06 | Una lámpara de escritorio que no ocupe sitio |
| es-07 | Flexo con USB |
| es-08 | Sartenes para inducción |
| es-09 | ¿Qué puedo regalar a un corredor? |
| es-10 | Algo para no mojarme en la bici |
| es-11 | Una luz para leer en la cama |
| es-12 | Ropa de abrigo para el frío |
| es-13 | Una sartén que no se pegue |
| es-14 | Tetera eléctrica |
| es-15 | Mochila por menos de setenta euros |
| es-16 | ¿Cuánto cuestan los gastos de envío? |
| es-17 | Quiero devolver un pedido |
| es-18 | Una planta que no sea tóxica para gatos |
| es-19 | Un kit de cocina para inducción por menos de doscientos euros |
| es-20 | ¿Tenéis alguna oferta para hoy? |

## English

| id | Say |
|---|---|
| en-01 | Running shoes for trail running |
| en-02 | Women's running shoes |
| en-03 | A waterproof backpack for the bike |
| en-04 | Laptop backpack for fifteen inches |
| en-05 | Twelve cup coffee maker |
| en-06 | A desk lamp that takes no space |
| en-07 | Reading light with USB |
| en-08 | Induction frying pans |
| en-09 | What can I get a runner as a gift? |
| en-10 | Something to stay dry on the bike |
| en-11 | A light for reading in bed |
| en-12 | Warm clothes for cold weather |
| en-13 | A pan food won't stick to |
| en-14 | Electric kettle |
| en-15 | Backpack under seventy euros |
| en-16 | How much is shipping? |
| en-17 | I want to return an order |
| en-18 | A plant that is safe for cats |
| en-19 | A kitchen kit for induction under two hundred euros |
| en-20 | Any offers today? |

**How the count is made.** The line said and what an engine heard are both
normalised before their words are compared: lowercased, punctuation removed and
numbers written as figures (*doce* and *12* are the same word heard, and so are
*¿Qué?* and *que*). So the rate measures what a search would see, and one column
is enough: the tool derives the rest.
