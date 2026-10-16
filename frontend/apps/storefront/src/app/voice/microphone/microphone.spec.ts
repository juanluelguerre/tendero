import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SPEECH_TO_TEXT, type Heard, type SpeechToText } from '../speech-to-text';
import { Microphone } from './microphone';

/** An adapter the test drives: whether it can listen, and what listening comes to. */
class FakeSpeech implements SpeechToText {
  readonly name = 'shop' as const;
  canListen = true;
  stops = 0;
  private finish: (heard: Heard) => void = () => undefined;

  async available(): Promise<boolean> {
    return this.canListen;
  }

  listen() {
    return {
      stop: () => this.stops++,
      heard: new Promise<Heard>((resolve) => (this.finish = resolve)),
    };
  }

  hears(heard: Heard): void {
    this.finish(heard);
  }
}

@Component({
  imports: [Microphone],
  template: `<storefront-microphone #mic="microphone" place="tino" [offered]="offered()" (heard)="heard.push($event)" />
    <p data-status>{{ mic.status() }}</p>`,
})
class Host {
  readonly heard: string[] = [];
  readonly offered = signal(true);
}

/**
 * The microphone (the voice spec, V-2 and V-3; the sketch of 2026-10-16): drawn
 * only when the adapter can listen in the page's language, pressed to listen and
 * again to finish, and what was heard handed to the box — never sent.
 */
describe('the microphone', () => {
  let speech: FakeSpeech;
  const telemetry = { trackEvent: vi.fn() };

  beforeEach(() => {
    speech = new FakeSpeech();
    telemetry.trackEvent.mockClear();
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { es: { voice: { start: 'Hablar', stop: 'Dejar de escuchar' } } },
          translocoConfig: { availableLangs: ['es'], defaultLang: 'es' },
        }),
      ],
      providers: [
        { provide: SPEECH_TO_TEXT, useValue: speech },
        { provide: CultureStore, useValue: { active: signal('es') } },
        { provide: Telemetry, useValue: telemetry },
      ],
    });
  });

  async function drawn() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    return {
      fixture,
      button: () => element.querySelector<HTMLButtonElement>('storefront-microphone button'),
      status: () => element.querySelector('[data-status]')?.textContent?.trim(),
      settle: async () => {
        await fixture.whenStable();
        fixture.detectChanges();
      },
    };
  }

  /** The header's box on a desktop: a keyboard is there already (the sketch, board 2). */
  it('is not drawn where its place turns it off, even when something can listen', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.offered.set(false);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('storefront-microphone button')).toBeNull();
  });

  it('is not drawn where nothing can listen (invariant 8)', async () => {
    speech.canListen = false;
    const { button } = await drawn();

    expect(button()).toBeNull();
  });

  it('listens when pressed, finishes when pressed again, and hands the words over unsent', async () => {
    const { fixture, button, status, settle } = await drawn();
    expect([button()?.getAttribute('aria-label'), button()?.getAttribute('aria-pressed')]).toEqual(['Hablar', 'false']);

    button()!.click();
    await settle();
    const listening = [button()?.getAttribute('aria-label'), button()?.getAttribute('aria-pressed'), status()];
    button()!.click();
    speech.hears({ kind: 'words', words: 'zapatillas para correr' });
    await settle();

    expect(listening).toEqual(['Dejar de escuchar', 'true', 'listening']);
    expect([speech.stops, fixture.componentInstance.heard, status(), button()?.getAttribute('aria-pressed')]).toEqual([
      1,
      ['zapatillas para correr'],
      'landed',
      'false',
    ]);
  });

  it('says it did not understand when nothing was heard, and hands nothing over', async () => {
    const { fixture, button, status, settle } = await drawn();

    button()!.click();
    await settle();
    speech.hears({ kind: 'nothing' });
    await settle();

    expect([fixture.componentInstance.heard, status()]).toEqual([[], 'failed']);
  });

  it('says the same when listening failed', async () => {
    const { fixture, button, status, settle } = await drawn();

    button()!.click();
    await settle();
    speech.hears({ kind: 'failed' });
    await settle();

    expect([fixture.componentInstance.heard, status()]).toEqual([[], 'failed']);
  });

  /** The sketch's listening state: Esc lets the shopper change their mind. */
  it('lets Escape cancel: it stops listening and hands nothing over', async () => {
    const { fixture, button, status, settle } = await drawn();

    button()!.click();
    await settle();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    speech.hears({ kind: 'words', words: 'no lo quería' });
    await settle();

    expect([speech.stops, fixture.componentInstance.heard, status()]).toEqual([1, [], '']);
  });

  // ---------- the evidence of use (ADR 0036; the tracking plan's "Speak to the shop") ----------

  const microphoneOf = (fixture: { debugElement: import('@angular/core').DebugElement }) =>
    fixture.debugElement.query((node) => node.componentInstance instanceof Microphone).componentInstance as Microphone;

  /** One event each time listening ends, with what it came to, and never the words. */
  it.each([
    [{ kind: 'words', words: 'zapatillas' } as Heard, 'words'],
    [{ kind: 'nothing' } as Heard, 'nothing'],
    [{ kind: 'failed' } as Heard, 'failed'],
  ])('says listening ended in %o as %s', async (heard, outcome) => {
    const { button, settle } = await drawn();

    button()!.click();
    await settle();
    speech.hears(heard);
    await settle();

    expect(telemetry.trackEvent.mock.calls).toEqual([
      ['voice_input_heard', { where: 'tino', adapter: 'shop', outcome }],
    ]);
  });

  it('says a cancel as cancelled', async () => {
    const { button, settle } = await drawn();

    button()!.click();
    await settle();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    speech.hears({ kind: 'words', words: 'no' });
    await settle();

    expect(telemetry.trackEvent.mock.calls).toEqual([
      ['voice_input_heard', { where: 'tino', adapter: 'shop', outcome: 'cancelled' }],
    ]);
  });

  /** What became of the words: sent as heard, sent changed, or never sent — the measurement repeated in the wild. */
  it.each([
    ['zapatillas para correr', 'as_heard'],
    ['  zapatillas para correr  ', 'as_heard'],
    ['zapatillas para correr de montaña', 'edited'],
  ])('says words sent as %j were used %s', async (sent, how) => {
    const { fixture, button, settle } = await drawn();
    button()!.click();
    await settle();
    speech.hears({ kind: 'words', words: 'zapatillas para correr' });
    await settle();
    telemetry.trackEvent.mockClear();

    microphoneOf(fixture).sent(sent);
    microphoneOf(fixture).sent('another question');

    expect(telemetry.trackEvent.mock.calls).toEqual([
      ['voice_transcript_used', { where: 'tino', adapter: 'shop', how }],
    ]);
  });

  it('says the words were discarded when the shopper speaks again, or leaves, without sending them', async () => {
    const { fixture, button, settle } = await drawn();
    for (const words of ['primera', 'segunda']) {
      button()!.click();
      await settle();
      speech.hears({ kind: 'words', words });
      await settle();
    }
    fixture.destroy();

    const used = telemetry.trackEvent.mock.calls.filter(([name]) => name === 'voice_transcript_used');
    expect(used).toEqual([
      ['voice_transcript_used', { where: 'tino', adapter: 'shop', how: 'discarded' }],
      ['voice_transcript_used', { where: 'tino', adapter: 'shop', how: 'discarded' }],
    ]);
  });
});
