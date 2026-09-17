# Flows

Images of how Tendero works as a shop, for the documentation and for sharing.
Each one explains a commerce flow and shows the pattern behind it.

**A diagram is a claim, and it goes stale like any other.** Every statement in
these images was checked against the code when it was drawn. The file comment at
the top of each `.html` names the types it was checked against. When one of those
types changes, redraw the image from its `.html`. Do not edit the `.png`.

Each image comes in two formats:

| format | size | used for |
|---|---|---|
| `-landscape` | 1200×627, exported at 2× | the link card on LinkedIn and X |
| `-portrait` | 1080×1350, exported at 2× | a 4:5 image post on LinkedIn |

The `.html` is the source. The `.svg` and `.png` are exported from it with the
procedure in `docs/blog/index.md` → *Diagrams*: a headless Chromium through
`frontend/node_modules/playwright-core`, `deviceScaleFactor: 2`, and a screenshot
of the `<svg>`. They use the tokens in `design/tokens.css`. Clay marks only what an
agent does, or what the shop refuses or undoes (`design/DESIGN.md`).

| # | flow | checked against |
|---|---|---|
| 01 | An order, end to end: cart, checkout, placed, stock saga, shipped, delivered, and the return | `Order.AllowedTransitions`, `PlaceOrder`, `StockSaga`, `ReturnRequest` |
