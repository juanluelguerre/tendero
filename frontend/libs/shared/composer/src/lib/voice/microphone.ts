import { InjectionToken } from '@angular/core';
import { SAMPLE_RATE } from './wav';

/** A recording under way: stopping it gives its samples, mono at 16 kHz. */
export interface Recording {
  stop(): Promise<Float32Array>;
}

/**
 * The device's microphone, as the shop adapter needs it: present or not, and a
 * recording that ends in the samples the shop listens to. An interface so the
 * adapter is tested without a microphone, and so another way of recording is a
 * new class, not an edit.
 */
export interface Microphone {
  readonly present: boolean;
  /** Rejects when the shopper refuses the permission, or there is no device. */
  record(): Promise<Recording>;
}

/** The longest question listened to (ADR 0061): a recording stops itself there. */
export const MAX_SECONDS = 30;

/**
 * The browser's microphone: `getUserMedia` and `MediaRecorder`, then the
 * recording decoded and redrawn at 16 kHz mono with an `OfflineAudioContext`.
 * Everything the browser already has; no dependency, no worklet.
 */
export class BrowserMicrophone implements Microphone {
  get present(): boolean {
    return (
      typeof navigator !== 'undefined' && !!navigator.mediaDevices?.getUserMedia && typeof MediaRecorder !== 'undefined'
    );
  }

  async record(): Promise<Recording> {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: { channelCount: 1, echoCancellation: true } });
    const recorder = new MediaRecorder(stream);
    const chunks: Blob[] = [];
    recorder.ondataavailable = (event) => chunks.push(event.data);

    const stopped = new Promise<Blob>((resolve) => {
      recorder.onstop = () => {
        stream.getTracks().forEach((track) => track.stop());
        resolve(new Blob(chunks, { type: recorder.mimeType }));
      };
    });
    const ceiling = setTimeout(() => recorder.state === 'recording' && recorder.stop(), MAX_SECONDS * 1000);
    recorder.start();

    return {
      stop: async () => {
        clearTimeout(ceiling);
        if (recorder.state === 'recording') recorder.stop();
        return monoAt16k(await (await stopped).arrayBuffer());
      },
    };
  }
}

/** A recording, whatever the browser encoded it as, redrawn as mono samples at the rate the shop listens at. */
async function monoAt16k(encoded: ArrayBuffer): Promise<Float32Array> {
  if (encoded.byteLength === 0) return new Float32Array();
  const decoding = new AudioContext();
  try {
    const decoded = await decoding.decodeAudioData(encoded);
    const frames = Math.ceil(decoded.duration * SAMPLE_RATE);
    const resampling = new OfflineAudioContext(1, Math.max(frames, 1), SAMPLE_RATE);
    const source = resampling.createBufferSource();
    source.buffer = decoded;
    source.connect(resampling.destination);
    source.start();
    return (await resampling.startRendering()).getChannelData(0);
  } finally {
    await decoding.close();
  }
}

export const MICROPHONE = new InjectionToken<Microphone>('Microphone', { factory: () => new BrowserMicrophone() });
