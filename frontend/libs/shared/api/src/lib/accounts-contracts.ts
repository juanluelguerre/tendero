/**
 * Accounts contracts, derived from the OpenAPI document: the agent registry
 * (P11-2) and what the shop refused agents (P11-6, drawn by the dashboard in P11-7).
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

/** An agent the shop knows: who operates it, how far it is trusted, and whether it is suspended. */
export type AgentView = Schemas['AgentView'];
export type RegisterAgentRequest = Schemas['RegisterAgentRequest'];
export type AgentChangeResponse = Schemas['AgentChangeResponse'];

export type AgentDenialsSummary = Schemas['AgentDenialsSummary'];
export type RecentDenial = Schemas['RecentDenial'];
