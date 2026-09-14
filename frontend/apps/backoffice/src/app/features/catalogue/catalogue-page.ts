import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import type { ProductSummary } from '@tendero/shared-api';
import { formatPrice } from '@tendero/shared-util';
import type { Subscription } from 'rxjs';
import { CatalogService } from '../../data-access/catalog.service';

type CatalogueState =
  | { status: 'loading' }
  | { status: 'ready'; items: ProductSummary[]; total: number }
  | { status: 'failed' };

/** Twenty rows a page, the review queue's number: the same table, dense. */
const PAGE_SIZE = 20;

/**
 * What is published, and the way to its variants.
 *
 * The review queue lists drafts, and it was the only screen that listed
 * products at all — so once everything was published the backoffice showed no
 * product anywhere, and defining variants (P1-8) could not be reached through
 * the interface. The storefront's variant picker had never met a product with
 * axes, and the Playwright spec that would have exercised it skipped itself
 * every run with a reason nobody read (UI review, 2026-09-11).
 *
 * It reads the same endpoint the queue reads, asking for Active. It offers no
 * clay action: nothing here is the primary thing to do, and the variants link
 * is a quiet one per row, as it is in the queue.
 */
@Component({
  selector: 'backoffice-catalogue-page',
  imports: [RouterLink, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './catalogue-page.html',
  styleUrl: './catalogue-page.css',
})
export class CataloguePage {
  private readonly catalog = inject(CatalogService);
  private readonly culture = inject(CultureStore);

  protected readonly state = signal<CatalogueState>({ status: 'loading' });
  protected readonly page = signal(1);

  protected readonly pages = computed(() => {
    const current = this.state();
    return current.status === 'ready' ? Math.max(1, Math.ceil(current.total / PAGE_SIZE)) : 1;
  });

  private inFlight?: Subscription;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.inFlight?.unsubscribe());

    // Names come resolved into the requested culture, so a language switch
    // reloads — the review queue's reason, one screen over.
    effect(() => {
      this.culture.active();
      this.page();
      untracked(() => this.load());
    });
  }

  protected go(page: number): void {
    this.page.set(Math.min(Math.max(1, page), this.pages()));
  }

  protected price(item: ProductSummary): string {
    return formatPrice(item.priceAmount, item.priceCurrency, this.culture.active());
  }

  private load(): void {
    this.state.set({ status: 'loading' });

    this.inFlight?.unsubscribe();
    this.inFlight = this.catalog.list('active', this.culture.active(), this.page(), PAGE_SIZE).subscribe({
      next: (result) => this.state.set({ status: 'ready', items: result.items, total: result.total }),
      error: (failure: unknown) => {
        if (isSessionExpired(failure)) return;
        this.state.set({ status: 'failed' });
      },
    });
  }
}
