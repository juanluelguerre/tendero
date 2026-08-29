import { Component } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

/**
 * Cola de revision de las traducciones y el enriquecimiento con IA. Hoy solo
 * ensena su estado vacio: el slice que la alimenta es de la fase 2. El estado
 * vacio dice que hacer, no "no hay registros" (design/DESIGN.md, Copy voice).
 */
@Component({
  selector: 'backoffice-review-queue-page',
  imports: [TranslocoDirective],
  template: `
    <ng-container *transloco="let t">
      <h1 class="title">{{ t('reviewQueue.title') }}</h1>
      <div class="empty">
        <p class="empty__text">{{ t('reviewQueue.empty') }}</p>
        <code class="empty__hint numeric">{{ t('reviewQueue.emptyHint') }}</code>
      </div>
    </ng-container>
  `,
  styles: `
    .title { font-family: var(--font-display); font-size: var(--text-lg); margin: 0 0 var(--space-5); }
    .empty { border: 1px dashed var(--border); border-radius: var(--radius-lg); padding: var(--space-7); text-align: center; }
    .empty__text { color: var(--text-muted); margin: 0 0 var(--space-3); }
    .empty__hint { font-size: var(--text-2xs); color: var(--text-subtle); }
  `,
})
export class ReviewQueuePage {}
