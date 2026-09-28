import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective, translateObjectSignal, translateSignal } from '@jsverse/transloco';
import { FilterChips, type FilterOption } from '../../work/filter-chips';
import { Pager } from '../../work/pager';
import type { AuditEntryView, AuditOutcome } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { formatDateTime } from '@tendero/shared-util';
import { CatalogService } from '../../data-access/catalog.service';
import { actorOf } from './audit-actor';

/** What the filter can be set to. `null` is everything. */
type Filter = AuditOutcome | null;

/**
 * Who did what, and what the shop refused.
 *
 * **It opens on the refusals, not on everything**, and that is the screen's one
 * real decision. An audit log whose default view is a thousand successful
 * publishes is a changelog; the rows somebody comes here for are the ones where
 * a rule said no. They have their own partial index in the database for the same
 * reason.
 *
 * `Denied` and `Failed` are shown apart because they mean opposite things. A
 * denial is the shop WORKING — "cannot publish an archived product" — and a
 * failure is the shop broken. A screen that painted both red would teach a
 * shopkeeper to ignore the colour.
 *
 * The payload is shown on demand rather than in the row: it is the command as
 * JSON, it is the widest column by an order of magnitude, and it is the thing
 * you want only once you have found the row.
 */
/** Twenty rows a page, as every list of work (R-7.2). */
const PageSize = 20;

@Component({
  selector: 'backoffice-audit',
  imports: [TranslocoDirective, FilterChips, Pager],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './audit-page.html',
  styleUrl: './audit-page.css',
})
export class AuditPage {
  private readonly catalog = inject(CatalogService);
  private readonly culture = inject(CultureStore);

  protected readonly entries = signal<AuditEntryView[]>([]);
  protected readonly page = signal(1);
  protected readonly pageSize = PageSize;
  protected readonly total = signal(0);

  /** Every outcome's number over the whole log, kept while the next page loads (R-7.2). */
  private readonly counts = signal<ReadonlyMap<string, number> | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  /** Opens on the refusals. Everything else is one click away. */
  protected readonly filter = signal<Filter>('Denied');

  /** Which payload is open. One at a time: they are JSON blobs, and two of them
   *  side by side is a screen nobody can read. */
  protected readonly opened = signal<string | null>(null);

  protected readonly filters: readonly Filter[] = ['Denied', 'Failed', 'Allowed', null];

  private readonly outcomeLabels = translateObjectSignal('audit.outcome');
  private readonly everything = translateSignal('audit.all');

  /**
   * Every chip says its number, whichever is chosen, and all of them the log's:
   * it used to be the chosen one's, counted from the rows it had — which was
   * the page's once the log had more than one.
   */
  protected readonly filterOptions = computed<FilterOption[]>(() => {
    const labels = this.outcomeLabels() as Record<string, string>;
    const counts = this.counts();
    const all = counts ? [...counts.values()].reduce((sum, count) => sum + count, 0) : null;
    return this.filters.map((option) => ({
      key: option ?? 'all',
      label: option ? (labels[option] ?? option) : this.everything(),
      count: option ? (counts?.get(option) ?? null) : all,
    }));
  });

  /** Who acted — or a guest, which is not the system. */
  protected readonly actorOf = actorOf;

  constructor() {
    this.load();
  }

  /**
   * The active filter in the reader's own language, for the empty message.
   *
   * The translate function is passed in rather than the service injected,
   * because the template already has one from `*transloco` and a second source
   * of the same strings is how a screen ends up half-translated.
   */
  protected outcomeLabel(t: (key: string) => string): string {
    const filter = this.filter();
    return filter ? t('audit.outcome.' + filter).toLowerCase() : '';
  }

  protected use(filter: Filter): void {
    this.filter.set(filter);
    this.page.set(1);
    this.opened.set(null);
    this.load();
  }

  protected go(page: number): void {
    this.page.set(page);
    this.opened.set(null);
    this.load();
  }

  protected toggle(id: string): void {
    this.opened.update((current) => (current === id ? null : id));
  }

  /**
   * Order ids and trace ids are long and the column is not. The first segment is
   * enough to match a row against something you are already looking at, and the
   * whole value is one hover away in the title.
   */
  protected short(value: string | null | undefined): string {
    return value ? value.split('-')[0] : '';
  }

  /** 04/09/2026 and 09/04/2026 are the same instant and different days, so the
   *  reader's culture decides. */
  protected when(at: string): string {
    return formatDateTime(at, this.culture.active());
  }

  /** The command as JSON, indented. It arrives as a string because the server
   *  stores it as one — pretty-printing is a reading aid, not a transformation. */
  protected pretty(payload: string): string {
    try {
      return JSON.stringify(JSON.parse(payload), null, 2);
    } catch {
      return payload;
    }
  }

  private load(): void {
    this.loading.set(true);
    this.failed.set(false);

    this.catalog.audit(this.filter(), this.page(), PageSize).subscribe({
      next: (log) => {
        this.entries.set(log.entries);
        this.total.set(log.total);
        this.counts.set(new Map(log.counts.map((entry) => [entry.outcome, entry.count])));
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        if (isSessionExpired(failure)) return;

        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }
}
