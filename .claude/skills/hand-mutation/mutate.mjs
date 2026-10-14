// One hand mutation, start to finish (CLAUDE.md, Testing rules: a test written
// for code that exists earns its place by failing against a hand-made mutation).
//
//   node .claude/skills/hand-mutation/mutate.mjs --file <path> --find <text> --replace <text>
//     --build "<command>" --test "<command>" [--why "<what the mutant breaks>"]
//
// It does what went wrong by hand, in the order that makes each step mean
// something:
//   1. The text to change must appear exactly once: a mutation that hits two
//      places, or none, proves nothing. It is literal text, never a pattern:
//      `sed` once swallowed a backslash and changed the mutant silently.
//   2. The file is kept byte for byte before it is touched.
//   3. The build must succeed. A mutant that does not compile leaves the old
//      binaries in place, and the test then runs against unmutated code
//      (phase 13b, twice).
//   4. The test must have run, and FAILED. A command that never reached a test
//      (a wrong path, a shell that cannot run it) is not a red test.
//   5. The file is restored, whatever happened, and compared byte for byte. A
//      `git diff` cannot show this for a file git does not track yet.
// It prints one line for the commit message.
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { delimiter, join } from 'node:path';
import { parseArgs } from 'node:util';

const { values: args } = parseArgs({
  options: {
    file: { type: 'string' },
    find: { type: 'string' },
    replace: { type: 'string' },
    build: { type: 'string' },
    test: { type: 'string' },
    why: { type: 'string', default: '' },
  },
});
for (const name of ['file', 'find', 'replace', 'build', 'test'])
  if (args[name] === undefined) {
    console.error(`--${name} is required`);
    process.exit(2);
  }

// What a run that reached its tests prints: xUnit's summary, Vitest's, Playwright's.
const Ran = ['Total:', 'Tests ', 'Test Files', ' passed', ' failed'];

/**
 * Git's bash, from PATH. Not a bare "bash": on Windows that can resolve to
 * System32's, which is WSL's, and WSL has no dotnet (seen writing the Python
 * version of this script).
 */
function bash() {
  const names = process.platform === 'win32' ? ['bash.exe'] : ['bash'];
  for (const folder of (process.env.PATH ?? '').split(delimiter)) {
    if (/system32|windowsapps/i.test(folder)) continue;
    for (const name of names) if (existsSync(join(folder, name))) return join(folder, name);
  }
  return 'bash';
}

/** A command as written, through bash: cmd.exe does not know `./tests/...`, and its failure looked like a red test. */
function run(command) {
  const done = spawnSync(bash(), ['-c', command], {
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
  });
  return {
    code: done.status ?? 1,
    out: `${done.stdout ?? ''}${done.stderr ?? ''}`,
  };
}

const lastLines = (text, count) => text.split(/\r?\n/).slice(-count).join('\n');

const original = readFileSync(args.file);
const text = original.toString('utf8');
const count = text.split(args.find).length - 1;
if (count !== 1) {
  console.log(`NOT RUN: the text appears ${count} times in ${args.file}; it must appear exactly once.`);
  process.exit(2);
}

let verdict = 'NOT RUN';
let detail = '';
try {
  // A function as the replacement, so `$&` and friends in it are taken literally.
  writeFileSync(
    args.file,
    text.replace(args.find, () => args.replace),
    'utf8',
  );

  const build = run(args.build);
  if (build.code !== 0 || build.out.includes('error CS') || build.out.includes(' error TS')) {
    verdict = 'DID NOT COMPILE';
    const errors = build.out.split(/\r?\n/).filter((line) => /error/i.test(line));
    detail = (errors.length ? errors.join('\n') : lastLines(build.out, 15)).slice(0, 2000);
  } else {
    const tested = run(args.test);
    detail = lastLines(tested.out, 15);
    if (!Ran.some((sign) => tested.out.includes(sign))) verdict = 'TEST DID NOT RUN';
    else verdict = tested.code !== 0 ? 'RED' : 'GREEN';
  }
} finally {
  writeFileSync(args.file, original);
  const restored = readFileSync(args.file).equals(original);
  console.log(`mutation: ${verdict}`);
  if (detail) console.log(detail);
  console.log(`restored: ${restored ? 'yes, byte for byte' : 'NO - restore it by hand'}`);
  if (verdict === 'RED' && restored) {
    const why = args.why ? ` (${args.why})` : '';
    console.log(
      `for the commit: ${args.file.replaceAll('\\', '/')}: \`${args.find.trim()}\` -> \`${args.replace.trim()}\`${why}: red, restored.`,
    );
  }
  if (verdict === 'GREEN') console.log('The test passed against the mutant: it does not hold what it was written for.');
  process.exitCode = verdict === 'RED' && restored ? 0 : 1;
}
