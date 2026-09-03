import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ReturnView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice, without } from '@tendero/shared-util';
import { OrderingService, type ReturnDecision } from '../../data-access/ordering.service';

/**
 * The returns queue.
 *
 * The column worth the room is **what is coming back and why**: the reason is a
 * closed set of five, and it is the input to the return-reason analysis a later
 * phase promises. A text box here would have made that analysis a language model
 * guessing at what somebody typed.
 *
 * The two-step shape is the point of the screen. Approving is a promise;
 * receiving is a fact, and **only receiving restocks** — a shop that put goods
 * back on the shelf when it said yes would be selling parcels that are still in
 * the post. Damaged goods never restock at all, which is one line of code and
 * the difference between a returns loop and a way to sell broken things twice.
 */
@Component({
  selector: 'backoffice-returns',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section *transloco="let t">
      <header class="page-head">
        <h1 class="page-title">{{ t('returns.title') }}</h1>
        @if (requests().length > 0) {
          <span class="tag tag--warn">{{ t('returns.openCount', { count: requests().length }) }}</span>
        }
      </header>
      <p class="page-hint">{{ t('returns.hint') }}</p>

      @if (loading()) {
        <p class="muted" role="status" aria-live="polite">{{ t('returns.loading') }}</p>
      } @else if (failed()) {
        <p class="failed" role="alert">{{ t('returns.failed') }}</p>
      } @else if (requests().length === 0) {
        <div class="empty"><p class="empty__text">{{ t('returns.empty') }}</p></div>
      } @else {
        <div class="table-wrap">
          <table class="table">
            <caption class="sr-only">{{ t('returns.title') }}</caption>
            <thead>
              <tr>
                <th scope="col">{{ t('returns.column.return') }}</th>
                <th scope="col">{{ t('returns.column.order') }}</th>
                <th scope="col">{{ t('returns.column.what') }}</th>
                <th scope="col">{{ t('returns.column.status') }}</th>
                <th scope="col" class="numeric">{{ t('returns.column.refund') }}</th>
                <th scope="col"><span class="sr-only">{{ t('returns.column.action') }}</span></th>
              </tr>
            </thead>
            <tbody>
              @for (request of requests(); track request.returnId) {
                <tr>
                  <td class="numeric code" [title]="request.returnId">{{ shortId(request.returnId) }}</td>
                  <td class="numeric" [title]="request.orderId">{{ shortId(request.orderId) }}</td>
                  <td>
                    <ul class="what">
                      @for (line of request.lines; track line.sku) {
                        <li class="what__line">
                          <span class="numeric">{{ line.sku }} ×{{ line.quantity }}</span>
                          <span class="reason" [class.reason--damaged]="line.reason === 'Damaged'">
                            {{ t('returns.reason.' + line.reason) }}
                          </span>
                        </li>
                      }
                    </ul>
                  </td>
                  <td>
                    <span class="status" [class]="'status--' + request.status">
                      {{ t('returns.status.' + request.status) }}
                    </span>
                  </td>
                  <td class="numeric refund">
                    {{ request.refundAmount ? money(request.refundAmount) : '—' }}
                  </td>
                  <td class="actions">
                    @for (decision of decisions(request); track decision) {
                      <button
                        class="button button--small"
                        [class.button--quiet]="decision !== decisions(request)[0]"
                        type="button"
                        [disabled]="busy() === request.returnId"
                        (click)="decide(request, decision)"
                      >
                        {{ busy() === request.returnId ? t('returns.deciding') : t('returns.decide.' + decision) }}
                      </button>
                    }
                  </td>
                </tr>

                @if (refusal()[request.returnId]; as reason) {
                  <tr class="refusal">
                    <td colspan="6">{{ reason }}</td>
                  </tr>
                }
              }
            </tbody>
          </table>
        </div>
      }
    </section>
  `,
  styles: `
    .code { color: var(--text); font-weight: 500; }
    .actions { text-align: end; white-space: nowrap; }
    .actions .button + .button { margin-inline-start: var(--space-2); }
    .refund { font-weight: 600; }

    .what { margin: 0; padding: 0; list-style: none; display: grid; gap: 2px; }

    .what__line {
      display: flex;
      align-items: baseline;
      gap: var(--space-2);
      font-size: var(--text-3xs);
    }

    .reason { color: var(--text-muted); }

    /* The one reason that changes what happens: damaged goods do not go back on
       sale, so the row that will not restock is the row to notice. */
    .reason--damaged { color: var(--danger); }

    .status {
      display: inline-block;
      padding: 1px var(--space-2);
      border: 1px solid currentColor;
      border-radius: var(--radius-sm);
      font-size: var(--text-3xs);
      white-space: nowrap;
    }

    .status--Requested { color: var(--text-subtle); }
    .status--Approved { color: var(--warning); }
    .status--Received { color: var(--accent); }
    .status--Refunded { color: var(--positive); }
    .status--Rejected, .status--Cancelled { color: var(--danger); }

    .refusal td {
      padding-block: var(--space-2);
      color: var(--danger);
      font-size: var(--text-xs);
    }
  `,
})
export class ReturnsPage {
  private readonly ordering = inject(OrderingService);
  private readonly culture = inject(CultureStore);

  protected readonly requests = signal<ReturnView[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busy = signal<string | null>(null);
  protected readonly refusal = signal<Record<string, string>>({});

  /**
   * What can be done next, from the status. The transition table is the
   * server's — receiving something that was never approved throws there — and
   * this only decides which buttons are worth offering.
   */
  protected decisions(request: ReturnView): ReturnDecision[] {
    switch (request.status) {
      case 'Requested':
        return ['Approve', 'Reject'];
      case 'Approved':
        return ['Receive'];
      case 'Received':
        // Rejected FROM received is the edge that is easy to leave out: the
        // parcel arrived and the goods are not what the reason claimed.
        return ['Refund', 'Reject'];
      default:
        return [];
    }
  }

  protected shortId(id: string): string {
    return id.split('-')[0];
  }

  protected money(amount: number): string {
    return formatPrice(amount, 'EUR', this.culture.active());
  }

  protected decide(request: ReturnView, decision: ReturnDecision): void {
    this.busy.set(request.returnId);
    this.refusal.update((current) => without(current, request.returnId));

    this.ordering.decide(request.returnId, decision).subscribe({
      next: (updated) => {
        // The queue only holds what is still waiting on somebody, so a refunded
        // or rejected return leaves it. Reloading would be the same list minus
        // one row and a round trip.
        this.requests.update((current) =>
          updated.status === 'Refunded' || updated.status === 'Rejected'
            ? current.filter((candidate) => candidate.returnId !== updated.returnId)
            : current.map((candidate) =>
                candidate.returnId === updated.returnId ? updated : candidate,
              ),
        );
        this.busy.set(null);
      },
      error: (error: { error?: unknown }) => {
        this.busy.set(null);
        this.refusal.update((current) => ({
          ...current,
          [request.returnId]:
            typeof error.error === 'string' ? error.error : 'The return refused that decision.',
        }));
      },
    });
  }

  constructor() {
    this.ordering.returns().subscribe({
      next: (result) => {
        this.requests.set(result);
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }
}
