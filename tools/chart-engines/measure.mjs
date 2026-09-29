// R-9's chart comparison (ADR 0045): what each engine adds to the backoffice,
// read from the production build's esbuild metafile, and how much code of ours
// each engine is. Run after:
//   cd frontend && npx nx build backoffice --configuration=production --stats-json
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { gzipSync } from 'node:zlib';
import { basename, join } from 'node:path';

const root = new URL('../../', import.meta.url).pathname.replace(/^\/([A-Z]:)/, '$1');
const dist = join(root, 'frontend/dist/apps/backoffice');
const stats = JSON.parse(readFileSync(join(dist, 'stats.json'), 'utf8'));

// Which output chunk holds which inputs: the engine's bytes are what its inputs contribute.
const groups = {
  'ECharts (library)': (path) => /node_modules[\\/](echarts|zrender)[\\/]/.test(path),
  'ECharts (our binding)': (path) => /app[\\/]charts[\\/]echarts/.test(path) || /chart-colours/.test(path),
  'SVG (ours)': (path) => /app[\\/]charts[\\/](svg-bar-chart|scale)/.test(path),
};

const bytes = Object.fromEntries(Object.keys(groups).map((name) => [name, 0]));
const echartsChunks = new Set();
for (const [path, output] of Object.entries(stats.outputs ?? {})) {
  for (const [input, { bytesInOutput }] of Object.entries(output.inputs ?? {})) {
    for (const [name, matches] of Object.entries(groups)) if (matches(input)) bytes[name] += bytesInOutput;
    if (groups['ECharts (library)'](input) && bytesInOutput > 0) echartsChunks.add(basename(path));
  }
}

// Gzip is measured on the whole chunks that hold ECharts — found in the metafile,
// not by searching the code, which minification renames — since gzip does not
// split by input.
const browser = join(dist, 'browser');
const gzip = [...echartsChunks].reduce((total, file) => total + gzipSync(readFileSync(join(browser, file))).length, 0);

const lines = (pattern) =>
  readdirSync(join(root, 'frontend/apps/backoffice/src/app/charts'))
    .filter((file) => pattern.test(file) && !file.endsWith('.spec.ts'))
    .reduce((total, file) => total + readFileSync(join(root, 'frontend/apps/backoffice/src/app/charts', file), 'utf8').split('\n').length, 0);

const kb = (value) => `${(value / 1024).toFixed(1)} KB`;
console.log('| | minified | lines of ours |');
console.log('|---|---:|---:|');
console.log(`| SVG (ours) | ${kb(bytes['SVG (ours)'])} | ${lines(/^(svg-bar-chart|scale)\./)} |`);
console.log(`| ECharts library | ${kb(bytes['ECharts (library)'])} (its chunks ${kb(gzip)} gzip: ${[...echartsChunks].join(', ')}) | — |`);
console.log(`| ECharts binding (ours) | ${kb(bytes['ECharts (our binding)'])} | ${lines(/^(echarts|chart-colours)/)} |`);
console.log(`\nThe contract (bar-chart, chart-contract) is shared by both and counted in neither.`);
statSync(browser); // fails loudly if the build is missing
