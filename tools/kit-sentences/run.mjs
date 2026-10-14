#!/usr/bin/env node
// K-6's golden set: sentences a shopper might write above the kits, each with
// the form it should become, sent to a running stack's
// `POST /api/kits/understand` (phase 13c; docs/specs/kit-solver.md, K-6).
//
//   node tools/kit-sentences/run.mjs [api base, default http://localhost:5130]
//
// It measures the whole slice as a shopper meets it: the model choosing the kit
// (or none) and code reading the budget and the requirements. A sentence that
// names no kit is right when nothing is understood. The model answers the same
// question at temperature zero, so a second run is the same run; what moves the
// number is the model, the prompt, or a sentence added here.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const api = process.argv[2] ?? 'http://localhost:5130';
const golden = JSON.parse(readFileSync(join(dirname(fileURLToPath(import.meta.url)), 'golden.json'), 'utf8'));

const status = await fetch(`${api}/api/kits/understand/status`).then((r) => r.json());
if (!status.available) {
  console.error('The stack has no storefront model (Tino off): nothing to measure.');
  process.exit(1);
}

const same = (a, b) => JSON.stringify([...a].sort()) === JSON.stringify([...b].sort());
const tally = { kit: 0, budget: 0, requirements: 0, whole: 0 };
for (const row of golden) {
  const started = performance.now();
  const response = await fetch(`${api}/api/kits/understand?culture=${row.culture}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sentence: row.sentence }),
  });
  const got = await response.json();
  const ms = Math.round(performance.now() - started);

  const kit = (got.understood ? got.template : null) === row.template;
  // With no kit named, the budget and the requirements are not asked.
  const budget = row.template === null || (got.budget?.amount ?? null) === row.budget;
  const requirements = row.template === null || same(got.requirements ?? [], row.requirements);
  const whole = kit && budget && requirements;
  tally.kit += kit;
  tally.budget += budget;
  tally.requirements += requirements;
  tally.whole += whole;

  const said = got.understood ? `${got.template} ${got.budget?.amount ?? '-'} [${(got.requirements ?? []).join(',')}]` : 'not understood';
  console.log(`${whole ? 'ok  ' : 'MISS'} ${String(ms).padStart(5)} ms  ${row.culture}  ${row.sentence}\n          -> ${said}`);
}

const n = golden.length;
const share = (k) => `${tally[k]}/${n} (${((tally[k] / n) * 100).toFixed(1)} %)`;
console.log(`\nkit ${share('kit')} · budget ${share('budget')} · requirements ${share('requirements')} · whole ${share('whole')}`);
