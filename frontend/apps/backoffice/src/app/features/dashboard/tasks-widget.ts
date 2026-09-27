import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { CatalogService } from '../../data-access/catalog.service';
import { InventoryService } from '../../data-access/inventory.service';
import { KnowledgeService } from '../../data-access/knowledge.service';
import { lowStockOf } from './figures';

const Shown = 3;

/**
 * What is waiting for the shopkeeper (P11-7): drafts to publish, claims to review —
 * weakest support first, because those are the ones a person is for (phase 10) —
 * and stock running low. Every figure here already had an endpoint, so this widget
 * reuses three rather than inventing a fourth; each links to the screen that acts
 * on it.
 */
@Component({
  selector: 'backoffice-tasks-widget',
  imports: [TranslocoDirective, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './tasks-widget.html',
  styleUrl: './tasks-widget.css',
})
export class TasksWidget {
  private readonly catalog = inject(CatalogService);
  private readonly knowledge = inject(KnowledgeService);
  private readonly inventory = inject(InventoryService);
  private readonly culture = inject(CultureStore);

  protected readonly drafts = rxResource({
    params: () => ({ culture: this.culture.active() }),
    stream: ({ params }) => this.catalog.list('draft', params.culture, 1, 1),
  });

  protected readonly claims = rxResource({
    stream: () => this.knowledge.claims(),
  });

  protected readonly stock = rxResource({
    params: () => ({ culture: this.culture.active() }),
    stream: ({ params }) => this.inventory.stock(params.culture),
  });

  protected readonly weakestClaims = computed(() =>
    [...(this.claims.value() ?? [])].sort((a, b) => a.support - b.support).slice(0, Shown),
  );

  /**
   * Per SKU, across warehouses. Found on the stack: counted per warehouse row, a
   * SKU stocked in only one warehouse read as empty in the other, and 144 "low"
   * rows said nothing a shopkeeper could act on.
   */
  protected readonly lowStock = computed(() => lowStockOf(this.stock.value()?.rows ?? []));

  protected statement(claim: { statementSpanish: string; statementEnglish: string }): string {
    return this.culture.active() === 'en' ? claim.statementEnglish : claim.statementSpanish;
  }

  protected readonly shown = Shown;
}
