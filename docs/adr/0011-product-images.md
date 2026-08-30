# 0011 — Product images are ingested, not referenced

**Status**: accepted

## Context
Catalogue sources hand over images in different ways: Shopify serves URLs from
its own CDN, a Merchant feed gives URLs, the seed connector has files on disk,
and the phase-4 document scanning produces bytes with no URL at all. The first
model stored the source's `Uri` inside the `Product` aggregate.

Three things were wrong with it. The domain held a foreign delivery URL, so
changing CDN or domain would be an `UPDATE` over millions of rows. The port
assumed every image is a URL, so the scanning case did not fit. And `Alt` was a
plain `string`, which is user-facing text and therefore violates invariant 6.

## Decision
**Copy the bytes at import time and store our own.** The connector's
`ExternalImage` carries a `Uri` that may be `http(s)` or `file`, which covers
both a remote CDN and local content without a type hierarchy; an
`IExternalImageReader` resolves either. The import reads the content, stores it
through `IImageStore`, and the aggregate keeps **the key**, never a URL.

**Identity is the content hash (SHA-256).** Deduplication is free — the same
photo across products is stored once — and a key can never change content, so
responses are served `immutable` with a one-year max-age. Changing a product's
photo changes its key; there is no cache to invalidate.

**The URL is composed at read time.** The index stores the key; the browser
requests `/api/images/{key}`. A CDN goes in front without touching the index,
the domain, or a migration.

`IImageStore` has one adapter today, on the filesystem. S3-compatible storage
(SeaweedFS locally — Apache-2.0, unlike MinIO which is AGPL and was archived in
April 2026 — R2/S3/B2 in production) is another adapter when the filesystem
stops being enough. Same shape as the catalogue connectors and payment
providers: one port, N adapters.

The deciding argument is phase 4, not tidiness: **multimodal search cannot be
built on images we do not control.** If the source deletes a photo, the vector
points at nothing and cannot be recomputed. Ingestion is a prerequisite of the
roadmap, not an improvement to it.

## Consequences
Importing costs bandwidth and storage, and an unreadable image no longer aborts
anything — the product enters without it and the next import retries. Derivatives
(thumbnails, WebP/AVIF) are deferred: they are a layer over the same port, the
URL shape already accommodates them (`?w=400&fmt=webp`), and with six images
there is nothing to measure. They land with the full ABO import.
