import { DOCUMENT } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { TranslocoDirective } from '@jsverse/transloco';
import { OrdersExportStore } from './orders-export.store';

/**
 * Orders for the accountant (phase 16, E-4; docs/specs/exports.md): a span of the
 * shop's days and a spreadsheet of its sales, in the reader's language. Folded
 * until asked for: it is used once a month, not every day. The store asks the
 * server; handing the file to the browser is the DOM's business, so the panel's.
 */
@Component({
  selector: 'backoffice-orders-export',
  imports: [TranslocoDirective, FormField],
  templateUrl: './orders-export.html',
  host: { class: 'block' },
  providers: [OrdersExportStore],
})
export class OrdersExport {
  private readonly document = inject(DOCUMENT);
  protected readonly store = inject(OrdersExportStore);
  protected readonly open = signal(false);

  protected async download(): Promise<void> {
    const file = await this.store.download();
    if (!file) return;

    const { from, to } = this.store.span();
    const url = URL.createObjectURL(file);
    const link = this.document.createElement('a');
    link.href = url;
    link.download = `tendero-orders-${from}-${to}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  }
}
