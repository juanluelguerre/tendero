import { heardOf, speechLanguage, type Heard, type Listening, type SpeechToText } from './speech-to-text';

/** The parts of the browser's SpeechRecognition this adapter uses; not in TypeScript's DOM types everywhere. */
interface Recognition {
  lang: string;
  processLocally: boolean;
  interimResults: boolean;
  onresult: ((event: { results: ArrayLike<{ 0: { transcript: string }; isFinal: boolean }> }) => void) | null;
  onerror: ((event: { error: string }) => void) | null;
  onend: (() => void) | null;
  start(): void;
  stop(): void;
}

interface RecognitionConstructor {
  new (): Recognition;
  available?(options: { langs: string[]; processLocally: boolean }): Promise<string>;
}

const constructorOf = (): RecognitionConstructor | undefined =>
  (globalThis as { SpeechRecognition?: RecognitionConstructor; webkitSpeechRecognition?: RecognitionConstructor })
    .SpeechRecognition ?? (globalThis as { webkitSpeechRecognition?: RecognitionConstructor }).webkitSpeechRecognition;

/**
 * The browser's own speech recognition, **on the device only** (the author's
 * ruling of 2026-10-16): asked with `processLocally`, and not offered where the
 * browser cannot recognise this language locally — there it would send the audio
 * to its maker's servers. Feature-detected, as everything the shop takes from the
 * browser (invariant 8).
 */
export class WebSpeechToText implements SpeechToText {
  readonly name = 'web-speech' as const;

  async available(culture: string): Promise<boolean> {
    const recognition = constructorOf();
    if (!recognition?.available) return false;
    try {
      return (await recognition.available({ langs: [speechLanguage(culture)], processLocally: true })) === 'available';
    } catch {
      return false;
    }
  }

  listen(culture: string): Listening {
    const Recognition = constructorOf();
    if (!Recognition) return { stop: () => undefined, heard: Promise.resolve({ kind: 'failed' }) };

    const recognition = new Recognition();
    recognition.lang = speechLanguage(culture);
    recognition.processLocally = true;
    recognition.interimResults = false;

    const heard = new Promise<Heard>((resolve) => {
      let words = '';
      let failed = false;
      recognition.onresult = (event) => {
        words = Array.from(event.results, (result) => result[0].transcript).join(' ');
      };
      recognition.onerror = (event) => {
        // Nobody speaking is nothing heard, not a failure.
        if (event.error !== 'no-speech' && event.error !== 'aborted') failed = true;
      };
      recognition.onend = () => resolve(failed ? { kind: 'failed' } : heardOf(words));
    });

    recognition.start();
    return { stop: () => recognition.stop(), heard };
  }
}
