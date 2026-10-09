# The catalogue grows: twenty products in families the shop did not have

**Status**: built 2026-10-08, on the author's ruling of 2026-10-07 (*the
catalogue grows, it does not change*) and his go-ahead the next day. The
pictures wait for him to accept them.

## Problem

Looking at the shop's pictures, it read as bags and shoes. Sixteen kinds of
product, all of them things carried, worn, cooked with or switched on; nothing
a person sits on, sleeps in, waters or hangs on a wall. A shop that is judged by
browsing it was a narrower shop than its engineering.

## Decided by the author

- **Add, never replace.** Replacing products would move the golden set's
  judgments and every number an article quotes. Twenty are added, in families
  the shop did not have, and nothing that existed is touched.
- **The families**: seating and tables, the bedroom and large storage, plants
  and the garden, decoration, and the bathroom.
- With their categories in es and en, prices, stock and pictures, and **the
  golden set extended rather than rejudged**.
- It changes `CLAUDE.md`'s *the catalogue is 100 products*: it is 120.

## What was added

| Family | Category | Products |
|---|---|---|
| Seating | `SEATING` under a new `FURNITURE` | an oak dining chair, an ergonomic office chair, a two-seater sofa, a tall pine stool |
| Tables | `TABLES` | an extendable oak dining table, an electric standing desk, a round steel side table, an oak coffee table |
| Bedroom | `BEDROOM` | an oak double bed frame, a two-door pine wardrobe, an oak bedside table, a four-drawer pine chest |
| Plants and garden | `PLANTS` | a monstera, a potted olive tree, a set of three terracotta pots, a steel watering can |
| Decoration | `DECOR` | a round wall mirror, a hand-woven wool rug |
| Bathroom | `BATHROOM` | a set of four cotton towels, a three-shelf bamboo rack |

All under `HOME`, so the tree is still three roots. Two new brands, *Arbor* for
furniture and *Verdor* for plants; the textiles and the mirror are the existing
*Lanar* and *Barro*.

**Six attributes**, because furniture and plants are described by things nothing
else in the shop has: `WIDTH_CM`, `DEPTH_CM`, `HEIGHT_CM`, `SEATS`,
`LIGHT_NEEDS` (full sun, bright indirect light, low light — a facet) and
`PET_SAFE` (a facet). Five materials: oak, pine, wool, terracotta clay,
upholstery fabric.

**What the categories require** (ADR 0056, amended): furniture must say its
three measures, and seating its seats — every one of the twelve pieces does. No
new product is incomplete; the five that are, are the five that were.

**Laboratory scale holds**: two warehouses, both stocked, with the shapes a
shop has — a sofa and a wardrobe that are only in Madrid, plants and towels by
the dozen. Both price lists carry every new SKU, the second at its usual tenth
less.

## What it did to search, measured

On throwaway engines, the gate's own run (`--strategy hybrid`: Elasticsearch
9.5.0, Qdrant v1.18.0, `bge-m3` and the reranker on the GPU), twice, identical:

| NDCG@10 | Before: 100 products, 52 scored queries | The same 52 queries, 120 products | 63 queries, 120 products |
|---|---|---|---|
| lexical en | 0.748 | 0.744 | **0.773** |
| lexical es | 0.692 | 0.685 | **0.714** |
| hybrid + reranker en | 0.843 | 0.834 | **0.855** |
| hybrid + reranker es | 0.831 | 0.822 | **0.839** |

recall@50 over the 63: lexical 0.806 en and 0.746 es, hybrid 0.943 and 0.924.

- **The middle column is the cost.** Twenty more products are twenty more
  things for a word to land on: *coffee maker* now finds a coffee table at
  seven, *electric kettle* a standing desk at two, and *luz para leer en la
  cama* opens with the bed and two plants that want light. Four and seven
  thousandths for lexical, nine for hybrid — real, small, and the kind a larger
  catalogue makes larger.
- **Nothing judged before was judged again.** 1075 judgments became 1273 in
  English and 1053 became 1256 in Spanish: every new one is for a product that
  did not exist, or for a query that did not. Most are zeros with a name on
  them — what a neighbourhood of meaning drags in, napkins for *bedside table*.
  The few above zero follow the rules the set already had: a thing that goes
  with what was asked is a one (the dining table for *setting the table*: where
  it is set, not what sets it; a wool rug for *merino wool*: wool, and not
  merino), and a decorative thing is a possible gift.
- **One query changed kind.** *House plant* was one of the eight with nothing to
  find — the shop sold none. It sells two now, and the query is scored: the
  monstera, then the olive tree, which is a plant for a patio.
- **Ten new pairs**, one for each thing a shopper would now ask for: a dining
  chair, an office chair, a sofa, a standing desk, a bedside table, a wardrobe,
  plant pots, bath towels, a rug for the living room, and *a plant that is safe
  for cats* — whose best lexical match is the monstera, which is toxic to them,
  judged zero for that reason.
- **The new queries are easy**, 0.91 and 0.85 on their own for lexical, 0.95
  and 0.92 for hybrid: one obvious product each, in a family with four. That is
  why the headline rose. It is not search getting better, and the floors were
  re-derived from the whole set for that reason: lexical 0.69 es / 0.75 en,
  hybrid 0.82 / 0.83 — each two hundredths under its measurement, as before.
- **One of the new ones is hard, and says why hybrid exists.** *Planta que no
  sea tóxica para gatos*: lexical 0.63 and hybrid 0.63 — neither reads
  *no es tóxico para mascotas* as an answer about cats and the monstera's
  *tóxica para perros y gatos* as the opposite. A negation is not a keyword and
  not a neighbourhood. In English hybrid gets it, 1.000.

Every result in every first ten of every strategy is judged (`judged@10` 1.000).

## Pictures

Twenty, drawn by the hosted model with the catalogue's own prompt, one command a
product, about sixty cents. Furniture came out without a room around it, which
the ruling had worried about. **For the author to accept or reject**; what a
first look found:

- the chest of drawers is drawn with five drawers and says four;
- the monstera's pot carries three small icons — a mark, which the checks exist
  to catch;
- the side table is a drum, not the tray on legs its description says;
- the stool's seat is white on untreated pine.

One was redrawn already, and it was the seed's fault: the bathroom rack said
`piezas: 3` for its three shelves, and the model drew three racks. A rack is one
piece; the attribute came off it and off the chest.

## Not done

- Variants on the new products: a sofa in two colours is the obvious next one,
  and it is two more pictures.
- Facets by category: `LIGHT_NEEDS` and `PET_SAFE` are facets everywhere, and
  mean something only among plants. ADR 0056 already lists it as owed.
- **The assistants, measured over the larger catalogue and not better for it**
  (their own documents carry the tables). Steward: 25, 24 and 25 of 28, where
  three runs over a hundred products gave 26 each — every failure one seen
  before. Tino: 22 and 24 of 28, where it gave 25 and 24; one case fails in
  both runs that had not failed in five — *do you ship to Portugal?*, answered
  after reading the shopper's orders — and why is not known yet.

## Evidence of use

Nothing new to instrument: the products are reached, found and bought through
what already measures that (`tendero.search.*`, the funnel, the dashboard's
sales by category), and a new family shows there by its category's name.
