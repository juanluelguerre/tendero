# Spec: the shopper's account — addresses, saved ways to pay, preferences

**Status**: accepted 2026-10-20 (`P15-1` of `docs/delivery-plan.md`, phase
15). Written 2026-10-18; the author took every decision as proposed, confirmed
the six-year period and settled the open questions (*Decisions for the author*,
at the end). The mock-ups follow the decisions, not the other way round.
**Mock-ups**: accepted 2026-10-20, `design/unpublished/mockups/storefront-account.pen`
(Pencil, English), exported to `storefront-account/`: ten boards, `01-account` to
`10-phone`, each light and `-dark`, each with a note naming the decisions it draws. The
way in is the header's user icon, whose menu gains *Profile*; the header carries no
*Account* link of its own (the author, 2026-10-20).
**Slice(s)**: `Accounts` (an address book, the profile, consent, the shopper's
data and erasure); `Ordering` (a wallet of saved methods, and the payment port
it needs); the storefront (the account's sections, and checkout reading them).
**Article**: *The shop remembered my address, and nothing it should not* — what
a customer's account may hold, what it must not, and where the line runs through
the payment port.

## Problem

The account page is one screen: who you are, sign out, your orders. Everything a
returning shopper has told the shop before, they tell it again at every
checkout — the address, the card, the language. A shop people come back to
remembers more than that, and the floor (CLAUDE.md, *Product vision*) names
accounts and idempotent payments as table stakes.

The hard part is not the screens. It is three lines that have to be drawn before
any of them: **which facts are the identity provider's** and are only shown;
**which are the shop's** and the shopper edits; and **what the shop may keep of a
card**, which is a token the provider minted and a label, never the card.

## What exists today, measured 2026-10-18

Read from the code before writing, because three things the delivery plan
assumes are not there:

| The plan says | The code has |
|---|---|
| `Customer` carries language and currency | `Culture` and `Segment`. **No currency**: the shop sells in one, `ShopOptions.DefaultCurrency = "EUR"` |
| order emails always, marketing by consent | **No mailer.** No email is sent anywhere, and the API reads no `email` claim |
| consent recorded with its date, as analytics consent is | Analytics consent is `localStorage` (`tendero.analytics-consent`): `granted`, `denied` or `unasked`, **with no date, and never sent to the server** |

And what the account can build on:

- `Customer` (`Accounts`): a guest is a customer with no `Subject`; signing in
  links the issuer's `sub`, idempotently; a guest merged into an account is
  superseded, never deleted. It holds a `DisplayName` the issuer sends.
- `Address` (SharedKernel): a value, frozen onto an order (ADR 0002). Editing an
  address can never move last month's parcel.
- `BrowsingHistory` (`Accounts`): a second aggregate keyed by the customer, with
  its own settings and its own `DELETE` — the pattern an address book follows.
- `IPaymentProvider` (`Ordering`): authorise, capture, refund, void, verify a
  webhook. `PaymentRequest.Instrument` is **already an opaque token** standing
  in for a card, and the one field never logged. What the port lacks is a way to
  keep one: today every instrument is single-use by convention.
- Checkout (`POST /api/checkout`) takes the shipping address, the billing
  address and the instrument **as values**, and the order freezes them.

## The line: whose fact is it

| Fact | Whose | In the account |
|---|---|---|
| name, email, password, second factor | **the identity provider's** (invariant 10) | shown, read-only, with *change it at your sign-in provider* linking to the issuer's own page when it has one (Keycloak's account console; the development issuer has none). The shop keeps `DisplayName` as a cache the next sign-in overwrites, and **does not store the email** |
| the language the shop speaks to you in | the shop's | edited (`Customer.Culture`, which exists) |
| addresses, and which is the default | the shop's | an address book |
| saved ways to pay, and which is preferred | **a token is the provider's**; the label and the choice of default are the shop's | a wallet. The shop never sees a card number, not even in transit |
| what the shop may send you | the shop's, as a consent with its date | preferences |
| the tariff (`Segment`) | the shop's, set by the shopkeeper | not shown; it is not the shopper's to change |
| orders | the shop's, and the law's | as today |

**A screen that lets a shopper edit what the issuer overwrites at the next
sign-in is a bug by design** (the plan's risk), so there is no *edit your name*
form anywhere.

## Behaviour

### Who may use it

Every endpoint is under `/api/accounts/me/…` and needs a **person**. An agent is
a principal, not a customer (invariant 10), and is refused with a problem code —
the in-browser agent (WebMCP) acts within the shopper's own session and sees
what the shopper sees, which is the whole point of that surface. What a
server-to-server agent may use of the account is decision 8.

### Addresses (`P15-2`)

- An **address book** is an aggregate of `Accounts` keyed by the customer, as
  `BrowsingHistory` is — not a collection inside `Customer`, because its
  invariant (one default of each kind, a ceiling) is its own and `Customer` is
  read on every authenticated request.
- An entry is an `Address` (the kernel's value, unchanged) plus a **label the
  shopper writes** (*Home*, *Work*, *Mum's*). The label is the shopper's own
  data, in their words, so it is a plain string and not `LocalizedText`:
  invariant 6 is about what the shop says.
- **One default for delivery and one for billing**, which may be the same entry.
  Add, edit, remove, choose a default.
- **Removing the default leaves no default.** Another entry is never promoted
  silently: a default applied without being shown is an order sent to the wrong
  house, so the next checkout asks.
- **Ten entries at most**: laboratory scale, and a guard on the endpoint, not a
  number anyone will reach. Refused past it with a problem code.
- Editing an entry changes the entry. Orders keep the address they froze.

### Checkout reads the account (`P15-3`)

- **The checkout contract does not change.** The storefront reads the address
  book and sends the chosen address *as values*, exactly as a typed one is sent
  today; the order freezes it. `Ordering` never references `Accounts` or an
  address id (invariant 1), and a guest's checkout is the same request.
- A signed-in shopper with a default sees it **preselected and shown** —
  *Deliver to* **Home** · *Calle …* — with the other entries one choice away
  and *a new address* last. Never a form to retype.
- **A new address typed at checkout can be kept**: a box, off by default. It is
  saved by a second call after the order is placed, so a full book or a failed
  save never stops an order.

### Saved ways to pay (`P15-4`)

The port changes **before** any screen, with `FakePaymentProvider` and the
contract suite in the same change:

- **`SaveInstrumentAsync`**: hands the provider a one-time instrument and gets
  back a **saved instrument** — the provider's token and what may be shown of
  it: brand, last four, expiry month and year. A real provider collects the card
  in its own field (Stripe's Elements, for instance) and the card number never
  reaches Tendero's server; the fake offers its named test cards.
- **`ForgetInstrumentAsync`**: the provider deletes the token. Idempotent:
  forgetting twice is one deletion.
- **Charging a saved one is `AuthorizeAsync`, unchanged**, with the token as the
  instrument: it is already opaque. The contract suite proves a saved token
  authorises, a forgotten one is declined, and an expired one is declined with a
  reason a shopper reads.
- The tokens live in a **wallet**, an aggregate of `Ordering` keyed by the
  customer: `Ordering` owns the payment port, and an `Accounts` that held
  payment tokens would have to reference it, which is the wrong direction.
- A token outlives nothing: removing a method, or the account, forgets it at the
  provider **before** the row goes (*the same mistake made later*, the plan's
  risk). If the provider cannot be reached, the method is marked for forgetting
  and a worker retries it; it is no longer offered at checkout either way.
- An **ADR for the payment token** (`P15-10`): what is kept, what never is,
  which context owns it, and why no PAN, CVC or full expiry date is ever logged
  or stored.

### Preferred way to pay (`P15-5`)

- One method is the **default**, preselected at checkout and shown — *Pay with*
  **Visa ending 4242** — with the others one choice away and *a new card* last.
- **An expired method is said to be expired** in the wallet and at checkout, and
  is not preselected. A default that expired leaves checkout asking.
- **A declined saved method is said so** at checkout, on the same page, with the
  provider's reason and the other methods to choose from. It is never skipped
  over in favour of the next one, and it is not removed: a decline can be
  temporary, and the shopper decides.

### Profile and preferences (`P15-6`)

- **Language**: es or en, `Customer.UseCulture`, which exists. It is what the
  shop speaks when the request names no other (ADR 0013's order is unchanged:
  `?culture=` and `Accept-Language` still win, because they are the request).
- **What the shop may send**: one consent, *marketing*, off until given, kept as
  a domain fact with **its date and the wording's version**, withdrawable, each
  change an audit row. **Nothing is sent yet**, because there is no mailer, and
  the screen says nothing that implies otherwise. Order emails are not a
  preference: when a mailer arrives they are part of an order, not of consent.
- **Analytics consent stays where it is**, in the browser, and the preferences
  section shows it and lets the shopper change it. Moving it to the server would
  tie an anonymous visitor's choice to an account, which is the opposite of what
  it is for.
- No currency: there is one.

### The menu (`P15-7`), last

Built when three sections exist — orders, addresses, ways to pay — and not
before, as the backlog decided on 2026-09-04. On a phone it is a list of the
sections, each its own page; on a wide screen a sidebar.

### What an agent may use (`P15-8`)

Decision 8. The proposal: **in phase 15, nothing.** A server-to-server checkout
(UCP, with or without an AP2 mandate) carries its own address and instrument, as
it does today. Letting a mandate name a saved method needs three things this
phase does not have: a claim in the mandate that names one, a grant the shopper
gives a particular agent for a particular method, and a place in the wallet
where the grant is seen and withdrawn. That is written down as the design it
would be, in this spec, and scheduled when an agent needs it.

### The shopper's own data (`P15-9`)

- **See what the shop holds**: one download, JSON, of the customer, the address
  book, the wallet's labels (never a token), the browsing history and its
  settings, the consents with their dates, and the orders.
- **Remove the account**:
  - forgotten at once: the address book, the wallet (each token forgotten at the
    provider first), the browsing history, the consents;
  - the `Customer` is kept and **anonymised**: `Subject` and `DisplayName`
    cleared, marked erased, so an order's owner still resolves and a later
    sign-in with the same identity starts a new customer;
  - **kept**: the orders, with the addresses they froze, because the law makes
    a shop keep its invoices. The period is the shop's to configure and
    `docs/privacy.md` states it: **six years** by default, Spain's Commercial
    Code (art. 30), confirmed by the author 2026-10-20. What is kept is
    **blocked**, in Spanish law's sense (LOPDGDD art. 32): held for that
    obligation and used for nothing else, which `docs/privacy.md` says too;
  - **not ours**: the identity at the provider. The page says so and links to it.
- Each is a domain operation with an audit row. `docs/privacy.md` gains a
  section for each.

### The degraded path

The account has no AI in it, so invariant 8 has nothing to degrade. What has a
degraded path is the provider: with it unreachable, saving a method answers that
it could not be saved now (a problem code, retryable), checkout with a saved
method answers *unavailable* as a typed one does today, and forgetting retries
from a worker.

## Non-goals

- **Gift vouchers**: a second tender beside the port, a liability the shop
  tracks; they stay on the backlog (*A real account, and the menu it earns*).
- **Editing anything the issuer owns**, or storing the email.
- **Sending email.** The consent is recorded; a mailer is its own work.
- **More than one currency**, a per-country address form, or postcode
  validation per country (`Address` already says why not).
- **Agents using saved addresses or methods** (decision 8).
- **Partial payments, splitting an order across two methods**: the port's own
  comment refuses them.

## Measures

- `Accounts.Tests`: the address book's invariants (one default of each kind,
  removing the default leaves none, the ceiling, an edit never touches an
  order's frozen address); erasure forgets what it says and keeps what it says.
- `Ordering.Tests`: the contract suite gains *save, charge the token, forget,
  charge again → declined*, *an expired token → declined with a reason*, and
  *forget twice → one deletion*, run against the fake and every future adapter;
  the wallet's invariants.
- `Api.Tests`: every new endpoint refuses an agent and an anonymous caller; the
  endpoint-policy test holds them; `docs/openapi/tendero.json` and the frontend
  types regenerated.
- Architecture tests: `Ordering` gains no reference to `Accounts`, nor the
  reverse; a test that no type in either context has a property named like a
  card number or CVC (a guard against the mistake, not proof of its absence).
- Logs and audit: today `Instrument` is kept out of them by convention only (the
  port's comment says so; no `[AuditRedacted]` marks it). The instrument and the
  saved token both get `[AuditRedacted]`, with a test that nothing written
  during save and charge, log line or audit row, contains either.
- Storefront: browser specs for *checkout with a saved address and method in two
  clicks*, *a default removed → checkout asks*, *a declined saved method → said
  so, others offered*.
- The demo in the plan: a returning shopper checks out with *Home* and *Visa
  ending 4242*; then a second address made the default, the card removed, and
  the next checkout asking for a way to pay.

## Evidence of use

In the tracking plan (ADR 0036), each with its question and the decision it
would change:

- **`checkout_prefilled`** — `{ address: saved | typed, method: saved | new }`
  on each order placed by a signed-in shopper. *Do returning shoppers use what
  the account remembers?* If almost every signed-in order still types its
  address, the book is not reachable enough at checkout, and checkout changes
  before the account grows another section.
- **`address_kept_at_checkout`** — the *keep this address* box ticked. *Is
  checkout where the book gets filled?* If yes, the account's own *add an
  address* form is secondary and stays plain.
- **`saved_method_declined`** — `{ reason, expired: bool }`. *Do saved methods
  go stale?* A high share means the wallet must warn before expiry, not at
  checkout.
- **`account_erased`** — a count, no identity. *Is erasure used?* It changes
  nothing in the product; it is the number the article quotes, and the
  evidence the operation is real.

## Decisions for the author

Each had a proposal. **All ten taken as proposed, 2026-10-20.**

1. **The email is not stored**, and name and email are shown read-only from the
   token. *Proposed: yes* — no mailer reads it, and a copy is a second source of
   truth the issuer can contradict.
2. **The address book is its own aggregate in `Accounts`**, keyed by the
   customer, ten entries at most. *Proposed: yes.*
3. **Removing the default leaves no default**; nothing is promoted. *Proposed:
   yes.*
4. **Checkout's contract is unchanged**: addresses travel as values; *keep this
   address* is a second call after the order. *Proposed: yes.*
5. **Saved methods live in a wallet in `Ordering`**; the port gains save and
   forget, and charging a saved token is the existing authorise. *Proposed:
   yes.* The ADR records it.
6. **A declined or expired saved method is said so and never skipped or removed
   automatically.** *Proposed: yes.*
7. **Preferences: language and one marketing consent with its date and wording
   version; no currency; analytics consent stays in the browser.** *Proposed:
   yes* — this corrects the plan's `P15-6`, which assumed a currency and a dated
   analytics consent that do not exist.
8. **Agents use none of it in phase 15**; the mandate-names-a-method design is
   written down and scheduled when needed. *Proposed: yes.*
9. **Erasure anonymises the customer and keeps the orders** for the legal
   period, configurable. *Proposed: yes*; the author confirmed six years, and
   what is kept is blocked (LOPDGDD art. 32).
10. **The mock-ups in Pencil, in English**: the account with its sections (and
    the menu, drawn but built last), the address book and its form, the wallet,
    preferences, *your data*, and checkout prefilled — light, then dark.
    *Proposed: Pencil*, the standing tool; FigCraft only if the author wants a
    second like-for-like run.

## Settled questions

- **Where *managed by your sign-in provider* links.** Keycloak is already the
  stack's real issuer (`Tendero__Issuer=keycloak`, phase 7), and from phase 15
  on the shopper signs in through it (`ana`, role `shopper`, in the committed
  realm). The read-only facts link to **Keycloak's account console**
  (`/realms/tendero/account`), and the link comes from `GET /api/auth/config`
  with the rest of the issuer's facts, so the storefront never builds a
  Keycloak URL. With the development issuer, which has no such page, the
  config carries no link and the section says *managed by your sign-in
  provider* with nothing to click. The dev issuer gains no page of its own.
  To check when it is built: that the realm import keeps Keycloak's default
  `account-console` client, which the committed realm does not list.
- **`P15-6` is resized from S/M to S**: without a currency or a mailer, it is a
  language and one dated consent.
