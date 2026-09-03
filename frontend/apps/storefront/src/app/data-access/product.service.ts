import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { ProductDetail } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * One product, in full.
 *
 * It is keyed on the CODE and not on the slug (ADR 0026). The slug is in the
 * page's URL for humans and for crawlers, and this never sends it: a slug is
 * derived from a name, so two products called the same thing claim the same one
 * and renaming a product changes it. The code is minted once and never moves.
 *
 * It reads the catalogue and not the search index, which is why a product page
 * still works when Elasticsearch does not.
 */
@Injectable({ providedIn: 'root' })
export class ProductService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  get(code: string, culture: string): Observable<ProductDetail> {
    const params = new HttpParams().set('culture', culture);

    return this.http.get<ProductDetail>(
      `${this.baseUrl}/api/catalog/products/${encodeURIComponent(code)}`,
      { params },
    );
  }
}
