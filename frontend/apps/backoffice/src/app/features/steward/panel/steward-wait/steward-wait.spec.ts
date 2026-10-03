import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { StewardWait } from './steward-wait';

/** The wait counts the seconds, and says it can last once it is past the hosted models' time. */
describe('the steward wait', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTenderoI18n()],
    });
  });

  afterEach(() => vi.useRealTimers());

  const shown = () => {
    const fixture = TestBed.createComponent(StewardWait);
    fixture.detectChanges();
    const words = {
      steward: { working: 'Working', elapsed: '{{seconds}} s', slow: 'A local model can take a minute or more.' },
    };
    for (const request of TestBed.inject(HttpTestingController).match((req) => req.url.includes('/i18n/')))
      request.flush(words);
    return {
      fixture,
      text: async (seconds: number) => {
        await vi.advanceTimersByTimeAsync(seconds * 1000);
        fixture.detectChanges();
        return (fixture.nativeElement as HTMLElement).textContent ?? '';
      },
    };
  };

  it('counts the seconds from zero', async () => {
    const { text } = shown();

    expect(await text(0)).toContain('0 s');
    expect(await text(7)).toContain('7 s');
  });

  it('says a local model can take longer only once the wait is long', async () => {
    const { text } = shown();

    expect(await text(14)).not.toContain('local model');
    expect(await text(1)).toContain('local model');
  });
});
