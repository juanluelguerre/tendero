# 0046 — A day is the shop's day

**Status**: accepted (the author, 2026-09-29, while R-9's first series was built)

## Context

R-9 draws sales by day, so the server has to decide which day each order was
placed on. Orders are stored in UTC; Madrid is UTC+2 in summer and UTC+1 in
winter. An order at 23:30 UTC on a Saturday is Sunday at 01:30 in Madrid, and
on the last Sunday of October the clocks go back and the day has 25 hours.
Deciding needs the IANA time zone database — the rules per region, by names
such as `Europe/Madrid`.

The plan asked `TimeZoneInfo` for the reader's zone, and the tests failed on the
first run: *"The time zone ID 'Europe/Madrid' was not found on the local
computer."* The backend runs with `InvariantGlobalization` (in
`Directory.Build.props` since the first commit), which keeps numbers, dates and
comparisons the same on every machine and every container. On Windows, that mode
leaves .NET with no way to resolve an IANA name: the translation from IANA to
Windows zones needs ICU, which invariant mode turns off. On Linux it depends on
whether the image ships `tzdata`, and the smallest do not.

Three ways out were weighed with the author:

1. **NodaTime** (Apache-2.0, tier 1): the reference date-and-time library for
   .NET, carrying the IANA database inside the process.
2. **Switching invariant mode off**: no dependency, but every culture-sensitive
   call in the backend changes behaviour — `59,90` on one machine, `59.90` on
   another — and the images would need ICU and `tzdata`.
3. **Days in UTC**: nothing to add, and a Madrid night's sales counted on the day
   before.

And one more question behind them: **whose** day. The plan said the reader's, sent
by the browser. The references do otherwise: Shopify reports in the store's time
zone, a setting of the shop; Seller Central in the marketplace's; Stripe and Google
Analytics in the account's. One day for everyone — the shopkeeper in Madrid, an
employee in the Canaries, the monitor on the wall.

## Decision

1. **A day is the shop's calendar day.** `ShopOptions.TimeZone` (section `Shop`,
   `Europe/Madrid` by default) in the SharedKernel, as a string: more than one
   context will need "a day", and none of them should carry the dependency to
   read the setting.
2. **NodaTime 3.3.4 resolves it**, referenced by Ordering only, where the
   dashboard's series computes the start of each day (`AtStartOfDay`, so a day
   whose midnight does not exist still starts). Invariant mode stays.
3. **The zone is validated when the API starts** (`ValidateOnStart`): a zone
   nobody has is a sentence at startup, never a 500 on the first dashboard.
4. **Times stay in UTC everywhere** — stored, compared, sent. The zone is applied
   only where a day is counted, and the series carries it so the browser labels
   each bar with the same clock.

## Consequences

- One dependency more, tier 1, pinned; NodaTime ships a new time zone database
  when a country changes its rules, a few times a year, and updating it is a
  version bump.
- Hours are absolute (sixty minutes each); days are calendar days, 23 or 25
  hours on the days the clocks change.
- The period totals (`GET /api/dashboard/sales`) are still rolling windows ending
  now; the series is on calendar days. Plan 2 decides whether the totals follow.
- A shop in another country changes one setting. Several shops in several zones
  — the suite's multi-tenancy — would make it a property of each shop.
