// Chooses the Amazon Berkeley Objects pictures the picture suite asks about
// (docs/specs/camera-search.md, Measures): per category of the shop, a few
// listings whose main picture is a product of that kind; per common object the
// shop does not sell, a few more. Each case's right answer comes from ABO's own
// product type through tools/picture-eval/abo-kinds.json, so none is labelled by
// hand.
//
//   node tools/picture-eval/choose.mjs [abo metadata folder]
//
// The metadata (listings_*.json.gz and images.csv.gz, about 90 MB) is read from
// artifacts/picture-eval/abo-meta, outside git. Out: the manifest,
// tools/SearchEval/Objects/abo.json — ids and answers, never a picture. Each
// kind takes its first pictures by a seeded hash of their ids, so running it
// again chooses the same pictures.
import { readFileSync, readdirSync, writeFileSync, mkdirSync } from "node:fs";
import { gunzipSync } from "node:zlib";
import { dirname, join, resolve } from "node:path";

const root = resolve(import.meta.dirname, "..", "..");
const meta = resolve(
  process.argv[2] ?? join(root, "artifacts", "picture-eval", "abo-meta"),
);
const out = join(root, "tools", "SearchEval", "Objects", "abo.json");
const kinds = JSON.parse(
  readFileSync(join(import.meta.dirname, "abo-kinds.json"), "utf8"),
);

// Pictures looked at and left out, each with why: skipped before the choice,
// so a replacement is drawn and looked at in turn.
const excluded = JSON.parse(
  readFileSync(join(import.meta.dirname, "abo-excluded.json"), "utf8"),
);

const PER_CATEGORY = 5;
const PER_NOT_SOLD = 3;
// A picture smaller than this on its long side would be enlarged to stand for a
// shopper's 1,536-pixel upload, and an enlarged picture is not a photograph.
const MIN_LONG_SIDE = 1000;
const SEED = "2026-10-19";

/**
 * A picture's place in its kind's draw: FNV-1a of the seed and its id. Each
 * picture's rank is its own, so leaving one out brings in the next of that kind
 * and moves nothing else — a shared shuffle would redraw every kind after it.
 */
function rank(image) {
  let hash = 0x811c9dc5;
  for (const ch of SEED + image) {
    hash ^= ch.charCodeAt(0);
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }
  return hash;
}

const images = new Map();
for (const line of gunzipSync(readFileSync(join(meta, "images.csv.gz")))
  .toString()
  .split("\n")
  .slice(1)) {
  const [id, height, width, path] = line.split(",");
  if (id) images.set(id, { height: +height, width: +width, path });
}

// One candidate a picture: the same product is listed in several marketplaces
// under one main picture, and it is one question, not several.
const candidates = new Map();
for (const file of readdirSync(meta)
  .filter((f) => f.startsWith("listings_"))
  .sort()) {
  for (const line of gunzipSync(readFileSync(join(meta, file)))
    .toString()
    .split("\n")) {
    if (!line) continue;
    const listing = JSON.parse(line);
    const type = listing.product_type?.[0]?.value;
    const picture = images.get(listing.main_image_id);
    if (!picture || candidates.has(listing.main_image_id)) continue;
    if (excluded[listing.main_image_id]) continue;
    if (Math.max(picture.width, picture.height) < MIN_LONG_SIDE) continue;
    // ABO's types are noisy; the English name has to say what the picture is.
    // A type may hold several of the shop's kinds (ABO's SHOES are running
    // shoes and loafers alike), so its rules are tried in order.
    const name = listing.item_name?.find((n) =>
      n.language_tag?.startsWith("en"),
    )?.value;
    if (!name) continue;
    const sold = [kinds.sold[type] ?? []].flat();
    const notSold = [kinds.notSold[type] ?? []].flat();
    const says = (r) =>
      new RegExp(r.name, "i").test(name) &&
      !(r.not && new RegExp(r.not, "i").test(name));
    const rule = sold.find(says) ?? notSold.find(says);
    if (!rule) continue;
    candidates.set(listing.main_image_id, {
      image: listing.main_image_id,
      path: picture.path,
      width: picture.width,
      height: picture.height,
      item: listing.item_id,
      type,
      kind: rule.kind,
      sold: sold.includes(rule),
      name,
    });
  }
}

const byKind = Map.groupBy(candidates.values(), (c) => c.kind);
const cases = [];
for (const [kind, group] of [...byKind].sort(([a], [b]) => a.localeCompare(b))) {
  const take = group[0].sold ? PER_CATEGORY : PER_NOT_SOLD;
  cases.push(...group.toSorted((a, b) => rank(a.image) - rank(b.image)).slice(0, take));
}

mkdirSync(dirname(out), { recursive: true });
writeFileSync(
  out,
  JSON.stringify(
    {
      source:
        "Amazon Berkeley Objects (https://amazon-berkeley-objects.s3.amazonaws.com/index.html), CC BY 4.0. Pictures are fetched by tools/picture-eval/prepare.mjs and never kept in the repository.",
      chosenBy: "tools/picture-eval/choose.mjs",
      cases,
    },
    null,
    2,
  ) + "\n",
);

const sold = cases.filter((c) => c.sold).length;
console.log(
  `${cases.length} cases: ${sold} of the shop's kinds in ${new Set(cases.filter((c) => c.sold).map((c) => c.kind)).size} categories, ${cases.length - sold} not sold`,
);
for (const [kind, group] of byKind)
  if (group.length < (group[0].sold ? PER_CATEGORY : PER_NOT_SOLD))
    console.log(`  short: ${kind} has ${group.length}`);
