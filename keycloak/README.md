# keycloak/

Everything Tendero gives Keycloak, the real identity provider: the realm a fresh
clone imports, and the theme its sign-in page wears. Neither is a feature of the
shop — no aggregate changes, no slice, no event — so neither is specified under
`docs/specs/`. They are platform, like the AppHost and the tokens, and they serve
the storefront and the backoffice alike: both apps send their shoppers and their
shopkeepers to the same page.

Keycloak is one of two adapters of the identity port (invariant 10, ADR 0017); the
development issuer is the other, and it has no sign-in page of its own to dress.

```
keycloak/
  realms/tendero-realm.json        # the realm, imported on a first start
  themes/tendero/login/            # the sign-in page in the shop's look (ADR 0065)
    theme.properties               # parent keycloak.v2, the stylesheets, the languages
    resources/css/tendero.css      # PatternFly's variables answered with the shop's tokens — hand-written
    resources/js/tendero.js        # light or dark from the device, before the first paint — hand-written
    resources/css/tokens.css       # GENERATED from design/theme.css
    resources/css/fonts.css        # GENERATED from design/fonts.css
    resources/img/tendero-icon.svg # GENERATED from the storefront's brand icon
```

## Running it

```bash
Tendero__Issuer=keycloak dotnet run --project src/AppHost   # or profile https-all
```

Without the switch Keycloak still appears in the Aspire dashboard, unstarted, one
click from running. With it the API trusts Keycloak instead of the development
issuer — one configuration value, no code path. Keycloak answers on
`https://localhost:38443`.

| who | user / password | role |
|---|---|---|
| a shopper | `ana` / `ana` | `shopper` |
| the shopkeeper | `juanlu` / `juanlu` | `shopkeeper` |
| an agent (client credentials) | client `claude-desktop` | `agent` |
| the admin console | `admin` / `admin` | — |

