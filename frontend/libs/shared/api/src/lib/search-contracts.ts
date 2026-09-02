/**
 * Contrato de `GET /api/search`, **derivado** del documento de OpenAPI.
 *
 * Igual que los de catálogo: era un espejo a mano de `Search.Contracts` y ahora
 * sale de `docs/openapi/tendero.json`, con un test que impide que el documento
 * y la API se separen.
 *
 * `imageId` sigue siendo la clave en el almacén, no una URL: la compone el
 * cliente como `/api/images/{imageId}` para que cambiar de CDN no sea un UPDATE
 * sobre millones de filas (ADR 0011).
 *
 * Regenerar: `npm run generate:api-types` desde `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

export type SearchHit = Schemas['SearchHit'];
export type SearchResultPage = Schemas['SearchResultPage'];
