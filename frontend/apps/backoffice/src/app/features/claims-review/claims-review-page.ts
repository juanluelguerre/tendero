import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { isSessionExpired } from '@tendero/shared-auth';
import type { ClaimForReview } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { forkJoin, type Subscription } from 'rxjs';
import { CatalogService } from '../../data-access/catalog.service';
import { type ClaimDecision, KnowledgeService } from '../../data-access/knowledge.service';
import { BackofficeUsage } from '../../usage';

/** Below it, a claim's support is flagged: the quote may not say what the claim does (P10-0). */
const WeakSupport = 0.8;

/**
 * The claims review queue (P10-5, ADR 0018): what a model proposed from a
 * product's description, waiting for a person. **The AI writes claims, not
 * products** — nothing here changes the catalogue until someone approves.
 *
 * The server orders the queue, the weakest support first, and owns every rule;
 * the screen joins in the product names, which Knowledge — sharing no entity
 * with Catalog (ADR 0014) — cannot give. No optimistic state: a row leaves when
 * the server confirms, as in the product review queue.
 */
@Component({
  selector: 'backoffice-claims-review-page',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './claims-review-page.html',
  styleUrl: './claims-review-page.css',
})
export class ClaimsReviewPage {
  private readonly knowledge = inject(KnowledgeService);
  private readonly usage = inject(BackofficeUsage);
  private readonly catalog = inject(CatalogService);
  private readonly culture = inject(CultureStore);

  protected readonly claims = signal<ClaimForReview[]>([]);
  protected readonly names = signal<ReadonlyMap<string, string>>(new Map());
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busy = signal<string | null>(null);

  private inFlight?: Subscription;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.inFlight?.unsubscribe());

    // Statements and product names are both per culture: reload when it changes.
    effect(() => {
      this.culture.active();
      untracked(() => this.load());
    });
  }

  protected statement(claim: ClaimForReview): string {
    return this.culture.active() === 'en' ? claim.statementEnglish : claim.statementSpanish;
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

  protected decide(claim: ClaimForReview, decision: ClaimDecision): void {
    this.busy.set(claim.claimId);

    this.knowledge.decide(claim.claimId, decision)
      .pipe(this.usage.reported('claims', decision === 'approve' ? 'claim_approve' : 'claim_reject'))
      .subscribe({
      next: () => {
        this.busy.set(null);
        this.load();
      },
      error: (failure: unknown) => {
        if (isSessionExpired(failure)) return;
        // A 409 means another reviewer decided it, or its text changed: the
        // queue is the server's, so reloading shows what is true now.
        this.busy.set(null);
        this.load();
      },
    });
  }

  private load(): void {
    this.inFlight?.unsubscribe();
    this.loading.set(true);
    this.failed.set(false);

    // A hundred products is the whole laboratory catalogue (the scale rule); a
    // shop past that would ask the catalogue for the queue's products only.
    this.inFlight = forkJoin({
      claims: this.knowledge.claims(),
      products: this.catalog.list(null, this.culture.active(), 1, 100),
    }).subscribe({
      next: ({ claims, products }) => {
        this.claims.set(claims);
        this.names.set(new Map(products.items.map((product) => [product.productId, product.name])));
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        if (isSessionExpired(failure)) return;
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }
}
