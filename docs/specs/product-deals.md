# Spec: the promotions a product is in

**Status**: building (phase 13c, 2026-10-14)
**Slice(s)**: Pricing/Domain (`ProductDeals`), Search (`SearchHit.Deals`, `DealBook`, `search_products`), SharedKernel (`DealTerms`, `DealText`), storefront `product-card`
**Article**: none yet — material for the chapter on the assistant (what grounding cannot invent)

## Problem

The author searched "menaje": 33 kitchenware cards, and none said a word about
the 5 € the autumn campaign takes off an order with kitchenware in it. Then they
asked Tino *muéstrame menaje con esos 5 € de descuento*, and Tino could not
answer. Run five times with the model's text kept, three times it tried to show
a baking dish *because it is in the 5 € promotion*. Grounding dropped the reason
every time, because nothing said that of any product. The till knew it all along:
`PromotionCondition.Reaches` is how a promotion finds its lines. It said it
nowhere a shopper could read before checkout.

"5 € en menaje" is not *on offer* (`OfferReach`). It takes 5 € off the order, so
the pan's own price does not move, and the *on offer* filter rightly leaves it
out. This is the other half: **the promotions a product is in**.

## Behavior

- **Pricing decides** (`ProductDeals.Reaching`). A product's deals are the
  promotions that:
  - anybody gets now, without a code or a segment (`Promotion.IsPublicAt`);
  - **name what they are about**, a category or an item;
  - reach the product by the till's own rule.

  Free shipping over 50 € reaches everything, so it says nothing of any product;
  on every card it would be wallpaper. The deals come in the till's order:
  priority, then code.
- **Search carries it** on every card it draws (`SearchHit.Deals`: the search,
  the cards endpoint, the home's rows). It is asked of Pricing once per page,
  through the one adapter that may read Pricing (`PricingRunningOffers.DealsNowAsync`
  → `DealBook`), and never indexed: a window opens and closes with no event, as
  with *on offer*. A reader composed without Pricing (the worker, the search
  evaluation) draws its cards with none.
- **As data on the wire** (ADR 0059): a deal is its code, its name in the
  reader's language, and `DealTerms`. The terms are the kind (an enum), the
  figure for that kind (percent, `amountOff` as `Money`, buy/free), and the
  minimums (`minimumSubtotal` as `Money`, `minimumQuantity`).
- **The card** shows the first deal in one quiet line under the price, in its own
  words: *5,00 € menos en tu pedido desde 40,00 €*, *3x2*, *10 % menos*. It uses
  body text and not the accent, because the card's one accent is *Añadir al
  carrito*. The words are composed in `deal-words.ts`, and money goes through
  `formatPrice`.
- **Tino** gets one fact per deal on each product `search_products` finds: *Bandeja
  de horno entra en «5 € en menaje»: 5,00 € menos en el pedido si suma al menos
  40,00 €.* The terms are worded once, in `DealText`, and the campaign's sentence
  (`list_campaigns`) uses the same function. Said of a product, an order
  discount needs only its minimum, because the product is the item it needs.
- **Nothing on offer, but in a promotion** (seen live the same night). With the
  stack restarted, Tino searched kitchenware *on offer* and answered only *No
  hay nada en oferta ahora que coincida con «menaje»*. That was true, and no help
  to a shopper asking about a 5 € promotion. Now, when nothing like the words is
  on offer, `search_products` asks again without the filter and answers with
  what matches and is in a promotion, each with its terms. "See all" then leads
  to that search. Code decides, not the model; with nothing in a promotion
  either, the shop's *nothing on offer* sentence stands.
- **The product page** (2026-10-15, the follow-ups). It reads the catalogue
  (`GET /api/catalog/products/{code}`), and Catalog does not reference Pricing, so
  it asks Pricing by the SKU on screen: `GET /api/pricing/deals/{sku}`
  (`ListProductDeals`). The branch is the one the till prices with
  (`IPricedItemReader`), and the rule is `ProductDeals`, so the page, the card
  and the basket agree. The product store reads it again when another variant
  is chosen or the language changes, and a failure is no line at all
  (invariant 8). The line uses the card's words (`deals/deal-words.ts`).
  `ProductDeal` moved to the shared kernel, beside `DealTerms`, because the
  search and Pricing both answer it.

## Non-goals

- **The saving on a card.** What a promotion takes off a particular basket is
  the quote's to say, and a card says the terms, not a computed saving.
- **More than one deal on a card.** The shop has one per branch today. A second
  would need the line to decide which goes first, and the till's order already
  says which.

## Measures

- `ProductDealsTests` (Pricing): the branch, nothing for a promotion about no
  category or item, only what anybody gets now, and the till's order.
- `ProductDealsOnCardsTests` (Search): a card in the branch carries the deal,
  named in the reader's language with its terms; a card elsewhere, or a reader
  without Pricing, carries none.
- `DealTextTests`, and `SearchProductsToolTests`: the fact in both languages.
- `listing.spec.ts`: the line under the price, and nothing on a card in no
  promotion.
- The `assistant` suite: `offers-the-5-euros-after-the-campaign-es`, the author's
  own turn, now expects kitchenware cards citing *entra en «5 € en menaje»*.
  Alone it passed 0 of 5 before the deals, 3 of 5 with them, and **5 of 5** once
  "nothing on offer" fell back to what is in a promotion. Before that fallback,
  the two misses asked back with a figure in the question, which a question back
  may not carry (`TinoReply.IsAQuestion`). The whole suite: run 32, 32 of 37,
  every offers case passing.
- **The cost**: every kitchenware product found carries the same deal fact, so a
  read of kitchenware grows by about twenty-five tokens a product. The largest
  call in run 32 was *¿alguna oferta para hoy?*, at 5,224 tokens (about 3,700
  before): the model searched products on offer and read the campaigns too.
  Worth watching, not yet worth a rule.

## Evidence of use

**No event of its own, on purpose.** A line on a card is not an action, and a
"deal seen" event per card per page would be volume with no decision behind it.
The question is whether the line moves anything: do orders that qualify for a
category's order promotion grow? That is read from the orders, through the
promotions applied to them. If that never moves, the line comes off the card.

## Open questions

- None now. The product page shows it since 2026-10-15 (Behaviour).
