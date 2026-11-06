// Mutation testing for one shared library: `npm run mutate -- util`.
// docs/testing.md, "The frontend": run at a phase close, never per pull request.
//
// It runs from frontend/, not from the library's folder: the libraries import
// each other through tsconfig.base.json's paths, which a sandbox holding one
// library could not resolve. Only that library's code is mutated, and only its
// own Vitest project runs. The applications wait for angular-cli#32182, since
// @angular/build:unit-test gives Stryker no hook into its Vitest.
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { Stryker } from '@stryker-mutator/core';

const libraries = readdirSync('libs/shared').filter((name) => existsSync(`libs/shared/${name}/vite.config.mts`));
const library = process.argv[2];

if (!libraries.includes(library)) {
  console.error(`Usage: npm run mutate -- <library>, one of: ${libraries.join(', ')}`);
  process.exit(2);
}

const root = `libs/shared/${library}`;
const report = `reports/mutation/${library}`;

const result = await new Stryker({
  testRunner: 'vitest',
  vitest: { configFile: `${root}/vite.config.mts` },
  // Production code only: not the specs, the contract suites and helpers they
  // share, or a file a generator writes.
  mutate: [
    `${root}/src/**/*.ts`,
    '!**/*.spec.ts',
    '!**/test-setup.ts',
    '!**/testing.ts',
    '!**/*-contract.ts',
    '!**/generated/**',
  ],
  coverageAnalysis: 'perTest',
  reporters: ['html', 'json', 'clear-text', 'progress'],
  htmlReporter: { fileName: `${report}/mutation.html` },
  jsonReporter: { fileName: `${report}/mutation.json` },
  tempDirName: '.stryker-tmp',
  plugins: ['@stryker-mutator/vitest-runner', './tools/angular-literals.ignorer.mjs'],
  ignorers: ['angular-literals'],
  // Each runner is a Vitest with workers of its own: one per core starved them,
  // and composer's run timed out 73 mutants no test could hang on.
  concurrency: 4,
  timeoutMS: 30_000,
  // The sandbox needs the libraries and the workspace's configuration, not
  // the applications or anything built.
  ignorePatterns: ['apps', 'dist', 'coverage', 'reports', '.nx', '.angular'],
}).runMutationTest();

console.log(`${result.length} mutants; report in ${report}/mutation.html`);

// The ratchet (docs/testing.md, Level 2): `of covered` may not fall below the
// library's baseline. A run that beats it says so; raising it is a decision
// written down with the run, so it is never done here.
const count = (...statuses) => result.filter((mutant) => statuses.includes(mutant.status)).length;
const detected = count('Killed', 'Timeout');
const covered = detected + count('Survived');
const score = covered === 0 ? 100 : Math.round((detected / covered) * 10_000) / 100;
const baseline = JSON.parse(readFileSync('tools/mutation-baselines.json', 'utf8'))[library];

if (baseline === undefined) {
  console.error(`${library}: ${score} % of covered, and no baseline in tools/mutation-baselines.json — add one.`);
  process.exit(1);
}
if (score < baseline) {
  console.error(`${library}: ${score} % of covered, under its baseline of ${baseline} %.`);
  process.exit(1);
}
console.log(
  score > baseline
    ? `${library}: ${score} % of covered, over its baseline of ${baseline} %: raise it in tools/mutation-baselines.json.`
    : `${library}: ${score} % of covered, at its baseline.`,
);
