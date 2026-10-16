import { computed, inject, resource } from '@angular/core';
import { patchState, signalStore, withComputed, withHooks, withMethods, withProps, withState } from '@ngrx/signals';
import { extendResource } from '@ngrx/signals/resource';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry, type TrackedEvents } from '@tendero/shared-telemetry';
import { SPEECH_TO_TEXT, type Listening } from '../speech-to-text';

/** What the microphone says beside the box: listening, words landed, or not understood. Null says nothing. */
export type MicrophoneStatus = 'listening' | 'landed' | 'failed' | null;

/** Which box the microphone writes into: the tracking plan's `where`. */
export type MicrophonePlace = TrackedEvents['voice_input_heard']['where'];

interface MicrophoneState {
  readonly status: MicrophoneStatus;
}

/**
 * One microphone's state (ADR 0047): whether the adapter can listen in the
 * page's language, what listening came to, and what became of the words it
 * wrote into the box — the tracking plan's *Speak to the shop* (ADR 0036). It
 * depends on `SpeechToText` and never on an adapter (ADR 0061), so the engine
 * behind it can change.
 */
export const MicrophoneStore = signalStore(
  withState<MicrophoneState>({ status: null }),
  withProps(() => {
    const speech = inject(SPEECH_TO_TEXT);
    const culture = inject(CultureStore).active;
    return {
      _speech: speech,
      _culture: culture,
      _telemetry: inject(Telemetry),
      /**
       * The question being listened to, whether Escape asked to drop it, the box
       * it is for, and the words last written there and not yet sent or dropped.
       */
      _current: {
        listening: null as Listening | null,
        cancelled: false,
        place: 'tino' as MicrophonePlace,
        landed: null as string | null,
      },
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
  withMethods((store) => {
    /** What became of the words written into the box. Never the words themselves. */
    const used = (how: TrackedEvents['voice_transcript_used']['how']) => {
      if (store._current.landed === null) return;
      store._current.landed = null;
      store._telemetry.trackEvent('voice_transcript_used', {
        where: store._current.place,
        adapter: store._speech.name,
        how,
      });
    };
    const heard = (outcome: TrackedEvents['voice_input_heard']['outcome']) =>
      store._telemetry.trackEvent('voice_input_heard', {
        where: store._current.place,
        adapter: store._speech.name,
        outcome,
      });

    return {
      /**
       * Pressed: listens, or finishes listening. Gives the words heard, or null when
       * there are none to hand over — nothing heard, a failure, a cancel, or the
       * press that only finished. Speaking again drops the words not yet sent.
       */
      async press(place: MicrophonePlace): Promise<string | null> {
        if (store._current.listening) {
          store._current.listening.stop();
          return null;
        }

        used('discarded');
        const listening = store._speech.listen(store._culture());
        store._current.listening = listening;
        store._current.cancelled = false;
        store._current.place = place;
        patchState(store, { status: 'listening' });

        const result = await listening.heard;
        store._current.listening = null;
        if (store._current.cancelled) {
          heard('cancelled');
          patchState(store, { status: null });
          return null;
        }
        heard(result.kind);
        patchState(store, { status: result.kind === 'words' ? 'landed' : 'failed' });
        if (result.kind !== 'words') return null;
        store._current.landed = result.words;
        return result.words;
      },
      /** Escape while listening: stop, and hand nothing over. */
      cancel(): void {
        if (!store._current.listening) return;
        store._current.cancelled = true;
        store._current.listening.stop();
      },
      /** The box changed: the line beside it goes. */
      clear(): void {
        if (store.status() !== 'listening') patchState(store, { status: null });
      },
      /** The box's question was sent: the line goes, and the words it held are counted as used. */
      sent(question: string): void {
        if (store._current.landed !== null)
          used(question.trim() === store._current.landed.trim() ? 'as_heard' : 'edited');
        if (store.status() !== 'listening') patchState(store, { status: null });
      },
      /** The box went away with words in it nobody sent. */
      _left(): void {
        used('discarded');
      },
    };
  }),
  withHooks({ onDestroy: (store) => store._left() }),
);
