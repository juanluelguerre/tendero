import { computed, inject, resource } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { extendResource } from '@ngrx/signals/resource';
import { CultureStore } from '@tendero/shared-i18n';
import { SPEECH_TO_TEXT, type Listening } from '../speech-to-text';

/** What the microphone says beside the box: listening, words landed, or not understood. Null says nothing. */
export type MicrophoneStatus = 'listening' | 'landed' | 'failed' | null;

interface MicrophoneState {
  readonly status: MicrophoneStatus;
}

/**
 * One microphone's state (ADR 0047): whether the adapter can listen in the
 * page's language, and what listening came to. It depends on `SpeechToText` and
 * never on an adapter (ADR 0061), so the engine behind it can change.
 */
export const MicrophoneStore = signalStore(
  withState<MicrophoneState>({ status: null }),
  withProps(() => {
    const speech = inject(SPEECH_TO_TEXT);
    const culture = inject(CultureStore).active;
    return {
      _speech: speech,
      _culture: culture,
      /** The question being listened to, and whether Escape asked to drop it. */
      _current: { listening: null as Listening | null, cancelled: false },
      /** Asked again when the language changes: an adapter may listen in one and not the other. */
      _available: extendResource(
        resource({ params: () => culture(), loader: ({ params }) => speech.available(params) }),
      ),
    };
  }),
  withComputed(({ _available, status }) => ({
    available: computed(() => _available.value() === true),
    listening: computed(() => status() === 'listening'),
  })),
  withMethods((store) => ({
    /**
     * Pressed: listens, or finishes listening. Gives the words heard, or null when
     * there are none to hand over — nothing heard, a failure, a cancel, or the
     * press that only finished.
     */
    async press(): Promise<string | null> {
      if (store._current.listening) {
        store._current.listening.stop();
        return null;
      }

      const listening = store._speech.listen(store._culture());
      store._current.listening = listening;
      store._current.cancelled = false;
      patchState(store, { status: 'listening' });

      const heard = await listening.heard;
      store._current.listening = null;
      if (store._current.cancelled) {
        patchState(store, { status: null });
        return null;
      }
      patchState(store, { status: heard.kind === 'words' ? 'landed' : 'failed' });
      return heard.kind === 'words' ? heard.words : null;
    },
    /** Escape while listening: stop, and hand nothing over. */
    cancel(): void {
      if (!store._current.listening) return;
      store._current.cancelled = true;
      store._current.listening.stop();
    },
    /** The question was sent, or the box changed: the line beside it goes. */
    clear(): void {
      if (store.status() !== 'listening') patchState(store, { status: null });
    },
  })),
);
