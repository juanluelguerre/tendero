import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type {
  AttributeDefinitionList,
  AuditLog,
  SkuDescriptionList,
  DefineVariantsResponse,
  ImportResult,
  ProductForReview,
  ProductListPage,
  ProductStatus,
  PublishProductResponse,
  WithdrawProductResponse,
} from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * The only place in the backoffice that knows what the catalogue endpoints are
 * called. The review queue reads from HERE and not from the search index: what
 * is under review is precisely what is not indexed yet, because only Active is
 * indexed.
 */
@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /**
   * What a set of SKUs is called.
   *
   * It is here and not in `InventoryService` for the reason that service
   * already states: one service per bounded context, because a service reaching
   * into two is how a frontend starts pretending they are one.
   *
   * And the reason the stock screen needs two calls at all is the boundary
   * itself. `Inventory` may reference the SharedKernel and nothing else — stock
   * exists without a catalogue — so `/api/inventory/stock` structurally cannot
   * name a product. The catalogue answers that separately and the SCREEN joins
   * them, which is what a screen is for.
   */
  describeSkus(skus: readonly string[], culture: string): Observable<SkuDescriptionList> {
    const params = skus.reduce(
      (query, sku) => query.append('sku', sku),
      new HttpParams().set('culture', culture),
    );

    return this.http.get<SkuDescriptionList>(`${this.baseUrl}/api/catalog/skus`, { params });
  }

  /** The SKUs of every product whose name or code contains the text: the catalogue's half of a stock search (R-7.1). */
  skusMatching(text: string, culture: string): Observable<SkuDescriptionList> {
    return this.http.get<SkuDescriptionList>(`${this.baseUrl}/api/catalog/skus`, { params: { q: text, culture } });
  }

  /**
   * The audit log, newest first.
   *
   * It is on the catalogue service only because `Accounts` has no service of
   * its own yet, and the log belongs to no context: the dispatcher writes it and
   * three later features read it. The day the agent activity panel arrives
   * (phase 11), both move to an `accounts.service.ts` together.
   */
  audit(outcome: string | null, page = 1, pageSize = 20): Observable<AuditLog> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (outcome) params = params.set('outcome', outcome);

    return this.http.get<AuditLog>(`${this.baseUrl}/api/audit`, { params });
  }

  list(
    status: ProductStatus | null,
    culture: string,
    page = 1,
    pageSize = 20,
    text = '',
  ): Observable<ProductListPage> {
    let params = new HttpParams()
      .set('culture', culture)
      .set('page', page)
      .set('pageSize', pageSize);

    // No parameter at all rather than an empty one: the API rejects an unknown
    // status, and "" is one.
    if (status) params = params.set('status', status);
    // A name in any culture, or a code (R-4.6).
    if (text.trim()) params = params.set('q', text.trim());

    return this.http.get<ProductListPage>(`${this.baseUrl}/api/catalog/products`, { params });
  }

  publish(productId: string): Observable<PublishProductResponse> {
    return this.http.post<PublishProductResponse>(
      `${this.baseUrl}/api/catalog/products/${productId}/publish`,
      null,
    );
  }

  /** Back to the review queue: the inverse of publishing (ADR 0012's addendum). */
  withdraw(productId: string): Observable<WithdrawProductResponse> {
    return this.http.post<WithdrawProductResponse>(`${this.baseUrl}/api/catalog/products/${productId}/withdraw`, {});
  }

  /** Every culture as stored, whatever the status: what a person approves (R-4.3). */
  review(code: string, culture: string): Observable<ProductForReview> {
    return this.http.get<ProductForReview>(`${this.baseUrl}/api/catalog/products/${encodeURIComponent(code)}/review`, {
      params: new HttpParams().set('culture', culture),
    });
  }

  import(source = 'seed'): Observable<ImportResult> {
    return this.http.post<ImportResult>(`${this.baseUrl}/api/catalog/import`, { source });
  }

  /**
   * Generates the variant matrix: the cartesian product of the axes. It is sent
   * whole because that is what a shopkeeper expects on declaring "colours ×
   * sizes", and retiring the combinations that do not exist afterwards costs
   * less than creating them one by one.
   */
  defineVariants(
    productId: string,
    axes: { code: string; options: string[] }[],
  ): Observable<DefineVariantsResponse> {
    return this.http.post<DefineVariantsResponse>(
      `${this.baseUrl}/api/catalog/products/${productId}/variants`,
      { axes, skuPrefix: null },
    );
  }

  /** The attribute definitions, with their label in every culture. */
  attributeDefinitions(): Observable<AttributeDefinitionList> {
    return this.http.get<AttributeDefinitionList>(`${this.baseUrl}/api/catalog/attributes`);
  }
}
