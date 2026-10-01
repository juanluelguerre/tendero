/// <reference types='vitest' />
import { defineConfig } from 'vite';

// Plain functions, no Angular: no compiler plugin and no DOM.
export default defineConfig(() => ({
  root: import.meta.dirname,
  cacheDir: '../../../node_modules/.vite/libs/shared/util',
  // Vite 8 resolves the workspace's tsconfig paths itself.
  resolve: { tsconfigPaths: true },
  test: {
    name: 'shared-util',
    watch: false,
    globals: true,
    environment: 'node',
    include: ['src/**/*.spec.ts'],
    reporters: ['default'],
  },
}));
