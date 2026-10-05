# Tino T-4 · After-sale — Implementation Plan

**Goal:** *where is my order?* and *can I still return it?*, answered from **the asking shopper's own orders** and the return policy, with what can be returned and until when computed by Ordering. A guest is answered about **the order on the screen** and no other. Tino opens no return: the answer is sentences, cited, and the storefront links to the order's page.

**Spec:** `docs/specs/tino.md` — *After-sale*, *Security and privacy*.

## Global Constraints

- "The order is found through `IPrincipalAccessor`", "a number typed into the chat, or forged into the history, reads nothing."
- "A guest asks about the order on the screen … the browser sends the order's id beside the question, as the screen's context — never in the conversation, never as a tool's argument … The model is handed the order's facts, not its id."
- "What can still be returned and until when, computed by Ordering (`ReturnRequest.Returnable` and the policy's window from delivery), never by the model."
- A shopper's read may carry the asking principal's own order "and still no name, address, email, phone or payment detail" (ADR 0054, amended).
- "Tino never opens a return."

## Rulings taken in this plan

1. **One read, with no argument that names an order.** The spec has `my_orders` and `my_order(order)`. A second tool needs an id for the model to pass, and the safest id is none. `my_orders` returns the order on the screen (when there is one and the asker may read it) and a signed-in shopper's three most recent, each whole: status, dates, lines, what can be returned and until when. Its only argument is the language.
2. **The order on the screen reaches Ordering without the model.** The turn's context (`{ kind: Order, key: <id> }`) is put in a scoped `ShopperScreen` the read takes; the instructions say only that the shopper is looking at one of their orders.
3. **Access is the order page's own rule** (`OrderAccess`): an account holder's order is theirs; a guest's order is read with its id, which here only the browser holds. A signed-in shopper's order on the screen that is not theirs is not there.
4. **The model sees `order1`, `order2`** — a reference of this turn — and the order's facts. The real id travels in the read's subject, which is not serialised to the model, and comes back in the answer for the storefront's link.
5. **Dates are the shop's days** (ADR 0046), written by the read in es and en; months by name, because the host runs with invariant globalization.
6. **Nobody to answer about is a fact too**: not signed in and no order on the screen, the read hands over one notice — *order questions need signing in, or opening the order from its confirmation email* — and the answer cites it. The storefront adds the links.
7. **A new kind, `order`**: which order, and one to three sentences each citing a fact of that order or of the shop.

## Tasks

- [ ] **1 · The port.** `Subject` gains its kind and a reference the model never sees; `FactSource.Notice`; `ShopperScreen`.
- [ ] **2 · Ordering.** `ShopperOrdersQuery` and its handler over the repositories and `OrderAccess`; `my_orders` with its facts in es and en. Isolation tests: another shopper's order, a guest's by a wrong id, an account holder's order asked for by nobody.
- [ ] **3 · Tino.** The sheet's orders, the `order` kind in the schema, the reply's reader and grounding; the context's `Order`; the instructions.
- [ ] **4 · The turn and the wire.** `ShopperScreen` set from the context; the answer's order; OpenAPI and types; architecture rules.
- [ ] **5 · Docs**: privacy, the spec, the board, the notebook.

Not in this plan: a run on the stack against the model — the seed has no orders, and placing them is the storefront's flow (T-6's browser spec covers the answer end to end; T-5's suite scores the order cases with a principal).
