/**
 * Campaign contracts (R-8, ADR 0044), derived from the OpenAPI document.
 *
 * Two audiences, two types, as the offers and the promotions are:
 * `CampaignView` is what the shop and an agent read from
 * `GET /api/pricing/campaigns` — the scheduled campaigns near now, the leading
 * one first, in one culture; `ManagedCampaignView` is the backoffice's, from
 * `/api/pricing/campaign-calendar`, with both languages, the stored status and
 * the window's phase.
 *
 * The clock is the client's to compute from `startsAt`/`endsAt`: a cached
 * answer must never count down wrong. The order of the list is the server's
 * rule; the home draws the first campaign whose scope is `Shop`.
 *
 * `imageId` is the key in the store, composed as `/api/images/{imageId}` like a
 * product's (ADR 0011).
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

export type CampaignView = Schemas['CampaignView'];
export type CampaignList = Schemas['ListCampaignsResult'];

/** From the document (ADR 0043). A client keeps a branch for a value it does not know yet. */
export type CampaignKind = Schemas['CampaignKind'];
export type CampaignScope = Schemas['CampaignScope'];
export type CampaignPhase = Schemas['CampaignPhase'];
export type CampaignStatus = Schemas['CampaignStatus'];

export type ManagedCampaignView = Schemas['ManagedCampaignView'];
export type BilingualText = Schemas['BilingualText'];
export type CampaignImageView = Schemas['CampaignImageView'];

export type CreateCampaignRequest = Schemas['CreateCampaignRequest'];
export type CampaignDetailsRequest = Schemas['CampaignDetailsRequest'];
export type CampaignImageRequest = Schemas['CampaignImageRequest'];
export type CampaignChangeResponse = Schemas['CampaignChangeResponse'];
export type CampaignChange = Schemas['CampaignChange'];
export type CampaignImageUploaded = Schemas['CampaignImageUploaded'];
