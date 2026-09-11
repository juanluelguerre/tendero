# ADR 0028 — Catalogue prices include VAT; tax is extracted, never added

Status: accepted · 2026-09-11 · Amends [ADR 0016](0016-quoted-live-frozen-at-order.md)

## Context

Every price Tendero shows is the price a shopper in Spain pays: the product page
says 79,95 €, the cart line says 59,96 € after the summer sale, and the summary
says "IVA incluido". That is also what the law requires of a shop selling to
consumers in the EU — the displayed price includes VAT.

The pricing context disagreed, silently. `ITaxCalculator` was written for net
prices: it took each amount as a base and returned 21 % of it, and the quote
added that on top — `total = net + shipping + tax`. So the UI review of
2026-09-11 bought a pair of shoes shown at 59,96 € and was charged **72,55 €**.
The cart's lines added up to 78,33 € and its total said 94,78 €, under a line
reading "IVA incluido · 16,45 €". The refund of a returned line added a share of
the order's tax again — which, because the order's tax included the shipping's,
also handed back VAT on a delivery that is not refunded.

Nothing was red. The calculator's tests asserted 21 on a base of 100, the quote's
tests asserted the sum the code produced, and `CheckoutLoopTests` asserted that
the order froze whatever the quote said. Every test agreed with every other test,
and none of them was about what the shopper saw.

## Decision

**Catalogue prices, price-list entries, promotion thresholds and shipping rates
are VAT-inclusive. A tax calculator extracts the VAT an amount contains; it never
adds any.**

- `TaxableAmount` carries `Inclusive`, renamed from `Net` so that no caller reads
  it as a base to add tax to. The rename is what made the compiler list every
  call site.
- A calculator answers, per tax class, a **base** and the **tax inside** what
  was paid. The base is rounded once and the tax is the remainder, so
  `Base + Amount` is what the class paid **to the cent**; the contract suite
  holds every adapter to that, and a CsCheck property over 2,000 random baskets
  holds `flat-vat` to it.
- A quote's `Total` is `Subtotal − DiscountTotal + Shipping`. `TaxTotal` says how
  much of it is VAT — a breakdown of the total, not an increment to it.
- `Order.Place` refuses totals that do not satisfy that equation. It still does
  not recompute anything (ADR 0016); it refuses to freeze numbers that
  contradict themselves. Had it existed, no order could have been saved with the
  tax added on top.
- A refund is the returned lines' price less their share of the discount. The
  VAT went back with the price.

The promotion engine needed no change: it never knew whether amounts were net or
gross, and "free shipping over 50 €" and "5 € off from 40 €" now mean the
tax-inclusive figures a shopper reads — which is what they always said.

## Consequences

- The shop charges what it displays. The same basket that was charged 94,78 €
  is charged 78,33 €, of which 13,59 € is VAT.
- Every number in the repository that was derived from the old arithmetic moved:
  the quote examples in the tests, the 72,55 € refund `docs/testing.md` used as
  its example, and any demo figure quoted from before this date. They are not
  comparable with the new ones.
- **Orders already in a development database keep their old totals.** They were
  frozen by design, and they no longer satisfy the equation above — which is
  harmless, because the check runs on placing and never on loading.
- A **B2B** shop quoting net prices would be a second mode, not a flag inside
  this calculator: prices would be net everywhere, the total would add the tax,
  and the display would say so. Nothing in this laboratory sells B2B, so the
  mode is not built — the day it is, it is a decision about the whole price
  model, and that is what this ADR is for.
- Destination-based VAT (OSS, a second country) is unchanged by this: it was
  already a third adapter keyed on `TaxRequest.DestinationCountry`, and it
  extracts like this one.
