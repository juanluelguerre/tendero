/// <reference types='vitest' />
import { defineConfig } from 'vite';
import { nxViteTsPaths } from '@nx/vite/plugins/nx-tsconfig-paths.plugin';

// Plain functions, no Angular: no compiler plugin and no DOM.
export default defineConfig(() => ({
  root: __dirname,
  cacheDir: '../../../node_modules/.vite/libs/shared/util',
  plugins: [nxViteTsPaths()],
  test: {
    name: 'shared-util',
    watch: false,
    globals: true,
    environment: 'node',
    include: ['src/**/*.spec.ts'],
    reporters: ['default'],
  },
}));
