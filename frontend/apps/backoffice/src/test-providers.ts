import { NOTHING_LISTENS, SPEECH_TO_TEXT } from '@tendero/shared-composer/testing';
import { provideReadableResources } from '@tendero/shared-util';

// jsdom lays nothing out and has no media queries: with none stubbed, a window matches
// no width at all, as a phone does. A spec that is about a width stubs its own.
window.matchMedia ??= ((query: string) => ({
  matches: false,
  media: query,
  addEventListener: () => undefined,
  removeEventListener: () => undefined,
})) as unknown as typeof window.matchMedia;

/** What every unit test gets, as the app does (app.config.ts): a failed resource reads as no value. */
export default [provideReadableResources(), { provide: SPEECH_TO_TEXT, useValue: NOTHING_LISTENS }];
