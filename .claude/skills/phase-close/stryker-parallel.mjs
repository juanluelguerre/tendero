// Stryker over one project in N processes at once (docs/testing.md, Level 2, since 2026-10-17).
//
//   node .claude/skills/phase-close/stryker-parallel.mjs <project> [--shards 8] [--minutes 90]
//        [--mutate "Speech/WhisperCppSpeechToText.cs,Features/X/X.cs"] [--keep]
//
// <project> is the folder that holds its stryker-config.json: src/AI, tools/SearchEval.
//
// Why processes and not threads: Stryker's MTP runner is the one whose per-test
// coverage works with xUnit v3 — v3 runs tests inside the test executable, where
// vstest's collector cannot see them — and that runner under-reports kills above
// concurrency 1 (#3832). So each process keeps concurrency 1, mutates a share of
// the project's files, and runs in its own git worktree, because Stryker builds in
// place and eight builds in one tree overwrite each other. A worktree has no
// models/ folder (git ignores it), so the tests that need a model skip by
// themselves. The shards' JSON reports are merged into one under the project's
// StrykerOutput, where stryker-read.mjs finds it, with an HTML report beside it.
import { execFileSync, spawn } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { join, relative, resolve, sep } from 'node:path';

const root = resolve(import.meta.dirname, '..', '..', '..');
const args = process.argv.slice(2);
const project = args[0];
const option = (name, fallback) => {
  const at = args.indexOf(`--${name}`);
  return at >= 0 ? args[at + 1] : fallback;
};
const shards = Number(option('shards', 8));
const minutes = Number(option('minutes', 90));
const scope = option('mutate', '')
  .split(',')
  .map((file) => file.trim())
  .filter(Boolean);
const keep = args.includes('--keep');

if (!project || !existsSync(join(root, project, 'stryker-config.json'))) {
  console.error('Give the folder of a project that has a stryker-config.json: src/AI, tools/SearchEval.');
  process.exit(2);
}

const git = (...a) => execFileSync('git', a, { cwd: root, encoding: 'utf8' }).trim();
const head = git('rev-parse', 'HEAD');
if (git('status', '--porcelain', '--', project)) {
  console.error(`${project} has uncommitted changes, and a worktree is made from HEAD: commit them first.`);
  process.exit(2);
}

// The files to mutate, weighed by their lines, dealt to the lightest shard first.
const sources = (dir) =>
  readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (['bin', 'obj', 'StrykerOutput'].includes(name)) return [];
    if (statSync(path).isDirectory()) return sources(path);
    return name.endsWith('.cs') ? [path] : [];
  });
const projectDir = join(root, project);
const files = (scope.length ? scope.map((file) => join(projectDir, file)) : sources(projectDir))
  .map((path) => ({ file: relative(projectDir, path).split(sep).join('/'), lines: readFileSync(path, 'utf8').split('\n').length }))
  .sort((a, b) => b.lines - a.lines);
const bins = Array.from({ length: Math.min(shards, files.length) }, () => ({ lines: 0, files: [] }));
for (const file of files) {
  const lightest = bins.reduce((a, b) => (b.lines < a.lines ? b : a));
  lightest.files.push(file.file);
  lightest.lines += file.lines;
}

// A configuration that names the integration suite runs with its unit suites only
// (Testcontainers would start a database per mutant: phase 13b's eight hours).
const config = readFileSync(join(projectDir, 'stryker-config.json'), 'utf8');
const units = config.includes('Integration.Tests')
  ? [...config.matchAll(/"([^"]*\.Tests\.csproj)"/g)]
      .map((m) => m[1])
      .filter((p) => !p.includes('Integration.Tests'))
      .flatMap((p) => ['--test-project', p])
  : [];

const name = project.split('/').pop();
const stamp = new Date().toISOString().replace(/[-:]/g, '').slice(0, 15);
const work = join(root, 'artifacts', 'stryker-shards', `${name}-${stamp}`);
mkdirSync(work, { recursive: true });
console.log(`${name}: ${files.length} files in ${bins.length} shards, ${minutes} minutes each at most, from ${head.slice(0, 8)}`);

