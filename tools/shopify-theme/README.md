# shopify-theme

Gives the development store's own storefront Tendero's look (phase 16, the
store in front), from Tendero's own tokens. The theme underneath is Shopify's
**Horizon**.

**Horizon is not in this repository, and must not be.** Its licence lets a
derived theme serve one merchant, but not be published or redistributed, and
this repository will be public. So the repository keeps only **our** part: the
scripts here, which write Tendero's look into a copy of Horizon that you
download yourself. That copy lives in `artifacts/shopify-theme`, which git
ignores (`artifacts/` is ignored as a whole), and the design tests read only
what git tracks, so it is never checked as if it were ours.

## What you need

- **Node** and the frontend's packages (`npm ci` in `frontend/`). `hero.mjs`
  and `departments.mjs` render with the Playwright the frontend already
  installs.
- **The Shopify CLI**, installed on your machine and not as a dependency of the
  repository: `npm install -g @shopify/cli` (4.9.4 was used, MIT). The first
  theme command asks you to sign in by device code.
- **Staff access to the store**, to pull and push themes.
- **The store's credentials in the AppHost's user secrets**, for the two scripts
  that upload pictures (`tools/shopify-seed/README.md`, *Before running*). The
  app needs `write_files`, and `departments.mjs` also needs `write_products`.

## 1. Get Horizon into `artifacts/shopify-theme`

The script reads `artifacts/shopify-theme` unless you give it another folder
(`node tools/shopify-theme/build.mjs <folder>`). The folder must exist before
the CLI writes into it.

**From the store (recommended).** Every new Shopify store has Horizon
installed, and this gives you exactly the version the store runs:

```bash
mkdir -p artifacts/shopify-theme
shopify theme list --store <store>.myshopify.com          # note Horizon's id
shopify theme pull --store <store>.myshopify.com --theme <id> --path artifacts/shopify-theme
```

If the store no longer has it: *Online Store → Themes → Add theme → Horizon*
(free, from Shopify's Theme Store), then list and pull as above.

**From GitHub.** Shopify publishes Horizon's source at
[github.com/Shopify/horizon](https://github.com/Shopify/horizon):

```bash
git clone https://github.com/Shopify/horizon.git artifacts/shopify-theme
```

Its `main` may carry features the stores do not run yet, so prefer the store's
copy. Either way, read its `LICENSE.md`; the same terms apply to what you build
from it.

## 2. Build, upload, preview

Run from the repository root, in this order:

```bash
node tools/shopify-theme/hero.mjs          # renders the hero's two pictures and uploads them to the store's Files
node tools/shopify-theme/build.mjs         # writes Tendero's look into artifacts/shopify-theme
shopify theme push --unpublished --theme Tendero --path artifacts/shopify-theme --store <store>.myshopify.com
node tools/shopify-theme/departments.mjs   # the departments' pictures on their collections (after tools/shopify-seed/collections.mjs)
```

- `hero.mjs` uploads `tendero-hero.png` and `tendero-shopfront.png`. The theme's
  settings name them (`shopify://shop_images/…`), so they must be in the store
  before you preview it. A picture already there is replaced.
- `build.mjs` reads `design/theme.css` and writes:
  - Horizon's settings (`config/settings_data.json`): the palette, buttons,
    inputs, badges, radii and typefaces;
  - the home page (`templates/index.json`) and the header group
    (`sections/header-group.json`, with the announcement bar disabled);
  - `assets/tendero.css`, for what the settings cannot say: prices in the
    receipt's mono face, and the awning rule under the menu. It links that file
    from `layout/theme.liquid` once, so it can run again on a copy it already
    built.
- `--unpublished` uploads the result as a new theme called *Tendero*, beside
  Horizon, which stays as it was. Preview it from *Online Store → Themes*, and
  publish it there when it is right. Publishing is the store owner's decision,
  never a script's.

To change the look later, change the tokens in `design/theme.css`, then run
`build.mjs` and push again. Pull the *Tendero* theme instead of Horizon if you
want to start from what the store already shows.
