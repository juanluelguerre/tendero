/**
 * Steward's contracts, derived from the OpenAPI document (phase 12, ADR 0052).
 *
 * Same arrangement as the other contexts: `schema.ts` is generated and these
 * aliases are the stable surface the apps consume.
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

/** One turn: the conversation so far (the server keeps none) and the screen it was asked on. */
export type StewardTurnRequest = Schemas['StewardTurnRequest'];
export type TurnMessage = Schemas['TurnMessage'];
export type ScreenContext = Schemas['ScreenContext'];

/**
 * What a turn answered: its text, every read it made, the proposals it stored
 * (by id: the card asks for them), its cost, and the status that says when no
 * model answered, the conversation spent its budget, or it outgrew every window.
 */
export type StewardAnswer = Schemas['StewardAnswer'];
export type StewardStatus = Schemas['StewardStatus'];
export type ToolUse = Schemas['ToolUse'];

/** What a read's rows are drawn as: the server builds it, the panel formats and translates it. */
export type ResultTable = Schemas['ResultTable'];
export type ResultColumn = Schemas['ResultColumn'];
export type ResultCell = Schemas['ResultCell'];
export type CellKind = Schemas['CellKind'];
export type TurnCost = Schemas['TurnCost'];

/** A proposal as a person reads it, in their language, with what became of it. */
export type ProposalView = Schemas['ProposalView'];
export type ProposalChangeView = Schemas['ProposalChangeView'];
export type ProposalStatus = Schemas['ProposalStatus'];
export type ProposalsPage = Schemas['ProposalsPage'];
export type ProposalFilter = Schemas['ProposalFilter'];

/** One row per proposal decided: applied, failed with its reason, stale… */
export type ProposalDecisionView = Schemas['ProposalDecisionView'];
export type ProposalDecisionsResponse = Schemas['ProposalDecisionsResponse'];
export type DecisionOutcome = Schemas['DecisionOutcome'];

/** What a batch of alt texts came to: the proposals stored, and the pictures skipped or failed. */
export type ProposeAltTextResult = Schemas['ProposeAltTextResult'];
export type AltTextStatus = Schemas['AltTextStatus'];
