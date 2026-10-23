// Gives a Shopify theme Tendero's look, from Tendero's own tokens (phase 16,
// case B, the store in front). The theme itself is Shopify's Horizon, pulled with
// the Shopify CLI into the ignored artifacts/shopify-theme: its licence keeps a
// derived theme out of a public repository, so this file and what it writes are
// all the repository holds of it.
//
// It reads design/theme.css — the tokens the storefront, the backoffice and the
// Keycloak sign-in page already take their look from (tools/tokens.mjs) — and:
//   - writes Horizon's own settings (config/settings_data.json): the palette,
//     the buttons, the inputs, the badges, the radii and the typefaces;
//   - writes assets/tendero.css for what the settings cannot say (prices in the
//     receipt's mono face), and links it from layout/theme.liquid.
//
// Run from the repository root:
//   shopify theme pull --store <store> --theme <id> --path artifacts/shopify-theme
//   node tools/shopify-theme/build.mjs
//   shopify theme push --unpublished --theme Tendero --path artifacts/shopify-theme --store <store>
import fs from 'node:fs';
import path from 'node:path';

const THEME = process.argv[2] ?? path.join('artifacts', 'shopify-theme');
const settingsFile = path.join(THEME, 'config', 'settings_data.json');
const layoutFile = path.join(THEME, 'layout', 'theme.liquid');
if (!fs.existsSync(settingsFile)) throw new Error(`No theme at ${THEME}: pull it with the Shopify CLI first.`);

// ---------- the tokens, as the light theme declares them ----------
const css = fs.readFileSync('design/theme.css', 'utf8');
const declared = {};
for (const [, name, value] of css.matchAll(/--([a-z0-9-]+):\s*([^;]+);/gi)) declared[name] ??= value.trim();

/** A token's value, following var() to the end. */
function token(name) {
  let value = declared[name];
  if (value === undefined) throw new Error(`design/theme.css declares no --${name}`);
  for (let hops = 0; value.startsWith('var(') && hops < 10; hops++)
    value = declared[value.match(/var\(--([a-z0-9-]+)\)/i)[1]];
  return value;
}
const px = (name) => Number.parseInt(token(name), 10);

// ---------- Horizon's settings ----------
const raw = fs.readFileSync(settingsFile, 'utf8');
const header = raw.match(/^\/\*[\s\S]*?\*\/\s*/)?.[0] ?? '';
const data = JSON.parse(raw.slice(header.length));
const settings = data.current;

Object.assign(settings, {
  color_palette: {
    background: token('color-page'),
    foreground: token('color-body'),
    color1: token('color-accent'),
    color2: token('color-hairline'),
  },
  page_background_color: token('color-page'),
  page_text_color: token('color-body'),

  // One clay action per view: the primary button is the accent, the secondary
  // a quiet outline in the body's ink.
  palette_primary_button_background: token('color-accent'),
  palette_primary_button_text: token('color-on-accent'),
  palette_primary_button_border: token('color-accent'),
  palette_secondary_button_background: token('color-surface'),
  palette_secondary_button_text: token('color-body'),
  palette_secondary_button_border: token('color-hairline-strong'),

  palette_input_background: token('color-surface'),
  palette_input_text: token('color-body'),
  palette_input_border: token('color-hairline-strong'),

  palette_variant_background: token('color-surface'),
  palette_variant_text: token('color-body'),
  palette_variant_border: token('color-hairline-strong'),
  palette_selected_variant_background: token('color-body'),
  palette_selected_variant_text: token('color-surface'),
  palette_selected_variant_border: token('color-body'),

  badge_sale_background_color: token('color-accent'),
  badge_sale_text_color: token('color-on-accent'),
  badge_sold_out_background_color: token('color-surface'),
  badge_sold_out_text_color: token('color-muted'),

  drawer_background_color: token('color-surface'),
  drawer_text_color: token('color-body'),
  drawer_border_color: token('color-hairline'),
  popover_background_color: token('color-surface'),
  popover_text_color: token('color-body'),
  popover_border_color: token('color-hairline'),
  popover_shadow_color: token('color-body'),

  button_border_radius_primary: px('radius-md'),
  button_border_radius_secondary: px('radius-md'),
  inputs_border_radius: px('radius-md'),
  card_corner_radius: px('radius-lg'),
  popover_border_radius: px('radius-lg'),
  product_corner_radius: px('radius-lg'),

  // Shopify's font library: Instrument Sans for the body, Bricolage Grotesque
  // for headlines only (design/theme.css, *Type*).
  type_body_font: 'instrument_sans_n4',
  type_subheading_font: 'instrument_sans_n6',
  type_heading_font: 'bricolage_grotesque_n8',
  type_accent_font: 'bricolage_grotesque_n8',
});

fs.writeFileSync(settingsFile, header + JSON.stringify(data, null, 2) + '\n');

