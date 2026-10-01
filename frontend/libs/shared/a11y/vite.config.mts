/// <reference types='vitest' />
import { defineConfig } from 'vite';

export default defineConfig(() => ({
  root: import.meta.dirname,
  cacheDir: '../../../node_modules/.vite/libs/shared/a11y',
  // Vite 8 resolves the workspace's tsconfig paths itself.
  resolve: { tsconfigPaths: true },
  test: {
    name: 'shared-a11y',
    watch: false,
    globals: true,
    environment: 'node',
    include: ['src/**/*.spec.ts'],
    reporters: ['default'],
  },
}));
