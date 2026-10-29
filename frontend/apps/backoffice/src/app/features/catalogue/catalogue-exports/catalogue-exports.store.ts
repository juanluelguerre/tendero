import { inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { patchState, signalStore, withMethods, withProps, withState } from '@ngrx/signals';
import { refusalSentence } from '@tendero/shared-util';
import { firstValueFrom } from 'rxjs';
import { type CatalogueFormat, ExportsService } from '../../../data-access/exports.service';

interface CatalogueExportsState {
  readonly downloading: boolean;
  /** Why the last download failed, in the server's words when it gave some; null otherwise. */
  readonly problem: string | null;
}

/**
 * The catalogue's exports (phase 16, E-3; ADR 0047): taking the catalogue to another
 * platform, one request that answers a file. The store reads; the panel hands the
 * file to the browser, which is the DOM's business.
 */
export const CatalogueExportsStore = signalStore(
  withState<CatalogueExportsState>({ downloading: false, problem: null }),
  withProps(() => ({
    _exports: inject(ExportsService),
    _transloco: inject(TranslocoService),
  })),
  withMethods((store) => ({
    /** The catalogue in the platform's format, or null — with a sentence — when it could not be taken. */
    async downloadCatalogue(format: CatalogueFormat): Promise<Blob | null> {
      patchState(store, { downloading: true, problem: null });
      try {
        const file = await firstValueFrom(store._exports.catalogue(format));
        patchState(store, { downloading: false });
        return file;
      } catch (failure) {
        patchState(store, {
          downloading: false,
          problem:
            refusalSentence((failure as { error?: unknown }).error) ||
            store._transloco.translate('catalogue.exports.failed'),
        });
        return null;
      }
    },
  })),
);
