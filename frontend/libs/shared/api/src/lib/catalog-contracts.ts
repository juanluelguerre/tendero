/**
 * Espejo en TypeScript de los contratos de catálogo.
 *
 * Escrito a mano HOY, generado desde OpenAPI en cuanto la API publique el
 * documento: mientras sea manual, esto y `Tendero.Catalog.Features.*` pueden
 * divergir en silencio y el fallo aparece en runtime. Ver docs/adr/0010.
 *
 * Fuente: src/Catalog/Features/ListProducts/ListProducts.cs
 *         src/Catalog/Features/PublishProduct/PublishProduct.cs
 */

export type ProductStatus = 'draft' | 'active' | 'archived';

export interface ProductSummary {
  productId: string;
  name: string;
  slug: string;
  brand: string | null;
  category: string | null;
  priceAmount: number;
  priceCurrency: string;
  /** Clave en el almacén; la URL se compone en el cliente: `/api/images/{imageId}`. */
  imageId: string | null;
  status: ProductStatus;
  /**
   * Idiomas que le faltan al producto. Lo calcula el servidor porque
   * LocalizedText resuelve en cadena (cultura → en → primera): sin este dato la
   * ficha se ve completa en inglés y nadie se entera de que no hay español.
   */
  missingCultures: string[];
  updatedAt: string;
}

export interface ProductListPage {
  items: ProductSummary[];
  total: number;
  page: number;
  pageSize: number;
}

/** Resultado de publicar. `alreadyActive` no es un error: publicar es idempotente. */
export type PublishOutcome = 'published' | 'alreadyActive' | 'archived' | 'notFound';

export interface PublishProductResponse {
  productId: string;
  outcome: PublishOutcome;
  status: ProductStatus;
}

/**
 * Resultado de una importacion. Vivia suelto en el servicio del backoffice
 * mientras el resto de contratos estaban aqui: un DTO de la API es un contrato
 * de la API, venga de donde venga.
 *
 * Fuente: src/Catalog/Features/ImportProducts/ImportProducts.cs
 */
export interface ImportResult {
  created: number;
  updated: number;
  failed: number;
  elapsedSeconds: number;
}
