/**
 * Sales channels contracts, derived from the OpenAPI document (phase 16, S3).
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

/**
 * An order a channel took and charged for that Tendero could not receive (P16-29):
 * why as a code to compose a sentence from, what it concerns, and the order's page
 * in the channel's own admin.
 */
export type HeldOrder = Schemas['HeldOrderView'];

/** Every held order, oldest first. */
export type HeldOrders = Schemas['HeldOrdersView'];

export type HeldOrderReason = Schemas['HeldOrderReason'];
