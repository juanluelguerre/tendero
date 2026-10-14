import { computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { form, maxLength, required } from '@angular/forms/signals';
import { Router } from '@angular/router';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { extendResource } from '@ngrx/signals/resource';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { majorUnits } from '@tendero/shared-util';
import { firstValueFrom } from 'rxjs';
import { KitService } from '../../data-access/kit.service';
import { ShopLinks } from '../../shop-links';

/** What the box takes: a sentence, not a letter (the API's own limit). */
export const SentenceMaxLength = 300;

interface KitsState {
  readonly reading: boolean;
  /** The last sentence named none of the shop's kits, or could not be read: the list is there to choose from. */
  readonly notUnderstood: boolean;
}

/**
 * The kits the shop offers, read once per language (phase 13c, K-5), and the
 * box above them (K-6). A shopper writes what they want — *un kit de cocina
 * para inducción por menos de 200 euros* — and is taken to that kit with its
 * form filled from the address, as any other ask is. The model only named
 * the kit; the budget and the requirements were read by code, and the kit is
 * built by the solver on the kit's page, where the shopper sees and corrects
 * what was understood. With no model the box is not drawn, and the list is
 * the page it was (invariant 8).
 */
export const KitsStore = signalStore(
  withState<KitsState>({ reading: false, notUnderstood: false }),
  withProps(() => {
    const kits = inject(KitService);
    const culture = inject(CultureStore);
    const sentence = signal({ text: '' });
    return {
      _kits: kits,
      _culture: culture,
      _router: inject(Router),
      _links: inject(ShopLinks),
      _telemetry: inject(Telemetry),
      sentence,
      sentenceForm: form(
        sentence,
        (box) => {
          required(box.text);
          maxLength(box.text, SentenceMaxLength);
        },
        { name: 'kitSentence' },
      ),
      _templates: extendResource(
        rxResource({
          params: () => ({ culture: culture.active() }),
          stream: ({ params }) => kits.list(params.culture),
        }),
      ),
      _sentenceStatus: extendResource(rxResource({ stream: () => kits.sentenceStatus() })),
    };
  }),
  withComputed(({ _templates, _sentenceStatus }) => ({
    templates: computed(() => _templates.value() ?? []),
    state: computed<'loading' | 'failed' | 'ready'>(() =>
      _templates.status() === 'error' ? 'failed' : _templates.hasValue() ? 'ready' : 'loading',
    ),
    /** Whether to draw the box: only when the shop says it can read a sentence. */
    offersSentence: computed(() => _sentenceStatus.value()?.available === true),
  })),
  withMethods((store) => ({
    /** Reads the sentence and, when it names a kit, goes there with what it said in the address. */
    async understand(): Promise<void> {
      const text = store.sentence().text.trim();
      if (!text || store.reading()) return;

      patchState(store, { reading: true, notUnderstood: false });
      try {
        const culture = store._culture.active();
        const read = await firstValueFrom(store._kits.understand(text, culture)).catch(() => null);
        store._telemetry.trackEvent('kit_sentence_read', {
          understood: read?.understood === true,
          template: (read?.understood && read.template) || 'none',
          budget: read?.budget != null,
        });

        if (!read?.understood || !read.template) {
          patchState(store, { notUnderstood: true });
          return;
        }

        await store._router.navigate(store._links.kit(read.template), {
          queryParams: {
            budget: read.budget ? `${majorUnits(read.budget)}` : null,
            needs: read.requirements.join(',') || null,
          },
          // Said by the navigation, not the address: the kit it builds is the
          // sentence's, and a reload of the page is not a second one.
          state: { kitSource: 'sentence' },
        });
      } finally {
        patchState(store, { reading: false });
      }
    },
  })),
);
