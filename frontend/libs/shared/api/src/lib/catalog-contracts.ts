/**
 * Catalogue contracts, **derived** from the OpenAPI document.
 *
 * They used to be a hand-written mirror of `ElGuerre.Tendero.Catalog.Features.*`,
 * under a note admitting they could diverge in silence until the document
 * existed. It does now: `docs/openapi/tendero.json`, plus a contract test
 * (`tests/Api.Tests`) that fails if the API stops serving exactly that document.
 *
 * The aliases stay here on purpose. `schema.ts` is generated and its shape
 * (`components['schemas'][...]`) is noise at every point of use; these names are
 * the stable surface the apps consume. If a type disappears from the document,
 * the line that aliases it stops compiling — which is exactly the warning that
 * did not exist before.
 *
 * Regenerate with `npm run generate:api-types` from `frontend/`.
 */
import type { components } from './generated/schema';

type Schemas = components['schemas'];

export type ProductSummary = Schemas['ProductSummary'];
export type ProductListPage = Schemas['ListProductsResult'];
export type PublishProductResponse = Schemas['PublishProductResponse'];
export type ImportResult = Schemas['ImportProductsResult'];
export type DefineVariantsResponse = Schemas['DefineVariantsResponse'];

/**
 * The product detail page, keyed on the public code (ADR 0026).
 *
 * `Axes` carries every option the CATALOGUE defines and `Variants` only the
 * combinations that exist, and the picker needs both: an option with no variant
 * has to render disabled rather than vanish, and it cannot render at all if the
 * response never mentions it.
 */
export type ProductDetail = Schemas['ProductDetail'];
export type VariantView = Schemas['VariantView'];
export type VariantAxisView = Schemas['VariantAxisView'];
export type VariantOptionView = Schemas['VariantOptionView'];
export type ProductAttributeView = Schemas['ProductAttributeView'];
export type ProductImageView = Schemas['ProductImageView'];
export type CategoryStep = Schemas['CategoryStep'];
export type AlternateSlug = Schemas['AlternateSlug'];

/**
 * What a SKU is called. It exists because `Inventory` cannot say — stock is
 * keyed on SKU precisely so it need not know a catalogue exists — so the stock
 * screen asks both and joins them.
 */
export type SkuDescription = Schemas['SkuDescriptionView'];
export type SkuDescriptionList = Schemas['DescribeSkusResult'];

/**
 * The audit log. It lives in the catalogue contracts file only because
 * `Accounts` has no file of its own yet; the schema belongs to no context — the
 * dispatcher writes it and three later features read it.
 */
export type AuditEntryView = Schemas['AuditEntryView'];
export type AuditLog = Schemas['ListAuditEntriesResult'];
export type AuditOutcome = 'allowed' | 'denied' | 'failed';

/**
 * The labels arrive UNRESOLVED — the whole culture dictionary — and that is
 * deliberate: this screen exists to see whether a translation is missing, and
 * text already resolved would hide the very fact people come to look at.
 */
export type AttributeDefinitionView = Schemas['AttributeDefinitionView'];
export type AttributeDefinitionList = Schemas['ListAttributeDefinitionsResult'];

/**
 * Publish's 409. It was an anonymous object on the endpoint, so it had no schema
 * and was invisible from the client: nothing said publishing could be refused
 * for being archived, nor in what shape.
 */
export type PublishProductConflict = Schemas['PublishProductConflict'];

/**
 * These two do not come from the document, and that is not an oversight: they
 * travel as `string` because the server serialises them in camelCase by hand —
 * see the comment on `PublishProductResponse` in the slice, where the enum came
 * out as a number and broke the moment anybody reordered the members.
 *
 * Narrowing them here is the client's decision, not the contract's. They are
 * documented together so they read as what they are: a promise the document
 * cannot enforce yet.
 */
export type ProductStatus = 'draft' | 'active' | 'archived';
export type PublishOutcome = 'published' | 'alreadyActive' | 'archived' | 'notFound';
