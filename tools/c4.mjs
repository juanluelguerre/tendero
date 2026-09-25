#!/usr/bin/env node
// The C4 model in docs/c4/tendero.c4: validated, drawn, and held to the AppHost.
//
//   node tools/c4.mjs     validate the model, check it against the AppHost,
//                         and rewrite docs/c4/README.md with its diagrams
//
// CI runs it and then `git diff --exit-code -- docs/c4/`, the pattern the
// generated API types already follow: a model edited without regenerating the
// page, or a page edited by hand, fails the build (ADR 0035).
//
// LikeC4 is run through npx at an exact version and is not a dependency of
// anything: it reads a text file and writes Mermaid, and nothing ships with it.

import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const LIKEC4 = 'likec4@1.59.4';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const modelDirectory = join(root, 'docs', 'c4');
const appHost = join(root, 'src', 'AppHost', 'AppHost.cs');

// AppHost resources that are not containers of the model, each with its reason.
// A resource missing from both the model and this list is a failure, so adding
// one to the AppHost forces a decision about how it is drawn.
const NOT_CONTAINERS = {
  'tendero-db': 'a database inside the postgres container, not a process of its own',
  elasticvue: 'a development tool behind Tendero__Tools, not part of the system',
  keycloak: 'drawn as the external identity provider it is in production (decided 2026-09-23)',
};

function likec4(...args) {
  // One command string: npx is a .cmd on Windows, which needs a shell, and Node
  // deprecates handing a shell an argument array. Every argument here is ours.
  const command = ['npx', '--yes', LIKEC4, ...args.map((a) => `"${a}"`)].join(' ');
  const run = spawnSync(command, { cwd: root, encoding: 'utf8', shell: true });
  if (run.status !== 0) {
    console.error(run.stdout, run.stderr);
    throw new Error(`likec4 ${args[0]} failed with exit code ${run.status}.`);
  }
  return run;
}

/**
 * The landing page draws levels 1 and 2 by hand, for people rather than for
 * GitHub (landing-page/site/index.html, "How it fits together"). Hand-drawn is
 * exactly how article 00's diagram came to show a Grafana nobody ran, so the
 * drawing is held to the model both ways: every element of the model is a box
 * with its id in `data-c4`, and the relations beside the drawing are the model's
 * relations, no more and no fewer.
 */
function landingDisagreements(model) {
  // The landing stays out of the public repository until the two histories
  // converge (docs/blog/publish-public.ps1), so a public clone has no folder to
  // check. Only the whole folder missing excuses it: a moved page must still fail.
  if (!existsSync(join(root, 'landing-page'))) return [];
  const page = readFileSync(join(root, 'landing-page', 'site', 'index.html'), 'utf8');
  const drawn = [...page.matchAll(/data-c4="([^"]+)"/g)].map((m) => m[1]);
  const relationsJson = /<script type="application\/json" id="arch-relations">([\s\S]*?)<\/script>/.exec(page)?.[1];
  const elements = Object.keys(model.elements);
  const endpoint = (end) => (typeof end === 'object' ? end.model : end);
  // Titles included: the page tells a reader what each connection does, in the
  // model's words, so those words are held to the model too.
  const modelled = Object.values(model.relations).map((r) => `${endpoint(r.source)} -> ${endpoint(r.target)} (${r.title})`);
  const pageRelations = relationsJson ? JSON.parse(relationsJson).map(([from, to, title]) => `${from} -> ${to} (${title})`) : [];

  // Nothing read means nothing compared: a renamed attribute must not pass.
  if (!drawn.includes('tendero.api') || pageRelations.length === 0) {
    return ['Could not read the landing page\'s architecture drawing; the check would prove nothing.'];
  }

  return [
    ...elements.filter((e) => !drawn.includes(e)).map((e) => `The landing's architecture drawing has no box for "${e}".`),
    ...drawn.filter((e) => !elements.includes(e)).map((e) => `The landing draws "${e}", which the model does not have.`),
    ...modelled.filter((r) => !pageRelations.includes(r)).map((r) => `The landing's drawing is missing the relation ${r}.`),
    ...pageRelations.filter((r) => !modelled.includes(r)).map((r) => `The landing draws the relation ${r}, which the model does not have.`),
  ];
}

