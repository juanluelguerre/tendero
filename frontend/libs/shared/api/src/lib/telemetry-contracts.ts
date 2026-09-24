/**
 * The contract for `GET /api/telemetry/config`, **derived** from the OpenAPI
 * document: which browser telemetry adapter the page loads (ADR 0036).
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components } from './generated/schema';

export type TelemetryConfiguration = components['schemas']['TelemetryConfiguration'];
