import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/** What a platform's catalogue file is called on the API: Shopify's product CSV, for now. */
export type CatalogueFormat = 'shopify-csv';

/**
 * The only place in the backoffice that knows what the export endpoints are called
 * (phase 16, E-3). The answer is a file, not JSON: it comes back as a Blob for the
 * page to hand over. The reader's language travels as `Accept-Language`, which the
 * culture interceptor sets for every call.
 */
@Injectable({ providedIn: 'root' })
export class ExportsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /**
   * The sales of the shop's days `from` to `to`, both included (ISO dates), as the
   * accountant's spreadsheet in the reader's language. Audited on the server: who
   * took which span.
   */
  orders(from: string, to: string): Observable<Blob> {
    return this.http.post(`${this.baseUrl}/api/exports/orders`, null, {
      params: new HttpParams().set('from', from).set('to', to).set('format', 'csv'),
      responseType: 'blob',
    });
  }

  /** Every live product, as the platform's importer reads it. Audited on the server: who took the catalogue, in which format. */
  catalogue(format: CatalogueFormat): Observable<Blob> {
    return this.http.post(`${this.baseUrl}/api/exports/catalogue`, null, {
      params: new HttpParams().set('format', format),
      responseType: 'blob',
    });
  }
}
