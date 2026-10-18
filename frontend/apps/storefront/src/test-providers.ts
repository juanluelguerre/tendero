import { provideReadableResources } from '@tendero/shared-util';
import { NOTHING_LISTENS, SPEECH_TO_TEXT } from '@tendero/shared-composer/testing';

/**
 * What every unit test gets, as the app does (app.config.ts): a failed resource
 * reads as no value, and a shop where nothing listens — no microphone, the boxes
 * as they always were (invariant 8). A spec about voice provides its own adapter.
 */
export default [provideReadableResources(), { provide: SPEECH_TO_TEXT, useValue: NOTHING_LISTENS }];
