import { InjectionToken } from '@angular/core';

/** What listening came to: words for the box, nothing heard, or a failure. Never half a question. */
export type Heard =
  { readonly kind: 'words'; readonly words: string } | { readonly kind: 'nothing' } | { readonly kind: 'failed' };

/** A question being listened to: stopped by the shopper, or ending on its own. */
export interface Listening {
  stop(): void;
  readonly heard: Promise<Heard>;
}

/**
 * Words from a spoken question (the voice spec, V-1; ADR 0061). **Every engine
 * behind this**, by the author's ruling: the microphone and the stores depend on
 * this interface and never on an adapter, so the engine — and the model behind
 * it — can change. A new adapter joins `speech-to-text.spec.ts` before it is used.
 */
export interface SpeechToText {
  /** Which adapter: for the evidence of use, never for a shopper. */
  readonly name: 'web-speech' | 'shop';
  /** Whether it can listen in this language here. The microphone is drawn only when it can (invariant 8). */
  available(culture: string): Promise<boolean>;
  listen(culture: string): Listening;
}

/** The adapter the storefront listens with; which one is configuration (`app.config.ts`). */
export const SPEECH_TO_TEXT = new InjectionToken<SpeechToText>('SpeechToText');

/** What an adapter calls a page's language: the regional tag the engines know. */
export const speechLanguage = (culture: string): string => (culture === 'en' ? 'en-US' : 'es-ES');

/** Words, or nothing heard when they are only blanks. */
export const heardOf = (words: string | null | undefined): Heard => {
  const trimmed = (words ?? '').trim();
  return trimmed.length > 0 ? { kind: 'words', words: trimmed } : { kind: 'nothing' };
};
