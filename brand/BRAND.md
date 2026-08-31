# Tendero — brand basics

**Tendero** is Spanish for *shopkeeper*: the corner-shop owner who knows every
customer by name, recommends what actually fits, and remembers what you bought
last time. That is exactly what an AI-native commerce platform should do — for
humans and for agents.

## The mark

A letter **T** whose crossbar is a striped shop awning. Reads as a storefront at
any size and as the initial of the name.

## Files

| File | Use |
|---|---|
| `tendero-logo.svg` | Horizontal lockup — README header, blog posts, slides |
| `tendero-icon.svg` | Square icon — GitHub org/social preview, app icon |
| `tendero-icon-mono.svg` | Single-colour (`currentColor`) — dark UIs, print, stickers |
| `tendero-favicon.svg` | Simplified 32 px — favicon, browser tabs |

### Per-surface variants

The two Angular apps are one brand with two surfaces, so they share the mark and
differ only by an initial set on the awning in terracotta. The stripes underneath
are untouched, which is what the rules below require.

| File | Use |
|---|---|
| `tendero-logo-shop.svg` | Storefront lockup — README, blog, slides. Icon at 76 px, not 60: in a 64-72 px header the original lockup left the awning too small to read. |
| `tendero-logo-backoffice.svg` | Same lockup, `Backoffice` suffix |
| `frontend/apps/storefront/public/brand/tendero-icon-shop.svg` | Storefront app icon and favicon |
| `frontend/apps/backoffice/public/brand/tendero-icon-backoffice.svg` | Backoffice app icon and favicon |

The two app icons live inside their app and not here, and that is not an
oversight: Angular refuses an asset path outside the workspace root
(`The ../brand asset path must be within the workspace root`), so the CSS bridge
that lets both apps read `design/tokens.css` has no equivalent for assets. Each
icon is used by exactly one app, so giving it one home there duplicates nothing.
The lockups stay in `brand/` because their consumers are the README and the
articles, not the apps.

**What the initial does not survive.** At 16 px the letter is a smudge and the
two tabs look alike; the letter earns its place from 32 px up, which is what
modern displays actually request. Distinguishing them at 16 px would take a
different silhouette or a different background colour, and both cost more brand
coherence than the problem is worth today.

## Colours

| Token | Hex | Use |
|---|---|---|
| Terracotta (primary) | `#D85A30` | Icon background, wordmark |
| Canvas | `#FAECE7` | Awning and stem on terracotta |
| Deep terracotta | `#993C1D` | Hover, text on light terracotta tints |
| Ink | `#2C2C2A` | Mono icon background, body text |
| Muted | `#888780` | Tagline, secondary text |

## Tagline

> commerce for humans and agents

Spanish variant, for elguerre.com: *comercio para personas y agentes*.

## Rules

- Keep clear space around the mark equal to the height of the T stem.
- Never stretch, rotate, or re-colour the awning stripes.
- On dark backgrounds use the mono version with `color: #FFFFFF`, or the
  terracotta icon as-is (it holds up on both).
- The stripes are decorative: at sizes below 24 px use `tendero-favicon.svg`,
  which drops them.