// ---------- the home page's hero ----------
// tools/shopify-theme/hero.mjs renders and uploads the two pictures. Horizon
// lays a hero's picture across the whole screen and keeps its content to the
// page's width, so the background is only the stone, the awning's stripes and
// the floor, and the shop front is an image block in the content: on the right,
// lined up with the products below. On the left, a group: the headline in the
// headline face and the body's ink, and the view's one clay action. No dark
// overlay over a light picture.
const indexFile = path.join(THEME, 'templates', 'index.json');
const indexRaw = fs.readFileSync(indexFile, 'utf8');
const indexHeader = indexRaw.match(/^\/\*[\s\S]*?\*\/\s*/)?.[0] ?? '';
const index = JSON.parse(indexRaw.slice(indexHeader.length));
const hero = Object.values(index.sections).find((section) => section.type === 'hero');
if (hero) {
  Object.assign(hero.settings, {
    image_1: 'shopify://shop_images/tendero-hero.png',
    toggle_overlay: false,
    // As tall as what it holds, with a breath above and below: the template's
    // 100 and 72 px of padding and a minimum height left a band of empty page.
    section_height: 'auto',
    'padding-block-start': 16,
    'padding-block-end': 16,
    content_direction: 'row',
    horizontal_alignment: 'space-between',
    vertical_alignment: 'center',
  });

  hero.blocks = {
    tendero_words: {
      type: 'group',
      settings: { content_direction: 'column', gap: 24, width: 'fit-content', width_mobile: 'fill' },
      blocks: {
        tendero_headline: {
          type: 'text',
          settings: {
            text: '<h1>Todo lo de casa,<br>del mostrador a tu puerta</h1>',
            type_preset: 'h1',
            text_color: token('color-body'),
            font: 'var(--font-heading--family)',
            width: 'fit-content',
          },
        },
        tendero_action: {
          type: 'button',
          settings: { label: 'Ver catálogo', link: 'shopify://collections/all', style_class: 'button' },
        },
      },
      block_order: ['tendero_headline', 'tendero_action'],
    },
    tendero_shopfront: {
      type: 'image',
      settings: { image: 'shopify://shop_images/tendero-shopfront.png', width: 'custom', custom_width: 34, width_mobile: 'fill' },
    },
  };
  hero.block_order = ['tendero_words', 'tendero_shopfront'];

  fs.writeFileSync(indexFile, indexHeader + JSON.stringify(index, null, 2) + '\n');
}

// ---------- no department cards ----------
// Tried on 2026-10-23 and taken out by the author: the menu above already leads
// to the three departments, so cards repeating it under the hero were noise.
// Their collections keep the department art (tools/shopify-theme/departments.mjs)
// for the collection pages.
const home = JSON.parse(fs.readFileSync(indexFile, 'utf8').slice(indexHeader.length));
delete home.sections.tendero_departments;
home.order = home.order.filter((key) => key !== 'tendero_departments');
fs.writeFileSync(indexFile, indexHeader + JSON.stringify(home, null, 2) + '\n');

// ---------- no announcement bar ----------
// Horizon opens every page with "Welcome to our store", which says nothing the
// store does not; the author asked for it gone (2026-10-23). Disabled rather
// than deleted, so the merchant can bring it back from the editor.
const headerGroupFile = path.join(THEME, 'sections', 'header-group.json');
const headerGroupRaw = fs.readFileSync(headerGroupFile, 'utf8');
const headerGroupHeader = headerGroupRaw.match(/^\/\*[\s\S]*?\*\/\s*/)?.[0] ?? '';
const headerGroup = JSON.parse(headerGroupRaw.slice(headerGroupHeader.length));
for (const section of Object.values(headerGroup.sections))
  if (section.type === 'header-announcements') section.disabled = true;

// The awning rule is drawn by tendero.css inside the header's own section, so
// it stays under the menu when the page scrolls. A section of its own, tried
// first, scrolled away beneath a sticky header.
delete headerGroup.sections.tendero_awning;
headerGroup.order = headerGroup.order.filter((key) => key !== 'tendero_awning');
fs.writeFileSync(headerGroupFile, headerGroupHeader + JSON.stringify(headerGroup, null, 2) + '\n');

// ---------- what the settings cannot say ----------
const ownCss = `/* Tendero's look on Horizon, written by tools/shopify-theme/build.mjs from
   design/theme.css. Prices speak the receipt's voice: JetBrains Mono. */
@import url('https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;500&display=swap');

:root {
  --tendero-font-mono: ${token('font-mono')};
  --tendero-accent: ${token('color-accent')};
  --tendero-accent-hover: ${token('color-accent-hover')};
}

.price,
.price-item,
product-price,
.cart-items__price {
  font-family: var(--tendero-font-mono);
  font-variant-numeric: tabular-nums;
}

a:hover {
  color: var(--tendero-accent-hover);
}

/* The signature: the awning rule under the store's menu, as under our
   storefront's (design/theme.css, .awning-rule) — the system's one decorative
   device, a rule and never a background fill. It is the last thing inside the
   header's own section, which is what Horizon makes sticky, so it stays under
   the menu as the page scrolls. (On #header-group it drew nothing: a sticky
   header turns that wrapper into display: contents.) */
.header-section::after {
  content: "";
  display: block;
  height: 6px;
  background: repeating-linear-gradient(
    90deg,
    ${token('color-clay-500')} 0 14px,
    ${token('color-clay-100')} 14px 28px
  );
}
`;
fs.writeFileSync(path.join(THEME, 'assets', 'tendero.css'), ownCss);

const link = "{{ 'tendero.css' | asset_url | stylesheet_tag }}";
const layout = fs.readFileSync(layoutFile, 'utf8');
if (!layout.includes(link)) fs.writeFileSync(layoutFile, layout.replace('</head>', `    ${link}\n  </head>`));

console.log(`Tendero's look written into ${THEME}: settings, assets/tendero.css, and its link in layout/theme.liquid.`);
