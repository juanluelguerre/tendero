import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SPEECH_TO_TEXT, type Heard, type SpeechToText } from '../voice/speech-to-text';
import type { MicrophonePlace } from '../voice/microphone/microphone.store';
import { ComposerBox, type AttachKind } from './composer-box';

/** An adapter the test drives: whether it can listen, and what listening comes to. */
class FakeSpeech implements SpeechToText {
  readonly name = 'shop' as const;
  canListen = true;
  private finish: (heard: Heard) => void = () => undefined;

  async available(): Promise<boolean> {
    return this.canListen;
  }

  listen() {
    return { stop: () => undefined, heard: new Promise<Heard>((resolve) => (this.finish = resolve)) };
  }

  hears(heard: Heard): void {
    this.finish(heard);
  }
}

@Component({
  imports: [ComposerBox],
  template: `<lib-composer-box
    inputId="question"
    label="Pregunta"
    placeholder="Pregúntale a Tino…"
    sendLabel="Enviar"
    stopLabel="Parar"
    [voice]="voice()"
    [attach]="attach()"
    [busy]="busy()"
    (asked)="asked.push($event)"
    (stopped)="stops = stops + 1"
    (attached)="attached.push($event)"
  />`,
})
class Host {
  readonly voice = signal<MicrophonePlace | null>('tino');
  readonly attach = signal<readonly AttachKind[]>([]);
  readonly busy = signal(false);
  readonly asked: string[] = [];
  readonly attached: AttachKind[] = [];
  stops = 0;
}

/**
 * The box Tino and Steward share (the sketches of 2026-10-17, 03 and 04): one
 * border round the field and its buttons; the microphone while it is empty and
 * the arrow once there is something to send; a square while a turn is out; and
 * the ways to add something by one rule — none, one icon, or a "+" with a menu.
 */
describe('the composer box', () => {
  let speech: FakeSpeech;

  beforeEach(() => {
    speech = new FakeSpeech();
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: {
            es: {
              voice: { start: 'Hablar', stop: 'Dejar de escuchar', listening: 'Escuchando…', landed: 'Revísalo' },
              composer: { attach: { photo: 'Foto o galería', camera: 'Cámara', file: 'Archivo', more: 'Añadir' } },
            },
          },
          translocoConfig: { availableLangs: ['es'], defaultLang: 'es' },
        }),
      ],
      providers: [
        { provide: SPEECH_TO_TEXT, useFactory: () => speech },
        { provide: CultureStore, useValue: { active: signal('es') } },
        { provide: Telemetry, useValue: { trackEvent: vi.fn() } },
      ],
    });
  });

  async function drawn(setup: (host: Host) => void = () => undefined) {
    const fixture = TestBed.createComponent(Host);
    setup(fixture.componentInstance);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const box = element.querySelector<HTMLTextAreaElement>('textarea')!;
    return {
      fixture,
      host: fixture.componentInstance,
      element,
      box,
      microphone: () => element.querySelector<HTMLButtonElement>('[data-microphone]'),
      send: () => element.querySelector<HTMLButtonElement>('[data-send]'),
      stop: () => element.querySelector<HTMLButtonElement>('[data-stop]'),
      async type(text: string) {
        box.value = text;
        box.dispatchEvent(new Event('input'));
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
      },
    };
  }

  it('offers the microphone, and no arrow, while the box is empty and an adapter can listen', async () => {
    const view = await drawn();

    expect(view.microphone()).not.toBeNull();
    expect(view.send()).toBeNull();
  });

  it('gives the microphone’s place to the arrow once there is something to send, and takes it back when the box is empty', async () => {
    const view = await drawn();

    await view.type('zapatillas');
    expect([view.microphone(), view.send()?.disabled]).toEqual([null, false]);

    view.send()!.click();
    await view.fixture.whenStable();
    view.fixture.detectChanges();

    expect(view.host.asked).toEqual(['zapatillas']);
    expect(view.microphone()).not.toBeNull();
  });

  it('writes what it heard into the box, unsent, and then offers the arrow', async () => {
    const view = await drawn();

    view.microphone()!.click();
    speech.hears({ kind: 'words', words: 'una sartén' });
    await view.fixture.whenStable();
    view.fixture.detectChanges();

    expect(view.box.value).toBe('una sartén');
    expect(view.host.asked).toEqual([]);
    expect(view.send()?.disabled).toBe(false);
  });

  it('with no voice, or no adapter that can listen, is a box and an arrow as before', async () => {
    const noVoice = await drawn((host) => host.voice.set(null));
    expect([noVoice.microphone(), noVoice.send()?.disabled]).toEqual([null, true]);

    TestBed.resetTestingModule();
    speech = new FakeSpeech();
    speech.canListen = false;
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { es: {} },
          translocoConfig: { availableLangs: ['es'], defaultLang: 'es' },
        }),
      ],
      providers: [
        { provide: SPEECH_TO_TEXT, useFactory: () => speech },
        { provide: CultureStore, useValue: { active: signal('es') } },
        { provide: Telemetry, useValue: { trackEvent: vi.fn() } },
      ],
    });
    const deaf = await drawn();
    expect([deaf.microphone(), deaf.send()?.disabled]).toEqual([null, true]);
  });

  it('while a turn is out, offers the square that stops it, and neither the arrow nor the microphone', async () => {
    const view = await drawn((host) => host.busy.set(true));

    expect([view.send(), view.microphone()]).toEqual([null, null]);
    view.stop()!.click();
    expect(view.host.stops).toBe(1);
  });

  it('draws nothing to add when it is asked for nothing, which is where both surfaces are today', async () => {
    const view = await drawn();

    expect(view.element.querySelectorAll('[data-attach], [data-attach-more]')).toHaveLength(0);
  });

  it('draws one way to add something as its own icon, never behind a "+"', async () => {
    const view = await drawn((host) => host.attach.set(['photo']));

    const photo = view.element.querySelector<HTMLButtonElement>('[data-attach="photo"]')!;
    expect(photo.getAttribute('aria-label')).toBe('Foto o galería');
    expect(view.element.querySelector('[data-attach-more]')).toBeNull();
    photo.click();
    expect(view.host.attached).toEqual(['photo']);
  });

  it('puts two or more ways behind a "+" that opens them, labelled, and closes once one is chosen', async () => {
    const view = await drawn((host) => host.attach.set(['photo', 'camera']));

    const more = view.element.querySelector<HTMLButtonElement>('[data-attach-more]')!;
    expect(view.element.querySelectorAll('[role="menuitem"]')).toHaveLength(0);
    more.click();
    view.fixture.detectChanges();

    const items = [...view.element.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')];
    expect(items.map((item) => item.textContent?.trim())).toEqual(['Foto o galería', 'Cámara']);
    expect(more.getAttribute('aria-expanded')).toBe('true');

    items[1].click();
    view.fixture.detectChanges();
    expect(view.host.attached).toEqual(['camera']);
    expect(view.element.querySelectorAll('[role="menuitem"]')).toHaveLength(0);
  });
});
