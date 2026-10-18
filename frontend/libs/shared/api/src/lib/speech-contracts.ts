/**
 * Speaking to the shop, derived from the OpenAPI document (phase 14).
 *
 * Same arrangement as the other contexts: `schema.ts` is generated and these
 * aliases are the stable surface the storefront consumes.
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

/** The words of a spoken question, for the box they are written into unsent. */
export type TranscriptionView = Schemas['TranscriptionView'];
/** Whether a microphone may be drawn: the shop has an engine. */
export type SpeechAvailability = Schemas['SpeechAvailability'];
