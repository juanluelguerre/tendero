import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type {
  AttributeDefinitionList,
  DefineVariantsResponse,
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

  /**
   * Genera la matriz de variantes: el producto cartesiano de los ejes. Se manda
   * entera porque es lo que un tendero espera al declarar "colores x tallas", y
   * retirar despues las combinaciones que no existen cuesta menos que crearlas
   * una a una.
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

  /** Las definiciones de atributo, con su etiqueta en todas las culturas. */
  attributeDefinitions(): Observable<AttributeDefinitionList> {
    return this.http.get<AttributeDefinitionList>(`${this.baseUrl}/api/catalog/attributes`);
  }
}
