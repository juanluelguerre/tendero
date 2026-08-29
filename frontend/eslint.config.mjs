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
          // El gemelo en TypeScript de los tests de NetArchTest del backend: la
          // arquitectura se ejecuta, no se comenta. Ver docs/adr/0010.
          depConstraints: [
            // Las dos apps NO se conocen. Solo pueden bajar a lo compartido.
            {
              sourceTag: 'scope:storefront',
              onlyDependOnLibsWithTags: ['scope:shared'],
            },
            {
              sourceTag: 'scope:backoffice',
              onlyDependOnLibsWithTags: ['scope:shared'],
            },
            // Y lo compartido no sabe que las apps existen: si una primitiva
            // necesita saber quien la usa, no es una primitiva.
            {
              sourceTag: 'scope:shared',
              onlyDependOnLibsWithTags: ['scope:shared'],
            },
            // Nadie importa de una app, en ninguna direccion.
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
