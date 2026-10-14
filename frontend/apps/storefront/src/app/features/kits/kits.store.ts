import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { extendResource } from '@ngrx/signals/resource';
import { CultureStore } from '@tendero/shared-i18n';
import { KitService } from '../../data-access/kit.service';

/** The kits the shop offers, read once per language (phase 13c, K-5). */
export const KitsStore = signalStore(
  withProps(() => {
    const kits = inject(KitService);
    const culture = inject(CultureStore);
    return {
      _templates: extendResource(
        rxResource({
          params: () => ({ culture: culture.active() }),
          stream: ({ params }) => kits.list(params.culture),
        }),
      ),
    };
  }),
  withComputed(({ _templates }) => ({
    templates: computed(() => _templates.value() ?? []),
    state: computed<'loading' | 'failed' | 'ready'>(() =>
      _templates.status() === 'error' ? 'failed' : _templates.hasValue() ? 'ready' : 'loading',
    ),
  })),
);
