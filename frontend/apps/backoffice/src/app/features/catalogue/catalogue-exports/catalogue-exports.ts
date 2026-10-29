import { DOCUMENT } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { API_BASE_URL } from '@tendero/shared-util';
import type { CatalogueFormat } from '../../../data-access/exports.service';
import { CatalogueExportsStore } from './catalogue-exports.store';

/**
 * What leaves the catalogue (phase 16, E-2 and E-3; docs/specs/exports.md): the
 * product feed's address in each language the shop speaks, for the shopkeeper to
 * give Merchant Center, which fetches it on its own schedule (decision 5); and the
 * catalogue as another platform imports it, downloaded. Folded until asked for: it
 * is set up once, not used every day.
 *
 * Whether it is open and what was just copied live only while the page is on
 * screen; the download is the store's, since it asks the server.
 */
@Component({
  selector: 'backoffice-catalogue-exports',
  imports: [TranslocoDirective],
  templateUrl: './catalogue-exports.html',
  host: { class: 'block' },
  providers: [CatalogueExportsStore],
})
export class CatalogueExports {
  private readonly api = inject(API_BASE_URL);
  private readonly document = inject(DOCUMENT);
  protected readonly store = inject(CatalogueExportsStore);

  protected readonly open = signal(false);
  protected readonly copied = signal<string | null>(null);

  /** On the API; on the page itself when the API is reached through it (the dev server's proxy). */
  protected readonly feeds = (['es', 'en'] as const).map((culture) => ({
    culture,
    url: `${this.api || this.document.location.origin}/feeds/google/${culture}.tsv`,
  }));

  protected async copy(url: string): Promise<void> {
    await this.document.defaultView?.navigator.clipboard?.writeText(url);
    this.copied.set(url);
  }

  /** The file handed to the browser: the DOM's business, so the panel's and not the store's. */
  protected async download(format: CatalogueFormat): Promise<void> {
    const file = await this.store.downloadCatalogue(format);
    if (!file) return;

    const url = URL.createObjectURL(file);
    const link = this.document.createElement('a');
    link.href = url;
    link.download = `tendero-catalogue-${format}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  }
}
