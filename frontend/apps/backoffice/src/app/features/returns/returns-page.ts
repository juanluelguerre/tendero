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
  templateUrl: './returns-page.html',
  styleUrl: './returns-page.css',
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
