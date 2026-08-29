import { Route } from '@angular/router';
import { Shell } from './layout/shell';

export const appRoutes: Route[] = [
  {
    path: '',
    component: Shell,
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./features/search/search-page').then((m) => m.SearchPage),
      },
    ],
  },
];
