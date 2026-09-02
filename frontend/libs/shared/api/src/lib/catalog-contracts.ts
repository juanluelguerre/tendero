/**
 * Contratos de catálogo, **derivados** del documento de OpenAPI.
 *
 * Antes eran un espejo escrito a mano de `ElGuerre.Tendero.Catalog.Features.*`,
 * con una nota admitiendo que podían divergir en silencio hasta que apareciese
 * el documento. Ya existe: `docs/openapi/tendero.json`, y un test de contrato
 * (`tests/Api.Tests`) falla si la API deja de servir exactamente ese documento.
 *
 * Los alias siguen aquí a propósito. `schema.ts` es generado y su forma
 * (`components['schemas'][...]`) es ruido en cada punto de uso; estos nombres
 * son la superficie estable que consumen las apps. Si un tipo desaparece del
 * documento, la línea que lo aliasa deja de compilar — que es exactamente el
 * aviso que antes no existía.
 *
 * Regenerar: `npm run generate:api-types` desde `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

export type ProductSummary = Schemas['ProductSummary'];
export type ProductListPage = Schemas['ListProductsResult'];
export type PublishProductResponse = Schemas['PublishProductResponse'];
export type ImportResult = Schemas['ImportProductsResult'];

/**
 * El 409 de publicar. Era un objeto anónimo en el endpoint, así que no tenía
 * esquema y desde el cliente era invisible: nada decía que publicar pudiera
 * rechazarse por archivado, ni con qué forma.
 */
export type PublishProductConflict = Schemas['PublishProductConflict'];

/**
 * Estos dos no salen del documento y no es un descuido: viajan como `string`
 * porque el servidor los serializa en camelCase a mano — ver el comentario de
 * `PublishProductResponse` en el slice, donde el enum salía como número y se
 * rompía en cuanto alguien reordenase los miembros.
 *
 * Estrecharlos aquí es una decisión del cliente, no del contrato. Se documentan
 * juntos para que se vean como lo que son: una promesa que el documento todavía
 * no puede hacer cumplir.
 */
export type ProductStatus = 'draft' | 'active' | 'archived';
export type PublishOutcome = 'published' | 'alreadyActive' | 'archived' | 'notFound';
