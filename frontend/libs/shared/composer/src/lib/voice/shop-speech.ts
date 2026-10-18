import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { SpeechService } from '../data-access/speech.service';
import { MICROPHONE } from './microphone';
import { heardOf, type Heard, type Listening, type SpeechToText } from './speech-to-text';
import { SAMPLE_RATE, wavOf } from './wav';

/** The shortest recording worth asking about (ADR 0061): a quarter of a second. */
const MIN_SAMPLES = SAMPLE_RATE / 4;

/**
 * The shop's own engine: the microphone's audio, as the WAV the shop listens to,
 * sent to the API, which asks its whisper.cpp server and keeps nothing (the voice
 * spec, V-1). Which engine and model the server runs is the server's business:
 * this adapter knows the endpoint and nothing behind it.
 */
@Injectable({ providedIn: 'root' })
export class ShopSpeechToText implements SpeechToText {
  readonly name = 'shop' as const;

  private readonly microphone = inject(MICROPHONE);
  private readonly speech = inject(SpeechService);

  /** Any language the server listens in: Whisper is multilingual, so only the microphone and the server decide. */
  async available(_culture?: string): Promise<boolean> {
    if (!this.microphone.present) return false;
    try {
      return (await firstValueFrom(this.speech.status())).available;
    } catch {
      return false;
    }
  }

  listen(culture: string): Listening {
    const recording = this.microphone.record();
    let stopRequested: () => void = () => undefined;
    const stopped = new Promise<void>((resolve) => (stopRequested = resolve));

    const heard = (async (): Promise<Heard> => {
      try {
        const active = await recording;
        await stopped;
        const samples = await active.stop();
        if (samples.length < MIN_SAMPLES) return { kind: 'nothing' };
        const answer = await firstValueFrom(this.speech.transcribe(wavOf(samples), culture));
        return answer.heard ? heardOf(answer.words) : { kind: 'nothing' };
      } catch {
        // Permission refused, no device, the server down: the box stays as it was.
        return { kind: 'failed' };
      }
    })();

    return { stop: () => stopRequested(), heard };
  }
}