function fail(message) {
  console.error(message);
  process.exit(1);
}

// 1 · The model is valid: every reference resolves.
likec4('validate', modelDirectory);

// 2 · Its containers are the AppHost's resources, both ways.
const scratch = mkdtempSync(join(tmpdir(), 'tendero-c4-'));
try {
  const exported = join(scratch, 'model.json');
  likec4('export', 'json', '-o', exported, modelDirectory);
  const model = JSON.parse(readFileSync(exported, 'utf8'));
  const containers = Object.keys(model.elements)
    .filter((id) => id.startsWith('tendero.'))
    .map((id) => id.slice('tendero.'.length));

  // `.Add…("name"` in the AppHost, parameters aside: they are values, not resources.
  const resources = [...readFileSync(appHost, 'utf8').matchAll(/\.Add(?!Parameter\b)\w+(?:<[^>]*>)?\(\s*"([a-z0-9-]+)"/g)]
    .map((m) => m[1]);

  // A pattern that matches nothing would pass every comparison below.
  if (!resources.includes('api') || !containers.includes('api')) {
    fail(`Could not read the resources (${resources.join(', ') || 'none'}) or the containers (${containers.join(', ') || 'none'}); the check would prove nothing.`);
  }

  const expected = resources.filter((r) => !(r in NOT_CONTAINERS));
  const missing = expected.filter((r) => !containers.includes(r));
  const extra = containers.filter((c) => !resources.includes(c));
  const staleExceptions = Object.keys(NOT_CONTAINERS).filter((r) => !resources.includes(r));

  const problems = [
    ...missing.map((r) => `The AppHost declares "${r}" and the model has no container for it — draw it, or say in NOT_CONTAINERS why not.`),
    ...extra.map((c) => `The model draws "${c}" and the AppHost declares no such resource.`),
    ...staleExceptions.map((r) => `NOT_CONTAINERS explains "${r}", which the AppHost no longer declares.`),
    ...landingDisagreements(model),
  ];
  if (problems.length) fail(problems.join('\n'));

  // 3 · The diagrams, as Mermaid GitHub renders, one per view.
  const mermaid = join(scratch, 'mermaid');
  likec4('gen', 'mermaid', '-o', mermaid, modelDirectory);
  const views = readdirSync(mermaid)
    .filter((f) => f.endsWith('.mmd'))
    .sort((a, b) => (a === 'index.mmd' ? -1 : b === 'index.mmd' ? 1 : a.localeCompare(b)));

  const page = [
    '# Tendero in C4',
    '',
    '> **Generated — do not edit.** Drawn from [`tendero.c4`](tendero.c4) by',
    '> `node tools/c4.mjs`, which also fails when the model\'s containers and the',
    '> AppHost\'s resources disagree (ADR 0035).',
    '',
    'Levels 1 and 2 of the [C4 model](https://c4model.com): who uses Tendero, and',
    'the processes and stores it runs on. Level 4 is the domain model, generated',
    'from the code in [`../domain-model.md`](../domain-model.md). Containers tagged',
    '`optional` start only when their capability is configured; the UCP agent is',
    'tagged `planned` for phase 11.',
    '',
    ...views.flatMap((file) => [
      '```mermaid',
      readFileSync(join(mermaid, file), 'utf8').replaceAll('\r\n', '\n').trimEnd(),
      '```',
      '',
    ]),
  ].join('\n');

  writeFileSync(join(modelDirectory, 'README.md'), page);
  console.log(`docs/c4: model valid, ${containers.length} containers match the AppHost, ${views.length} views written.`);
} finally {
  rmSync(scratch, { recursive: true, force: true });
}
