import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { SpecSheet } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * The specs of one product or several, each value with where it came from
 * (ADR 0018). The product page asks for one code, the comparison for up to four.
 *
 * Optional by design (invariant 8): the product page already has its attributes
 * from the catalogue, so a failure here costs the source chips and nothing else.
 */
@Injectable({ providedIn: 'root' })
export class SpecsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  get(codes: readonly string[], culture: string): Observable<SpecSheet> {
    const params = new HttpParams().set('codes', codes.join(',')).set('culture', culture);
    return this.http.get<SpecSheet>(`${this.baseUrl}/api/knowledge/specs`, { params });
  }
}
