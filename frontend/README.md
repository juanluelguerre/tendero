# frontend

The Nx workspace holding Tendero's two Angular 22 applications.

```bash
nvm use                      # Node 24: Angular 22 requires >= 24.15 (see .nvmrc)
npm install

npx nx serve storefront      # http://localhost:4200
npx nx serve backoffice      # http://localhost:4201
```

Usually there is no need to start them by hand:
`dotnet run --project src/AppHost` brings these two up as well, with the API's
URL injected.

## `npx nx …` in a terminal, `nx …` in a script

Nx is a **local** dependency, not a global one. That single fact decides which
of the two you write, and getting it backwards is the sort of thing that costs
ten minutes and reads like a broken machine:

| Where | Write | Why |
|---|---|---|
| A terminal, by hand | `npx nx run-many -t lint` | `npx` resolves the binary out of `node_modules/.bin`. Without it the shell looks on `PATH`, finds nothing, and says `nx: command not found` — or worse, finds a **globally installed Nx of a different version** and runs that against this workspace |
| Inside `package.json` scripts | `nx run-many -t lint` | npm already puts `node_modules/.bin` on `PATH` for the script it is running, so `npx` is redundant there |
| A CI step | `npx nx …` | A runner's shell is a terminal like any other |

The same rule applies to `playwright`, `tsc` and every other local binary. The
short version: **the prefix is about who is resolving the command, not about
what the command is.**

## Layout

```
apps/
  storefront/       the shop. Roomy, light, one clay action per view
  backoffice/       dense, dark by default, 36px rows
  storefront-e2e/   Playwright: the shopper and the browser agent (WebMCP)
  backoffice-e2e/   Playwright: the shopkeeper. The browser half of the pyramid, kept thin
libs/shared/
  tokens/           bridge to design/tokens.css plus Tailwind's @theme block
  ui/               primitives with NO identity: inputs, tables, focus ring
  util/             helpers: the API base URL token, price and date formatting, image URLs
  api/              the API contract's types, generated from the OpenAPI document
  i18n/             the Transloco wiring and the culture store (not the translations)
  auth/             the OIDC redirect sign-in, the token interceptor, the route guard
  agent/            the WebMCP registration mechanism, feature-detected (the tools stay per app)
```

Inside each app: `layout/` (its own shell, which is **not** shared),
`features/` (one folder per feature, twins of the backend's vertical slices),
`data-access/` (the only folder that talks HTTP) and, in the storefront,
`agent/` (the tools it offers a browser agent).

## A component is three files

```
cart-page.ts      the class
cart-page.html    the template
cart-page.css     the styles
```

Same name, three extensions, which is what the current Angular style guide
describes. The old "extract anything over three lines" rule is gone from it, and
the reason this repository keeps the split anyway is concrete rather than
stylistic: a template in a `.ts` literal is invisible to every HTML tool, and a
stylesheet in one is invisible to every CSS tool — including the test that
enforces the first rule below.

## The two apps do not know about each other

Not by convention: by a lint rule that breaks the build.

| from | may import from |
|---|---|
| `scope:storefront` | `scope:shared` — and nothing else |
| `scope:backoffice` | `scope:shared` — and nothing else |
| `scope:shared` | `scope:shared` — it does not know the apps exist |

It is the TypeScript twin of the backend's NetArchTest rules. There is no
mutation tooling here; what there is, is the habit of checking a new rule by
breaking something on purpose and watching it go red — which is how the boundary
rule, the Pricing confinement rule and the design-token rule were each verified
the day they were written.

**What is shared and what is duplicated**: what is hard because of its
*behaviour* is shared (a DatePicker is calendar logic, keyboard handling and
accessibility); what is hard because of its *identity* is duplicated (the two
layouts share nothing but the word). Argued in `docs/adr/0010`.

## Rules that are not negotiable

- **Not one hex outside `design/tokens.css`.** Components read tokens. This one
  is now a test — `libs/shared/i18n/src/lib/design-tokens.spec.ts` scans every
  `.css` in the repository — and it only became possible once the styles left
  the TypeScript files.
- **Not one user-facing literal outside the Transloco files**, and always in es
  *and* en. Each app has its own: the shop's voice and the backoffice's are not
  the same.
- **Not one backend URL in the code.** The dev server proxies to the address
  Aspire injects; `proxy.conf.mjs` is the only place with a fallback localhost.
- **Not one `HttpClient` outside `data-access/`.** A screen and an agent tool
  reach the API through the same service, so a URL, a header and an error shape
  exist once. An eslint rule refuses the import under `features/**` and
  `agent/**`; the day a page needs one, the service it should call is missing.

## Commands

```bash
npx nx run-many -t lint      # includes the boundary rules and the token rule
npx nx run-many -t test
npx nx run-many -t build
npx nx affected -t build     # only what your change touched

npx nx e2e storefront-e2e    # needs the stack up: dotnet run --project src/AppHost
npx nx e2e backoffice-e2e
```
