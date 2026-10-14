import { Component, computed, inject, input, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { CellKind, ResultCell, ResultTable } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { ShopCurrency } from '@tendero/shared-util';

/** A column as the table draws it: its heading's key, and whether it lines up as figures. */
interface Heading {
  readonly key: string;
  readonly numeric: boolean;
}

/** A cell as the table draws it: its text in the reader's culture, and whether it is one of the shop's terms. */
interface Shown {
  readonly text: string;
  readonly term: boolean;
  readonly numeric: boolean;
}

const Figures: readonly CellKind[] = ['Number', 'Money', 'Days'];

/** How many rows a table shows before it is asked for the rest: a conversation is read, not scrolled past. */
export const RowsAtFirst = 8;

/**
 * The rows a read returned, drawn as a table (P12-10). The server builds it from
 * the read — the columns say how each is formatted — so what is on screen is what
 * was read and not the model's retelling of it; the headings and the shop's own
 * words (a status, a reason) are translated here, and the figures are written in
 * the reader's culture. Nothing in it is markup: cells are text.
 *
 * The panel is narrow, so a column with nothing in any row is not drawn — the
 * languages a name is missing, when none is — and figures and codes keep to one
 * line while a name wraps. And a long read shows its first rows, with the rest
 * one press away: fifty rows would push the next question off the screen.
 */
@Component({
  selector: 'backoffice-steward-table',
  imports: [TranslocoDirective],
  templateUrl: './steward-table.html',
  host: { class: 'block' },
})
export class StewardTable {
  readonly table = input.required<ResultTable>();

  private readonly culture = inject(CultureStore);

  /** Whether every row is shown; a table that fits has nothing to expand. */
  protected readonly expanded = signal(false);

  protected readonly total = computed(() => this.table().rows.length);

  protected readonly long = computed(() => this.total() > RowsAtFirst);

  /** The columns that hold something in at least one row, by their place in the table. */
  private readonly drawn = computed(() => {
    const { columns, rows } = this.table();
    return columns.map((_, index) => index).filter((index) => rows.some((row) => holds(row[index])));
  });

  protected readonly headings = computed<readonly Heading[]>(() => {
    const { columns } = this.table();
    return this.drawn().map((index) => ({ key: columns[index].key, numeric: Figures.includes(columns[index].kind) }));
  });

  protected readonly rows = computed<readonly (readonly Shown[])[]>(() => {
    const { columns, currency } = this.table();
    const rows = this.expanded() ? this.table().rows : this.table().rows.slice(0, RowsAtFirst);
    const culture = this.culture.active();
    const plain = new Intl.NumberFormat(culture, { maximumFractionDigits: 2 });
    const days = new Intl.NumberFormat(culture, { maximumFractionDigits: 1 });
    const money = new Intl.NumberFormat(culture, { style: 'currency', currency: currency ?? ShopCurrency });
    const show = (cell: ResultCell, kind: CellKind): Shown => {
      const numeric = Figures.includes(kind);
      if (cell.number !== null && cell.number !== undefined) {
        const figure = kind === 'Money' ? money : kind === 'Days' ? days : plain;
        return { text: figure.format(cell.number), term: false, numeric };
      }
      return { text: cell.text || '—', term: kind === 'Term' && !!cell.text, numeric };
    };
    return rows.map((row) => this.drawn().map((index) => show(row[index], columns[index].kind)));
  });
}

/** Whether a cell says anything: a figure, or text that is not empty. */
function holds(cell: ResultCell | undefined): boolean {
  return !!cell && ((cell.number !== null && cell.number !== undefined) || !!cell.text);
}
