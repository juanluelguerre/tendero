import playwright from 'eslint-plugin-playwright';
import baseConfig from '../../eslint.config.mjs';

export default [
  ...baseConfig,
  {
    ...playwright.configs['flat/recommended'],
    files: ['src/**/*.spec.ts'],
    rules: {
      ...playwright.configs['flat/recommended'].rules,

      // The one the plugin gets wrong for this repository. A conditional skip is
      // exactly right when the fixture is "whatever the running stack has": a
      // spec that demanded an order would fail on a fresh clone for a reason
      // that has nothing to do with the code.
      'playwright/no-skipped-test': ['error', { allowConditional: true }],
    },
  },
];
