import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, linkedSignal, signal, viewChild } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective, translateObjectSignal } from '@jsverse/transloco';
import type { ReturnView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice, without } from '@tendero/shared-util';
import { OrderingService, type ReturnDecision } from '../../data-access/ordering.service';
import { type ActionVia, BackofficeUsage, type BackofficeAction } from '../../usage';
import { ConfirmDialog } from '../../work/confirm-dialog';
import { FilterChips, type FilterOption } from '../../work/filter-chips';
import { WorkPane } from '../../work/work-pane';
import { refusalOf } from '../../work/refusal';

/** Each decision as the usage event names it (ADR 0041). */
const ReturnActions = {
  Approve: 'return_approve',
  Reject: 'return_reject',
  Receive: 'return_receive',
  Refund: 'return_refund',
} as const satisfies Record<ReturnDecision, BackofficeAction>;

/** The queue's statuses in the order a return moves through them. */
const Statuses = ['Requested', 'Approved', 'Received'] as const;

/**
 * The returns queue, as R-3 drew the lists of work (R-4.3): it opens on what
 * waits for a decision, keeps the next step on the row, and shows a return
 * beside the list with its lines, their reasons and the customer's words.
 *
 * The column worth the room is **what is coming back and why**: the reason is a
 * closed set of five, the input to the return-reason analysis a later phase
 * promises. The two-step shape is the point of the screen. Approving is a
 * promise; receiving is a fact, and **only receiving restocks** — a shop that
 * put goods back when it said yes would be selling parcels still in the post.
 *
 * Rejecting cannot be undone, so it asks first and can say why; the next step
 * forward is never it.
 */
@Component({
  selector: 'backoffice-returns',
  imports: [TranslocoDirective, NgTemplateOutlet, RouterLink, WorkPane, FilterChips, ConfirmDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './returns-page.html',
  styleUrl: './returns-page.css',
})
export class ReturnsPage {
  private readonly ordering = inject(OrderingService);
  private readonly usage = inject(BackofficeUsage);
  private readonly culture = inject(CultureStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private readonly rejectDialog = viewChild(ConfirmDialog);
  private readonly filterLabels = translateObjectSignal('returns.filter');
  private readonly params = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });

  protected readonly statusKey = computed(() => this.params().get('status') ?? 'Requested');
  protected readonly selectedId = computed(() => this.params().get('return'));

  private readonly queue = rxResource({ stream: () => this.ordering.returns() });

  /** The queue, with each decision's answer put in place until it is asked again. */
  protected readonly requests = linkedSignal(() => this.queue.value() ?? []);
  protected readonly loading = computed(() => this.queue.isLoading() && !this.queue.hasValue());
  protected readonly failed = computed(() => this.queue.status() === 'error');

  protected readonly shown = computed(() => {
    const key = this.statusKey();
    return key === 'all' ? this.requests() : this.requests().filter((request) => request.status === key);
  });

  protected readonly filters = computed<FilterOption[]>(() => {
    const labels = this.filterLabels() as Record<string, string>;
    const count = (status: string) => this.requests().filter((request) => request.status === status).length;
    return [
      ...Statuses.map((status) => ({ key: status as string, label: labels[status] ?? status, count: count(status) })),
      { key: 'all', label: labels['all'] ?? 'all', count: this.requests().length },
    ];
  });

  protected readonly selected = computed(() => this.requests().find((request) => request.returnId === this.selectedId()) ?? null);

  protected readonly busy = signal<string | null>(null);
  protected readonly refusal = signal<Record<string, string>>({});

  /** The step forward from a status; the server's table still has the last word. */
  protected next(request: ReturnView): ReturnDecision | null {
    switch (request.status) {
      case 'Requested':
        return 'Approve';
      case 'Approved':
        return 'Receive';
      case 'Received':
        return 'Refund';
      default:
        return null;
    }
  }

  /**
   * Rejected FROM received is the edge that is easy to leave out: the parcel
   * arrived and the goods are not what the reason claimed.
   */
  protected rejectable(request: ReturnView): boolean {
    return request.status === 'Requested' || request.status === 'Received';
  }

  protected shortId(id: string): string {
    return id.split('-')[0];
  }

  protected money(amount: number): string {
    return formatPrice(amount, 'EUR', this.culture.active());
  }

  protected filterBy(status: string): void {
    void this.router.navigate([], { queryParams: { status: status === 'Requested' ? null : status }, queryParamsHandling: 'merge' });
  }

  protected open(request: ReturnView): void {
    void this.router.navigate([], { queryParams: { return: request.returnId }, queryParamsHandling: 'merge' });
  }

  protected close(): void {
    void this.router.navigate([], { queryParams: { return: null }, queryParamsHandling: 'merge' });
  }

  protected askToReject(): void {
    this.rejectDialog()?.open();
  }

  protected decide(request: ReturnView, decision: ReturnDecision, via: ActionVia, reason?: string): void {
    this.busy.set(request.returnId);
    this.refusal.update((current) => without(current, request.returnId));

    this.ordering
      .decide(request.returnId, decision, reason)
      .pipe(this.usage.reported('returns', ReturnActions[decision], via))
      .subscribe({
        next: (updated) => {
          this.requests.update((current) => current.map((row) => (row.returnId === updated.returnId ? updated : row)));
          this.busy.set(null);
        },
        error: (error: unknown) => {
          this.busy.set(null);
          this.refusal.update((current) => ({
            ...current,
            [request.returnId]: refusalOf(error),
          }));
        },
      });
  }
}
