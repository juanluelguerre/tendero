import { Component, computed, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ResultCell, ResultTable } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';

/** A cell as the table draws it: its text in the reader's culture, and whether it lines up as a figure. */
interface Shown {
  readonly text: string;
  readonly term: boolean;
}

/**
 * The rows a read returned, drawn as a table (P12-10). The server builds it from
 * the read — the columns say how each is formatted — so what is on screen is what
 * was read and not the model's retelling of it; the headings and the shop's own
 * words (a status, a reason) are translated here, and the figures are written in
 * the reader's culture. Nothing in it is markup: cells are text.
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

  /** Which columns line up as figures. */
  protected readonly numeric = computed(() =>
    this.table().columns.map((column) => column.kind === 'Number' || column.kind === 'Money' || column.kind === 'Days'),
  );

  protected readonly rows = computed<readonly (readonly Shown[])[]>(() => {
    const { columns, rows, currency } = this.table();
    const culture = this.culture.active();
    const plain = new Intl.NumberFormat(culture, { maximumFractionDigits: 2 });
    const days = new Intl.NumberFormat(culture, { maximumFractionDigits: 1 });
    const money = new Intl.NumberFormat(culture, { style: 'currency', currency: currency ?? 'EUR' });
    const show = (cell: ResultCell, kind: ResultTable['columns'][number]['kind']): Shown => {
      if (cell.number !== null && cell.number !== undefined) {
        const figure = kind === 'Money' ? money : kind === 'Days' ? days : plain;
        return { text: figure.format(cell.number), term: false };
      }
      return { text: cell.text ?? '—', term: kind === 'Term' && cell.text !== null };
    };
    return rows.map((row) => row.map((cell, index) => show(cell, columns[index].kind)));
  });
}
