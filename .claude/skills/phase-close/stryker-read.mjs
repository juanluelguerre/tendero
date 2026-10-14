// Reads a project's newest Stryker report: the scores, and the survivors by
// file — all of them, or only in the files named.
//
//   node .claude/skills/phase-close/stryker-read.mjs <Project> [file filters...] [--detail]
//
// The score that counts is `of covered`: killed ÷ (killed + survived), with
// timeouts counted as killed (docs/testing.md, Level 2).
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', 'src');
const [project, ...rest] = process.argv.slice(2);
if (!project) {
  console.error('usage: stryker-read.mjs <Project> [file filters...] [--detail]');
  process.exit(2);
}
const detail = rest.includes('--detail');
const only = rest.filter((arg) => !arg.startsWith('--')).map((name) => name.toLowerCase());
const ordinal = (a, b) => (a < b ? -1 : a > b ? 1 : 0);
const wanted = (name) => only.length === 0 || only.some((part) => name.toLowerCase().includes(part));

const output = join(root, project, 'StrykerOutput');
const reports = existsSync(output)
  ? readdirSync(output)
      .map((run) => join(output, run, 'reports', 'mutation-report.json'))
      .filter((path) => existsSync(path))
      .sort((a, b) => statSync(a).mtimeMs - statSync(b).mtimeMs)
  : [];
if (reports.length === 0) {
  console.error(`No Stryker report under src/${project}/StrykerOutput: run it first (stryker-run.sh).`);
  process.exit(1);
}

const newest = reports.at(-1);
const report = JSON.parse(readFileSync(newest, 'utf8'));
console.log(`== ${project} · ${newest.split(/[\\/]/).at(-3)}`);

const status = {};
const byFile = new Map();
const survivors = [];
for (const [path, entry] of Object.entries(report.files)) {
  const name = path.replaceAll('\\', '/').split(`/src/${project}/`).at(-1);
  const lines = (entry.source ?? '').split(/\r?\n/);
  for (const mutant of entry.mutants) {
    status[mutant.status] = (status[mutant.status] ?? 0) + 1;
    const counts = byFile.get(name) ?? {};
    counts[mutant.status] = (counts[mutant.status] ?? 0) + 1;
    byFile.set(name, counts);
    if (mutant.status === 'Survived') {
      const line = mutant.location.start.line;
      survivors.push({
        name,
        line,
        mutator: mutant.mutatorName ?? '',
        replacement: (mutant.replacement ?? '').slice(0, 60),
        source: (lines[line - 1] ?? '').trim().slice(0, 110),
      });
    }
  }
}

const n = (key, counts = status) => counts[key] ?? 0;
const killed = n('Killed') + n('Timeout');
const covered = killed + n('Survived');
const total = covered + n('NoCoverage');
const percent = (part, whole) => `${((part / whole) * 100).toFixed(2)} %`;
console.log(JSON.stringify(status));
console.log(
  covered
    ? `of covered ${percent(killed, covered)}   of total ${percent(killed, total)}   survivors ${n('Survived')}`
    : 'nothing covered',
);

console.log('-- by file (killed/survived/no coverage), files with survivors or uncovered');
for (const [name, counts] of [...byFile].sort((a, b) => n('Survived', b[1]) - n('Survived', a[1]))) {
  if (!wanted(name) || (!n('Survived', counts) && !n('NoCoverage', counts))) continue;
  const pad = (value) => String(value).padStart(4);
  console.log(
    `   ${pad(n('Killed', counts) + n('Timeout', counts))} ${pad(n('Survived', counts))} ${pad(n('NoCoverage', counts))}  ${name}`,
  );
}

if (detail) {
  console.log('-- survivors');
  survivors
    .filter((survivor) => wanted(survivor.name))
    // Ordinal, not by locale: the same order on every machine.
    .sort(
      (a, b) =>
        ordinal(a.name, b.name) ||
        a.line - b.line ||
        ordinal(a.mutator, b.mutator) ||
        ordinal(a.replacement, b.replacement),
    )
    .forEach((s) => console.log(`   ${s.name}:${s.line} [${s.mutator}] ${s.source}  =>  ${s.replacement}`));
}
