import nx from '@nx/eslint-plugin';

export default [
  ...nx.configs['flat/base'],
  ...nx.configs['flat/typescript'],
  ...nx.configs['flat/javascript'],
  {
    ignores: [
      '**/dist',
      '**/vite.config.*.timestamp*',
      '**/vitest.config.*.timestamp*',
    ],
  },
  {
    files: ['**/*.ts', '**/*.tsx', '**/*.js', '**/*.jsx'],
    rules: {
      '@nx/enforce-module-boundaries': [
        'error',
        {
          enforceBuildableLibDependency: true,
          allow: ['^.*/eslint(\\.base)?\\.config\\.[cm]?[jt]s$'],
          // The TypeScript twin of the backend's NetArchTest rules: the
          // architecture is executed, not commented. See docs/adr/0010.
          depConstraints: [
            // The two apps do NOT know each other. They can only reach down to shared.
            {
              sourceTag: 'scope:storefront',
              onlyDependOnLibsWithTags: ['scope:shared'],
            },
            {
              sourceTag: 'scope:backoffice',
              onlyDependOnLibsWithTags: ['scope:shared'],
            },
            // And shared does not know the apps exist: if a primitive needs to
            // know who uses it, it is not a primitive.
            {
              sourceTag: 'scope:shared',
              onlyDependOnLibsWithTags: ['scope:shared'],
            },
            // Nobody imports from an app, in either direction.
            {
              sourceTag: '*',
              notDependOnLibsWithTags: ['type:app'],
            },
          ],
        },
      ],
    },
  },
  {
    // The agent surface calls the SAME services the interface calls (P6-3).
    //
    // It is the frontend sibling of "MCP is a transport over the query
    // dispatcher": if a tool could reach the API by its own route, the two paths
    // would drift and only one of them would have tests — the agent would get a
    // cart that skipped a revalidation, or an error shape nobody had seen.
    //
    // Review cannot enforce that, so this does. A tool file may not import
    // HttpClient, and the day it needs one it is telling you the service it
    // should have called does not exist yet.
    //
    // The screens are held to the same line. A page that reached the API on
    // its own — the order page did, for a while — is a URL and an error shape
    // that exist nowhere a tool or another page can reuse, which is the drift
    // the rule exists to stop. `data-access/` is where HttpClient lives.
    files: ['apps/*/src/app/agent/**/*.ts', 'apps/*/src/app/features/**/*.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          paths: [
            {
              name: '@angular/common/http',
              importNames: ['HttpClient', 'HttpHeaders', 'HttpParams'],
              message:
                'Screens and agent tools call the same data-access services. Add the ' +
                'call to a service under data-access/ rather than talking to the API ' +
                'directly (P6-3).',
            },
          ],
        },
      ],
    },
  },
  {
    files: [
      '**/*.ts',
      '**/*.tsx',
      '**/*.cts',
      '**/*.mts',
      '**/*.js',
      '**/*.jsx',
      '**/*.cjs',
      '**/*.mjs',
    ],
    // Override or add rules here
    rules: {},
  },
];
