import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { SearchResultPage } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * El unico sitio del storefront que sabe como se llama el endpoint de busqueda.
 * El contrato de `/api/search` no cambia aunque debajo entre la busqueda
 * hibrida: es la promesa del ADR 0004.
 */
@Injectable({ providedIn: 'root' })
export class ProductSearchService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  search(text: string, culture: string, pageSize = 20): Observable<SearchResultPage> {
    const params = new HttpParams()
      .set('q', text)
      .set('culture', culture)
      .set('pageSize', pageSize);

    return this.http.get<SearchResultPage>(`${this.baseUrl}/api/search`, { params });
  }
}
