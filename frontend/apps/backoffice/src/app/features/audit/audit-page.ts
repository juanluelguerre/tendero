import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { FilterChips } from '../../worklist/filter-chips/filter-chips';
import { Pager } from '../../worklist/pager/pager';
import { CultureStore } from '@tendero/shared-i18n';
import { formatDateTime } from '@tendero/shared-util';
import { actorOf } from './audit-actor';
import { AuditStore, PageSize } from './audit.store';
import { Notice } from '@tendero/shared-ui';

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
  imports: [TranslocoDirective, FilterChips, Pager, Notice],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './audit-page.html',
  styleUrl: './audit-page.css',
  providers: [AuditStore],
})
export class AuditPage {
  protected readonly store = inject(AuditStore);
  private readonly culture = inject(CultureStore);

  protected readonly pageSize = PageSize;
  protected readonly actorOf = actorOf;

  /**
   * The active filter in the reader's own language, for the empty message.
   *
   * The translate function is passed in rather than the service injected,
   * because the template already has one from `*transloco` and a second source
   * of the same strings is how a screen ends up half-translated.
   */
  protected outcomeLabel(t: (key: string) => string): string {
    const filter = this.store.filter();
    return filter ? t('audit.outcome.' + filter).toLowerCase() : '';
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
}
