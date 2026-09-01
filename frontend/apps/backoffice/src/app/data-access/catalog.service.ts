import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type {
  ImportResult,
  ProductListPage,
  ProductStatus,
  PublishProductResponse,
} from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * El unico sitio del backoffice que sabe como se llaman los endpoints de
 * catalogo. La cola de revision lee de AQUI y no del indice de busqueda: lo que
 * se revisa es justo lo que todavia no esta indexado, porque solo lo Active se
 * indexa.
 */
@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  list(
    status: ProductStatus | null,
    culture: string,
    page = 1,
    pageSize = 20,
  ): Observable<ProductListPage> {
    let params = new HttpParams()
      .set('culture', culture)
      .set('page', page)
      .set('pageSize', pageSize);

    // Sin el parametro, no con el vacio: la API rechaza un estado desconocido,
    // y "" lo es.
    if (status) params = params.set('status', status);

    return this.http.get<ProductListPage>(`${this.baseUrl}/api/catalog/products`, { params });
  }

  publish(productId: string): Observable<PublishProductResponse> {
    return this.http.post<PublishProductResponse>(
      `${this.baseUrl}/api/catalog/products/${productId}/publish`,
      null,
    );
  }

  import(source = 'seed'): Observable<ImportResult> {
    return this.http.post<ImportResult>(`${this.baseUrl}/api/catalog/import`, { source });
  }
}

