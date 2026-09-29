import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

/** Which rows a page shows, counted from one, and how many pages there are. */
export interface PageRange {
  readonly from: number;
  readonly to: number;
  readonly pages: number;
}

/**
 * The rows a page holds out of the whole: "21–40 of 312". Pure, so the edges —
 * the last, short page, an empty list, a page past the end — are tested without
 * a browser.
 */
export function rangeOf(page: number, pageSize: number, total: number): PageRange {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const current = Math.min(Math.max(1, page), pages);
  return {
    from: total === 0 ? 0 : (current - 1) * pageSize + 1,
    to: Math.min(current * pageSize, total),
    pages,
  };
}

/**
 * The foot of every list of work that can be longer than a page (R-7.2). Its
 * first job is to say how much there is — a list that showed the newest fifty
 * and said nothing was the reason for it — and then to go back and forth.
 * Previous and next rather than numbered pages, as Polaris does: the lists are
 * worked in order, and the page lives in the URL, so a person can still share
 * page three.
 */
@Component({
  selector: 'backoffice-pager',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './pager.html',
  styleUrl: './pager.css',
})
export class Pager {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly total = input.required<number>();
  readonly go = output<number>();

  protected readonly range = computed(() => rangeOf(this.page(), this.pageSize(), this.total()));
}