const temp = (tree) => {
  const dir = join(tree, '.tmp');
  mkdirSync(dir, { recursive: true });
  return dir;
};
const started = Date.now();
const runs = bins.map((bin, i) => {
  const tree = join(work, `shard-${i}`);
  git('worktree', 'add', '--detach', '--quiet', tree, head);
  // coverage-analysis all: which mutants a test reaches at all (so "no coverage"
  // still counts), then the whole suite against each one. Per test, the MTP
  // runner mixed xUnit v3's parallel collections up and reported mutants as
  // survivors that a hand mutation showed killed (TdtGreedy, phase 14's close).
  // It is a configuration key, not an option, so the worktree's copy says it.
  const configPath = join(tree, project, 'stryker-config.json');
  const tuned = JSON.parse(readFileSync(configPath, 'utf8'));
  tuned['stryker-config']['coverage-analysis'] = 'all';
  writeFileSync(configPath, JSON.stringify(tuned, null, 2));
  const cwd = join(tree, project);
  const log = join(work, `shard-${i}.log`);
  const mutate = bin.files.flatMap((file) => ['-m', file]);
  return new Promise((done) => {
    const child = spawn(
      'dotnet',
      ['tool', 'run', 'dotnet-stryker', '--config-file', 'stryker-config.json', '--skip-version-check', '--concurrency', '1',
        '-r', 'json', '-r', 'html', ...units, ...mutate],
      // Each share its own temporary folder: the MTP runner leaves files there, and
      // shares that shared %TEMP% read each other's (known-killed mutants came back
      // as survivors, and an endless loop as one, when shares ran side by side).
      { cwd, shell: false, env: { ...process.env, TEMP: temp(tree), TMP: temp(tree) } },
    );
    const out = [];
    child.stdout.on('data', (d) => out.push(d));
    child.stderr.on('data', (d) => out.push(d));
    // The ceiling stops the whole tree: on Windows, killing dotnet leaves Stryker and its
    // test hosts running (phase 14's Api run went on for eleven hours past its 90 minutes).
    const timer = setTimeout(() => {
      if (process.platform === 'win32') spawn('taskkill', ['/pid', String(child.pid), '/T', '/F']);
      else child.kill('SIGKILL');
    }, minutes * 60_000);
    child.on('close', (code) => {
      clearTimeout(timer);
      writeFileSync(log, Buffer.concat(out));
      const seconds = Math.round((Date.now() - started) / 1000);
      console.log(`  shard ${i}: ${bin.files.length} files, ${bin.lines} lines, exit ${code} after ${seconds} s`);
      done({ i, tree, cwd, code });
    });
  });
});

// dotnet tool restore once per worktree would race on the same tool cache: it was
// restored in the main tree, and every worktree shares the user's cache.
const results = await Promise.all(runs);

// Merge: each file from the shard that mutated it.
const merged = { files: {} };
for (const { i, cwd } of results) {
  const outputs = existsSync(join(cwd, 'StrykerOutput')) ? readdirSync(join(cwd, 'StrykerOutput')) : [];
  const report = outputs.map((run) => join(cwd, 'StrykerOutput', run, 'reports', 'mutation-report.json')).find(existsSync);
  if (!report) {
    console.log(`  shard ${i}: no report (see ${join(work, `shard-${i}.log`)})`);
    continue;
  }
  const json = JSON.parse(readFileSync(report, 'utf8'));
  for (const key of Object.keys(json)) if (key !== 'files' && !(key in merged)) merged[key] = json[key];
  // Paths come back as the worktree's, in either slash: each is filed under the main tree's.
  const shardRoot = cwd.split(sep).join('/').toLowerCase();
  const mainRoot = projectDir.split(sep).join('/');
  for (const file of bins[i].files) {
    const entry = Object.entries(json.files).find(([path]) => path.replaceAll('\\', '/').endsWith(`/${project}/${file}`));
    if (!entry) continue;
    const path = entry[0].replaceAll('\\', '/');
    const inMain = path.toLowerCase().startsWith(shardRoot) ? mainRoot + path.slice(shardRoot.length) : path;
    merged.files[inMain] = entry[1];
  }
  const html = report.replace('mutation-report.json', 'mutation-report.html');
  if (existsSync(html) && !merged.html) merged.html = readFileSync(html, 'utf8');
}

const count = (status) =>
  Object.values(merged.files).reduce((n, file) => n + file.mutants.filter((m) => m.status === status).length, 0);
const killed = count('Killed') + count('Timeout');
const survived = count('Survived');
const noCoverage = count('NoCoverage');
const percent = (a, b) => (b === 0 ? 'n/a' : `${((100 * a) / b).toFixed(2)} %`);

const html = merged.html;
delete merged.html;
const target = join(projectDir, 'StrykerOutput', `${stamp}-parallel`, 'reports');
mkdirSync(target, { recursive: true });
writeFileSync(join(target, 'mutation-report.json'), JSON.stringify(merged));
if (html) {
  // Stryker's HTML report carries its JSON inline; the merged one takes its place.
  const replaced = html.replace(/app\.report = \{[\s\S]*?\};\s*<\/script>/, () => `app.report = ${JSON.stringify(merged)};</script>`);
  writeFileSync(join(target, 'mutation-report.html'), replaced);
}

const wall = Math.round((Date.now() - started) / 60_000);
console.log(
  `${name}: ${Object.keys(merged.files).length} files merged in ${wall} min — killed ${killed}, survived ${survived}, ` +
    `no coverage ${noCoverage}; of covered ${percent(killed, killed + survived)}, of total ${percent(killed, killed + survived + noCoverage)}`,
);
console.log(`  report: ${target}`);

// The worktrees go; the shards' logs stay beside where they were, for a shard that failed.
if (!keep) for (const { tree } of results) rmSync(tree, { recursive: true, force: true });
git('worktree', 'prune');
console.log(`  logs: ${work}`);
