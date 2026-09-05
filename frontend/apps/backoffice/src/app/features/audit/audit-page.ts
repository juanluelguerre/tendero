import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { AuditEntryView } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { formatDateTime } from '@tendero/shared-util';
import { CatalogService } from '../../data-access/catalog.service';

/** What the filter can be set to. `null` is everything. */
type Filter = 'denied' | 'failed' | 'allowed' | null;

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
@Component({
  selector: 'backoffice-audit',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './audit-page.html',
  styleUrl: './audit-page.css',
})
export class AuditPage {
  private readonly catalog = inject(CatalogService);
  private readonly culture = inject(CultureStore);

  protected readonly entries = signal<AuditEntryView[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  /** Opens on the refusals. Everything else is one click away. */
  protected readonly filter = signal<Filter>('denied');

  /** Which payload is open. One at a time: they are JSON blobs, and two of them
   *  side by side is a screen nobody can read. */
  protected readonly opened = signal<string | null>(null);

  protected readonly refused = computed(
    () => this.entries().filter((entry) => entry.outcome !== 'allowed').length,
  );

  protected readonly filters: readonly Filter[] = ['denied', 'failed', 'allowed', null];

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

    this.catalog.audit(this.filter()).subscribe({
      next: (log) => {
        this.entries.set(log.entries);
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
