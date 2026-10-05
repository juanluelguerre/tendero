/**
 * Tino's contracts, derived from the OpenAPI document (phase 12b).
 *
 * Same arrangement as the other contexts: `schema.ts` is generated and these
 * aliases are the stable surface the storefront consumes.
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

/** One turn: the conversation so far (the server keeps none) and what the shopper is looking at. */
export type TinoTurnRequest = Schemas['TinoTurnRequest'];
export type TinoContext = Schemas['TinoContext'];

/**
 * What a turn answered. Never prose of the model's own: products with reasons
 * that each cite a fact, sentences about the shop or about an order, a question
 * back, or what the shop does not hold — and, when Tino did not answer, why,
 * with the shopper's words to search for.
 */
export type TinoAnswer = Schemas['TinoAnswer'];
export type TinoKind = Schemas['TinoKind'];
export type TinoCause = Schemas['TinoCause'];
export type AnsweredProduct = Schemas['AnsweredProduct'];
export type AnsweredOrder = Schemas['AnsweredOrder'];
export type TinoNoData = Schemas['NoData'];

/** A sentence and the fact it rests on: the words are the model's, the source and the evidence the shop's. */
export type TinoReason = Schemas['Reason'];
export type FactSource = Schemas['FactSource'];

/** Whether Tino can be asked: the door says so before anybody tries. */
export type TinoAvailability = Schemas['TinoAvailability'];
