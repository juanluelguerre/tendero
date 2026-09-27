import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective, translateObjectSignal } from '@jsverse/transloco';
import type { ClaimForReview } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { forkJoin } from 'rxjs';
import { CatalogService } from '../../data-access/catalog.service';
import { type ClaimDecision, KnowledgeService } from '../../data-access/knowledge.service';
import { BackofficeUsage } from '../../usage';
import { FilterChips, type FilterOption } from '../../work/filter-chips';
import { WorkPane } from '../../work/work-pane';

/** Under this, a claim's quote may not say what the claim does: read these first. */
const WeakSupport = 0.8;

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
@Component({
  selector: 'backoffice-claims-review-page',
  imports: [TranslocoDirective, NgTemplateOutlet, WorkPane, FilterChips],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './claims-review-page.html',
  styleUrl: './claims-review-page.css',
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

  protected readonly filterKey = computed(() => this.params().get('support') ?? 'weak');
  protected readonly selectedId = computed(() => this.params().get('claim'));

  /**
   * The queue and the names of its products. A hundred products is the whole
   * laboratory catalogue (the scale rule); a shop past that would ask the
   * catalogue for the queue's products only.
   */
  private readonly queue = rxResource({
    params: () => ({ culture: this.culture.active() }),
    stream: ({ params }) =>
      forkJoin({ claims: this.knowledge.claims(), products: this.catalog.list(null, params.culture, 1, 100) }),
  });

  protected readonly claims = computed(() => this.queue.value()?.claims ?? []);
  private readonly names = computed(
    () => new Map((this.queue.value()?.products.items ?? []).map((product) => [product.productId, product.name])),
  );
  protected readonly loading = computed(() => this.queue.isLoading() && !this.queue.hasValue());
  protected readonly failed = computed(() => this.queue.status() === 'error');

  protected readonly shown = computed(() =>
    this.filterKey() === 'all' ? this.claims() : this.claims().filter((claim) => this.isWeak(claim)),
  );

  protected readonly filters = computed<FilterOption[]>(() => {
    const labels = this.filterLabels() as Record<string, string>;
    return [
      { key: 'weak', label: labels['weak'] ?? 'weak', count: this.claims().filter((claim) => this.isWeak(claim)).length },
      { key: 'all', label: labels['all'] ?? 'all', count: this.claims().length },
    ];
  });

  protected readonly selected = computed(() => this.claims().find((claim) => claim.claimId === this.selectedId()) ?? null);
  protected readonly busy = signal<string | null>(null);

  protected statement(claim: ClaimForReview, culture = this.culture.active()): string {
    return culture === 'en' ? claim.statementEnglish : claim.statementSpanish;
  }

  protected productName(claim: ClaimForReview): string {
    return this.names().get(claim.productId) ?? claim.productId.slice(0, 8);
  }

  protected isWeak(claim: ClaimForReview): boolean {
    return claim.support < WeakSupport;
  }

  protected percent(support: number): string {
    return `${Math.round(support * 100)} %`;
  }

  protected filterBy(key: string): void {
    void this.router.navigate([], { queryParams: { support: key === 'weak' ? null : key }, queryParamsHandling: 'merge' });
  }

  protected open(claim: ClaimForReview): void {
    void this.router.navigate([], { queryParams: { claim: claim.claimId }, queryParamsHandling: 'merge' });
  }

  protected close(): void {
    void this.router.navigate([], { queryParams: { claim: null }, queryParamsHandling: 'merge' });
  }

  protected decide(claim: ClaimForReview, decision: ClaimDecision): void {
    this.busy.set(claim.claimId);

    this.knowledge
      .decide(claim.claimId, decision)
      .pipe(this.usage.reported('claims', decision === 'approve' ? 'claim_approve' : 'claim_reject'))
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
