import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective, translateObjectSignal } from '@jsverse/transloco';
import type { ClaimForReview, ClaimSupportFilter } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { forkJoin } from 'rxjs';
import { CatalogService } from '../../data-access/catalog.service';
import { type ClaimDecision, KnowledgeService } from '../../data-access/knowledge.service';
import { type ActionVia, BackofficeUsage } from '../../usage';
import { FilterChips, type FilterOption } from '../../work/filter-chips';
import { landing, stepOf } from '../../work/list-keys';
import { Pager } from '../../work/pager';
import { WorkPane } from '../../work/work-pane';

/** Under this, a claim's quote may not say what the claim does: read these first. */

/**
 * Claims a model proposed and a person decides (phase 10, ADR 0018), as R-3
 * drew the lists of work (R-4.3): the queue opens on the weakly supported —
 * the ones a person is for — keeps both decisions on the row, and opens a
 * claim beside the list with the statement in both languages, every quote that
 * backs it and how well, and who proposed it.
 *
 * **No bulk approve, on purpose** (R-2, P4): each claim is a judgement about one
 * product, and a bulk approve is how a review queue becomes a rubber stamp.
 * Nothing reaches the catalogue until a person approves it (invariant 12).
 */
/** Twenty rows a page, as every list of work (R-7.2). */
const PageSize = 20;

@Component({
  selector: 'backoffice-claims-review-page',
  imports: [TranslocoDirective, NgTemplateOutlet, WorkPane, FilterChips, Pager],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './claims-review-page.html',
  styleUrl: './claims-review-page.css',
  host: { '(document:keydown)': 'key($event)' },
})
export class ClaimsReviewPage {
  private readonly knowledge = inject(KnowledgeService);
  private readonly usage = inject(BackofficeUsage);
  private readonly catalog = inject(CatalogService);
  private readonly culture = inject(CultureStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private readonly filterLabels = translateObjectSignal('claims.filter');
  private readonly params = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });

  /** The queue opens on the weak ones (R-4.3); which are weak is the server's rule now (R-7.2). */
  // Read in any case: a link saved before the filter took the enum's name said `all`.
  protected readonly filterKey = computed<ClaimSupportFilter>(() => (this.params().get('support')?.toLowerCase() === 'all' ? 'All' : 'Weak'));
  protected readonly page = computed(() => Math.max(1, Number(this.params().get('page') ?? 1) || 1));
  protected readonly pageSize = PageSize;
  protected readonly selectedId = computed(() => this.params().get('claim'));

  /**
   * The queue and the names of its products. A hundred products is the whole
   * laboratory catalogue (the scale rule); a shop past that would ask the
   * catalogue for the queue's products only.
   */
  private readonly queue = rxResource({
    params: () => ({ culture: this.culture.active(), support: this.filterKey(), page: this.page() }),
    stream: ({ params }) =>
      forkJoin({
        claims: this.knowledge.claims(params.support, params.page, PageSize),
        products: this.catalog.list(null, params.culture, 1, 100),
      }),
  });

  /** The page of the queue the filter shows. */
  protected readonly claims = computed(() => this.queue.value()?.claims.items ?? []);
  protected readonly total = computed(() => this.queue.value()?.claims.total ?? 0);

  /** Every filter's number over the whole queue, kept while the next page loads. */
  private readonly counts = linkedSignal<ReadonlyMap<string, number> | undefined, ReadonlyMap<string, number> | null>({
    source: () => {
      const page = this.queue.value()?.claims;
      return page && new Map(page.counts.map((entry) => [entry.support, entry.count]));
    },
    computation: (counts, previous) => counts ?? previous?.value ?? null,
  });
  private readonly names = computed(
    () => new Map((this.queue.value()?.products.items ?? []).map((product) => [product.productId, product.name])),
  );
  protected readonly loading = computed(() => this.queue.isLoading() && !this.queue.hasValue());
  protected readonly failed = computed(() => this.queue.status() === 'error');

  /** The server filtered it; the page is what is shown. */
  protected readonly shown = this.claims;

  protected readonly filters = computed<FilterOption[]>(() => {
    const labels = this.filterLabels() as Record<string, string>;
    const counts = this.counts();
    return (['Weak', 'All'] as const).map((key) => ({ key, label: labels[key] ?? key, count: counts?.get(key) ?? null }));
  });

  protected readonly selected = computed(() => this.claims().find((claim) => claim.claimId === this.selectedId()) ?? null);
  protected readonly busy = signal<string | null>(null);

  protected statement(claim: ClaimForReview, culture = this.culture.active()): string {
    return culture === 'en' ? claim.statementEnglish : claim.statementSpanish;
  }

  protected productName(claim: ClaimForReview): string {
    return this.names().get(claim.productId) ?? claim.productId.slice(0, 8);
  }

  protected percent(support: number): string {
    return `${Math.round(support * 100)} %`;
  }

  protected filterBy(key: string): void {
    void this.router.navigate([], { queryParams: { support: key === 'Weak' ? null : key, page: null }, queryParamsHandling: 'merge' });
  }

  protected go(page: number): void {
    void this.router.navigate([], { queryParams: { page: page <= 1 ? null : page }, queryParamsHandling: 'merge' });
  }

  protected open(claim: ClaimForReview): void {
    void this.router.navigate([], { queryParams: { claim: claim.claimId }, queryParamsHandling: 'merge' });
  }

  /** j and k down the queue, the pane following (R-3, P11). */
  protected key(event: KeyboardEvent): void {
    const step = stepOf(event, !!document.querySelector('dialog[open]'));
    if (!step) return;
    const claims = this.shown();
    const index = landing(claims.findIndex((claim) => claim.claimId === this.selectedId()), claims.length, step);
    if (index === null) return;
    event.preventDefault();
    this.usage.affordance('keys', step === 1 ? 'next' : 'previous');
    this.open(claims[index]);
  }

  protected close(): void {
    void this.router.navigate([], { queryParams: { claim: null }, queryParamsHandling: 'merge' });
  }

  protected decide(claim: ClaimForReview, decision: ClaimDecision, via: ActionVia): void {
    this.busy.set(claim.claimId);

    this.knowledge
      .decide(claim.claimId, decision)
      .pipe(this.usage.reported('claims', decision === 'approve' ? 'claim_approve' : 'claim_reject', via))
      .subscribe({
        next: () => this.settle(claim),
        error: (failure: unknown) => {
          if (isSessionExpired(failure)) return;
          // A 409 means another reviewer decided it, or its text changed: the
          // queue is the server's, so asking again shows what is true now.
          this.settle(claim);
        },
      });
  }

  /** The decided claim leaves, and the pane moves to the next one down the queue. */
  private settle(claim: ClaimForReview): void {
    this.busy.set(null);
    if (this.selectedId() === claim.claimId) {
      const list = this.shown();
      const next = list[list.findIndex((candidate) => candidate.claimId === claim.claimId) + 1];
      if (next) this.open(next);
      else this.close();
    }
    this.queue.reload();
  }
}
