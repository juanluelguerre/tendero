import { Route } from '@angular/router';
import { Shell } from './layout/shell';

export const appRoutes: Route[] = [
  {
    path: '',
    component: Shell,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'review' },
      {
        path: 'review',
        loadComponent: () =>
          import('./features/review-queue/review-queue-page').then((m) => m.ReviewQueuePage),
      },
    ],
  },
];
