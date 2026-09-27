import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/** One filter: its key, its words and, when known, how many it would show. */
export interface FilterOption {
  readonly key: string;
  readonly label: string;
  readonly count: number | null;
}

/**
 * The filters above a list of work (R-3, P3): one pressed at a time, each with
 * its count, so a filter says what it holds before it is chosen. The row
 * scrolls inside itself on a phone, with a fade at the edge to say so.
 */
@Component({
  selector: 'backoffice-filter-chips',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './filter-chips.html',
  styleUrl: './filter-chips.css',
  host: { role: 'group', '[attr.aria-label]': 'label()' },
})
export class FilterChips {
  readonly options = input.required<readonly FilterOption[]>();
  readonly selected = input.required<string>();
  readonly label = input.required<string>();
  readonly chosen = output<string>();
}