The passwords are committed on purpose: a development realm with a password worth
protecting is a password that should not have been committed (the AppHost says so
beside the admin's).

## The realm

`realms/tendero-realm.json` holds the roles, the two clients (`tendero-app`, public,
for both front ends; `claude-desktop`, a service account for an agent) and the
users above. It also turns the shop's languages on: `internationalizationEnabled`,
`supportedLocales: ["es", "en"]`, `defaultLocale: "es"` — the shop's own default
culture (invariant 6) — and `loginTheme: "tendero"`.

**Imported customers** (phase 16, S2b; ADR 0067) add three things:

- **`tendero-provisioning`**, a confidential client whose service account holds
  `realm-management`'s `manage-users`, `view-users` and `query-users` and nothing
  else. The API uses it, when the issuer is Keycloak, to make an identity with no
  password for each customer an import brings. Its development secret is in the
  file, for the reason the admin password is (`keycloak-provisioning-secret` in the
  AppHost).
- **`resetPasswordAllowed: true`**: an imported customer sets their password with
  *forgot password*, which also proves the address is theirs.
- **`smtpServer`**: `mailpit:1025`, the AppHost's Mailpit, which catches every
  email and shows it at `http://localhost:38025`. Nothing reaches a real inbox.

A machine that started Keycloak before this has none of the three: see the next
paragraph. Changing the running realm by hand means creating the client with a
service account and those three roles, turning *Forgot password* on (Realm settings
→ Login) and setting the SMTP host and port (Realm settings → Email).

**An import happens once.** `--import-realm` skips a realm that already exists, and
the realm lives in the `tendero-keycloak-data` volume, so an edit to this file does
not reach a machine that has started Keycloak before. Two ways to bring it in:

- **Change the running realm by hand** in the admin console (Realm settings), or
  through the admin REST API, to match the file. The data is kept.
- **Delete the volume** (`docker volume rm tendero-keycloak-data`, with the stack
  stopped) so the next start imports the file afresh. Everything Keycloak held goes
  with it: the users are recreated with **new subject ids**, so to the API Keycloak's
  `ana` is a new customer with no orders, addresses or saved cards.

**The admin account is also created once.** Keycloak creates `admin` from
`KC_BOOTSTRAP_ADMIN_*` only when the volume has no admin yet. A volume created
before the AppHost fixed the password to `admin` (it was generated per machine and
kept in the AppHost's user secrets) keeps the old one, and `admin` / `admin` is
refused. The password is in the AppHost's user secrets of that time; failing that,
the volume has to go. `kc.sh bootstrap-admin` cannot help while the stack runs: the
development database is an H2 file the server holds locked.

## The sign-in page

Keycloak's own login theme, `keycloak.v2` (PatternFly 5), dressed in the shop's
tokens, typefaces and mark (ADR 0065). It replaces no template: every step, error
and translation Keycloak renders stays Keycloak's, and only how it looks is the
shop's — the awning icon and the wordmark in the header, the page and card
surfaces, fields boxed with the strong hairline, the one clay action, the shop's
focus ring, light and dark.

**How it reaches the page.** The AppHost bind-mounts `themes/tendero` read-only to
`/opt/keycloak/themes/tendero`, and the realm names it as its `loginTheme`. Beside
that, `KC_SPI_THEME__CACHE_THEMES=false`, `KC_SPI_THEME__CACHE_TEMPLATES=false` and
`KC_SPI_THEME__STATIC_MAX_AGE=-1` — Keycloak's advice for working on a theme: an
edit to `tendero.css` shows on the next reload, not after a restart.

**A realm imported before the theme existed does not wear it** until it is chosen
once: admin console → the `tendero` realm → Realm settings → Themes → Login theme
→ `tendero` (the same screen turns the languages on, under Localization).

**Never make it the server's default** (`KC_SPI_THEME__DEFAULT=tendero`). It was,
for a day, to spare that click: that variable is the default of *every* theme type
— login, account, admin, welcome, email — and this theme has only a login. The
welcome page answered *The theme is null*, and the admin console and the account
pages a 500 (`Failed to find ACCOUNT theme tendero`, then a
`NullPointerException`). Measured on two throwaway Keycloaks, with and without it.

**The focus is the shop's for a text field** (`design/DESIGN.md`, *Where the ring
sits*): 2px of the focus colour drawn **on** the field's edge (`outline-offset:
-1px`), one line, on the field's box (`:has(> :focus-visible)`) and never on the
bare input inside it. PatternFly draws that box with square pseudo-elements; they
take the field's corner. The password and its eye are one control: the input group
owns the corners and the focus, and the field inside draws none, as input groups do
in shadcn, Nord and other systems. (It was a ring 2px outside first, the shop's
rule then; the author found it a second line round a field already focused, and the
rule changed for every text field in both apps.) Edge's own eye inside a
password field is hidden (`::-ms-reveal`, Microsoft's advice for a page with its
own), or there are two.

**Light and dark.** The shop's tokens turn dark with `data-theme="dark"` on
`<html>`; Keycloak turns PatternFly dark with a class on the same element, from the
same media query. `tendero.js` sets the attribute the tokens read, in `<head>`,
before the first paint. The overrides in `tendero.css` sit on the components
themselves (`html.login-pf .pf-v5-c-button.pf-m-primary`), because PatternFly's dark
theme sets the components' variables there too, and a value on `<html>` lost to it
— the first attempt left the dark button blue and the dark fields without a box.

**The language.** The page is in the shop's language because the shop says which:
`signIn()` in `libs/shared/auth` passes `ui_locales` with `<html lang>`, which both
front ends keep in step with their culture (`CultureStore`, `libs/shared/i18n`). Keycloak honours it on a realm
with its languages turned on; without it, it answers in the realm's default, `es`.
Its own picker stays on the page, for the shopper who wants the other one.

**Generated files.** `node tools/tokens.mjs` writes `tokens.css`, `fonts.css` and
the icon from `design/` and the brand, as it writes `design/tokens.css`; CI diffs
the theme's `resources/` with the rest, and `design-tokens.spec.ts` fails when the
copies differ from their sources byte for byte. Never edit them here.
`tendero.css` follows the repository's colour rule like every other stylesheet: no
hex, only tokens, and the token test reads it.

## Checking a change

Keycloak serves the page; nothing renders it in a unit test. After a change to the
theme or the realm:

1. Run the stack with `Tendero__Issuer=keycloak` and sign in from the storefront,
   `/es/account` and `/en/account`, in light and dark (a browser's colour-scheme
   emulation does), and once at phone width. Focus each field from the keyboard,
   and look once in **Edge** too: it draws things into a password field that
   Playwright's Chromium does not. Then open `https://localhost:38443/admin/`: the
   admin console must still be Keycloak's own.
2. To see what a **fresh clone** gets — the realm imported from the file, which the
   stack's volume may no longer reflect — run a throwaway Keycloak with no volume:

   ```bash
   docker run --rm -p 127.0.0.1:39180:8080 --memory=1280m --cpus=2 \
     -e KC_BOOTSTRAP_ADMIN_USERNAME=admin -e KC_BOOTSTRAP_ADMIN_PASSWORD=admin \
     -v "$PWD/keycloak/realms:/opt/keycloak/data/import:ro" \
     -v "$PWD/keycloak/themes/tendero:/opt/keycloak/themes/tendero:ro" \
     quay.io/keycloak/keycloak:26.6 start-dev --import-realm
   ```

   and open its authorisation endpoint for `tendero-app` with and without
   `ui_locales`. Stop it when done; `--rm` removes it.

**On a Keycloak upgrade**, look at the page before anything else: the theme leans
on PatternFly's variable names and on `keycloak.v2`'s markup (`#kc-header-wrapper`,
`.pf-v5-c-login__main-header`). A renamed variable fails silently, as Keycloak's
default look coming back.
