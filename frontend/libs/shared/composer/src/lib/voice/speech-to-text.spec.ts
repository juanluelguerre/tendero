import { TestBed } from '@angular/core/testing';
import type { SpeechAvailability, TranscriptionView } from '@tendero/shared-api';
import { Observable, of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { SpeechService } from '../data-access/speech.service';
import { MICROPHONE, type Microphone } from './microphone';
import { ShopSpeechToText } from './shop-speech';
import type { SpeechToText } from './speech-to-text';
import { WebSpeechToText } from './web-speech';

/**
 * What a script makes the engine do once listening starts: say these words,
 * hear nothing, or fail.
 */
type Script = { readonly say: string } | { readonly silent: true } | { readonly fails: true };

/** One adapter, its fakes set up to follow a script, and whether it can listen at all. */
interface Arrangement {
  readonly name: string;
  make(script: Script, available: boolean): SpeechToText;
}

// ---------- web-speech: the browser's SpeechRecognition, faked ----------

class FakeRecognition {
  static script: Script = { silent: true };
  static onDevice: 'available' | 'unavailable' = 'available';
  static asked: { langs: string[]; processLocally: boolean }[] = [];
  static last: FakeRecognition | null = null;

  static async available(options: { langs: string[]; processLocally: boolean }): Promise<string> {
    FakeRecognition.asked.push(options);
    return FakeRecognition.onDevice;
  }

  lang = '';
  processLocally = false;
  interimResults = true;
  onresult: ((event: { results: { 0: { transcript: string }; isFinal: boolean }[] }) => void) | null = null;
  onerror: ((event: { error: string }) => void) | null = null;
  onend: (() => void) | null = null;

  start(): void {
    FakeRecognition.last = this;
    const script = FakeRecognition.script;
    queueMicrotask(() => {
      if ('say' in script) this.onresult?.({ results: [{ 0: { transcript: script.say }, isFinal: true }] });
      if ('silent' in script) this.onerror?.({ error: 'no-speech' });
      if ('fails' in script) this.onerror?.({ error: 'network' });
      this.onend?.();
    });
  }

  stop(): void {
    /* the script ends on its own */
  }
}

const webSpeech: Arrangement = {
  name: 'web-speech',
  make(script, available) {
    FakeRecognition.script = script;
    FakeRecognition.onDevice = available ? 'available' : 'unavailable';
    (globalThis as { SpeechRecognition?: unknown }).SpeechRecognition = FakeRecognition;
    return new WebSpeechToText();
  },
};

// ---------- shop: the microphone and the server, faked ----------

const shop: Arrangement = {
  name: 'shop',
  make(script, available) {
    const second = new Float32Array(16_000).fill(0.1);
    const microphone: Microphone = {
      present: true,
      record: async () => ({ stop: async () => second }),
    };
    const answer = (): Observable<TranscriptionView> =>
      'fails' in script
        ? throwError(() => ({ status: 503 }))
        : of({ words: 'say' in script ? script.say : '', heard: 'say' in script, engine: 'scripted' });
    TestBed.configureTestingModule({
      providers: [
        ShopSpeechToText,
        { provide: MICROPHONE, useValue: microphone },
        {
          provide: SpeechService,
          useValue: {
            status: (): Observable<SpeechAvailability> => of({ available }),
            transcribe: answer,
          },
        },
      ],
    });
    return TestBed.inject(ShopSpeechToText);
  },
};

/**
 * The front end's contract (the voice spec, V-1; ADR 0061): every adapter
 * behind `SpeechToText` hears words, hears nothing, or fails, and says when it
 * cannot listen at all. A new one is added here before it is used.
 */
describe.each([webSpeech, shop])('the $name adapter', (arrangement) => {
  afterEach(() => {
    delete (globalThis as { SpeechRecognition?: unknown }).SpeechRecognition;
    TestBed.resetTestingModule();
  });

  const heard = (script: Script) => arrangement.make(script, true).listen('es');

  it('gives the words it heard, trimmed', async () => {
    const listening = heard({ say: '  zapatillas para correr ' });
    listening.stop();

    expect(await listening.heard).toEqual({ kind: 'words', words: 'zapatillas para correr' });
  });

  it('says nothing was heard rather than giving empty words', async () => {
    const listening = heard({ silent: true });
    listening.stop();

    expect(await listening.heard).toEqual({ kind: 'nothing' });
  });

  it('says it failed, and never gives half a question', async () => {
    const listening = heard({ fails: true });
    listening.stop();

    expect(await listening.heard).toEqual({ kind: 'failed' });
  });

  it('says when it cannot listen at all', async () => {
    expect(await arrangement.make({ silent: true }, false).available('es')).toBe(false);
  });
});

describe('the web-speech adapter, on the device only', () => {
  beforeEach(() => (FakeRecognition.asked = []));
  afterEach(() => delete (globalThis as { SpeechRecognition?: unknown }).SpeechRecognition);

  /** The author's ruling (2026-10-16): where the browser would send the audio away, it is not offered. */
  it('asks the browser for recognition on the device, in the page language, and listens that way', async () => {
    const adapter = webSpeech.make({ say: 'hola' }, true);

    const available = await adapter.available('es');
    const listening = adapter.listen('es');
    await listening.heard;

    expect([
      available,
      FakeRecognition.asked,
      FakeRecognition.last?.lang,
      FakeRecognition.last?.processLocally,
    ]).toEqual([true, [{ langs: ['es-ES'], processLocally: true }], 'es-ES', true]);
  });

  it('cannot listen in a browser with no speech recognition', async () => {
    expect(await new WebSpeechToText().available('en')).toBe(false);
  });
});

describe('the shop adapter', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('cannot listen where the server answers nothing about speech', async () => {
    TestBed.configureTestingModule({
      providers: [
        ShopSpeechToText,
        {
          provide: MICROPHONE,
          useValue: { present: true, record: async () => ({ stop: async () => new Float32Array() }) },
        },
        {
          provide: SpeechService,
          useValue: { status: () => throwError(() => ({ status: 0 })), transcribe: () => of() },
        },
      ],
    });

    expect(await TestBed.inject(ShopSpeechToText).available('es')).toBe(false);
  });

  it('hears nothing in a tap that recorded less than a quarter of a second, and asks the server nothing', async () => {
    let asked = 0;
    TestBed.configureTestingModule({
      providers: [
        ShopSpeechToText,
        {
          provide: MICROPHONE,
          useValue: { present: true, record: async () => ({ stop: async () => new Float32Array(1_000) }) },
        },
        {
          provide: SpeechService,
          useValue: {
            status: () => of({ available: true }),
            transcribe: () => (asked++, of({ words: 'x', heard: true, engine: 'scripted' })),
          },
        },
      ],
    });
    const listening = TestBed.inject(ShopSpeechToText).listen('es');
    listening.stop();

    expect([await listening.heard, asked]).toEqual([{ kind: 'nothing' }, 0]);
  });
});
