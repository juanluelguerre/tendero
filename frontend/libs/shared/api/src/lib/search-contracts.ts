/**
 * Espejo en TypeScript del contrato de `GET /api/search`.
 *
 * Escrito a mano HOY, generado desde OpenAPI en cuanto la API publique el
 * documento: mientras sea manual, esto y `Tendero.Search.Contracts` pueden
 * divergir en silencio y el fallo aparece en runtime. Ver docs/adr/0010.
 *
 * Fuente: src/Search/Contracts.cs
 */

export interface SearchHit {
  productId: string;
  name: string;
  slug: string;
  brand: string | null;
  category: string | null;
  priceAmount: number;
  priceCurrency: string;
  /** Clave de la imagen en el almacén. La URL se compone en el cliente:
   *  `/api/images/{imageId}`. Ver docs/adr/0011-product-images.md. */
  imageId: string | null;
  score: number;
}

export interface SearchResultPage {
  hits: SearchHit[];
  total: number;
  page: number;
  pageSize: number;
  tookMs: number;
}
