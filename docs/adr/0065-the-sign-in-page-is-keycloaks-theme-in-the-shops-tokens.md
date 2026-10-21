# ADR 0065 — The sign-in page is Keycloak's own theme in the shop's tokens, never templates of ours

**Status**: accepted (2026-10-21, after phase 15)

## Context

With Keycloak as the issuer (ADR 0017), signing in leaves the shop for a page the
shop did not draw: Keycloak's default look, the realm's name in capitals, a blue
button, and English whatever language the shopper was browsing in. It is the first
page of every signed-in session, in the storefront and the backoffice alike, and it
looked like somebody else's site.

Keycloak offers three ways to change it. **Templates**: copy its FreeMarker `.ftl`
files into a theme and edit them, which gives full control of the markup. **A
theme with styles only**: extend the shipped `keycloak.v2` theme and add
stylesheets, which changes how the page looks and nothing it says. **Keycloakify**
or a hand-written single-page app: rebuild the page in a front-end framework,
which is a build, a dependency and a second app to keep.

Each template copied is a fork of Keycloak's: every upgrade can change the markup,
the variables a template is given, or a step's flow, and a forked template keeps
rendering the old one — a broken page, or a missing security fix, at the next
version. The page shows many more screens than sign-in (errors, a forgotten
password, an expired link, required actions), and a template set would have to
cover each.

## Decision

**The theme extends `keycloak.v2` with stylesheets and one script, and copies no
template.** `keycloak/themes/tendero/login` declares `parent=keycloak.v2` and adds
the shop's typefaces, its tokens and one hand-written stylesheet, `tendero.css`,
which answers PatternFly 5's CSS variables with the shop's tokens and styles the
few elements the variables do not reach (the header's mark, the card). Everything
Keycloak renders — every step, every error, every translation — stays Keycloak's.

**The tokens are the shop's, generated, never copied by hand.** `tools/tokens.mjs`
writes the theme's `tokens.css`, `fonts.css` and icon from `design/` and the brand,
CI diffs them, and the token test holds them byte for byte. The colour rule holds:
no hex in `tendero.css`, which the token test reads like every stylesheet.

**The language is the shop's.** The realm turns on `es` and `en` with `es` as its
default (invariant 6), and both front ends ask for the page's own language with
`ui_locales` when they send someone to sign in.

**It is platform, not a feature.** It lives in `keycloak/` beside the realm and is
documented in `keycloak/README.md`, not in `docs/specs/`: no aggregate, slice or
event is involved, and both apps share it.

## Consequences

- An upgrade of Keycloak cannot leave a stale template behind. What can break is
  quieter: a renamed PatternFly variable or a changed selector brings Keycloak's
  default look back on that element. The README says to look at the page first on
  every upgrade.
- What the page says cannot be changed. Its wording is Keycloak's, in its own
  translations ("Iniciar sesión", not the shop's "Identifícate"), and its layout is
  PatternFly's. A screen that needs words or structure of the shop's own is the day
  this decision is revisited — with one template, for that screen, not a set.
- PatternFly's dark theme sets component variables on the components, so the
  overrides sit there too, with a selector that outranks its `:where()`. Light and
  dark follow the device, as the shop does, through `data-theme` set before the
  first paint.
- No dependency was added: no Keycloakify, no build. The theme is four files of
  ours and three generated.
- A realm imported before this change gets neither the theme nor the languages,
  because an import happens once; they are chosen once in its admin console, or the
  volume goes. `keycloak/README.md` describes both. **The theme is never the
  server's default** (`KC_SPI_THEME__DEFAULT`): that is the default of every theme
  type, and with only a login theme here the admin console and the account pages
  failed — tried for a day, measured, and removed.
- The theme follows the shop's focus for a text field — 2px drawn on the field's
  edge, one line (`design/DESIGN.md`) — on PatternFly's box, which takes the
  field's corner, with the password and its eye as one input group that owns it.
  The first version kept a ring 2px outside; the author found it a second line, and
  the rule changed for every text field in the shop, the backoffice and here.
