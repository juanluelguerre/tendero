import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { VisualSearchAvailability, VisualSearchResult } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * The only place in the storefront that knows what searching with a picture is
 * called on the server (phase 13a).
 */
@Injectable({ providedIn: 'root' })
export class VisualSearchService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** Whether the shop can be searched with a picture. Configured on the server, not probed: no model is run. */
  status(): Observable<VisualSearchAvailability> {
    return this.http.get<VisualSearchAvailability>(`${this.baseUrl}/api/search/by-image/status`);
  }

  /**
   * The products whose pictures are nearest this one. The picture goes as a
   * form's one file; the server reads it, answers and keeps nothing of it.
   * Unsubscribing aborts the request.
   */
  byPicture(picture: Blob, culture: string): Observable<VisualSearchResult> {
    const form = new FormData();
    form.append('picture', picture, 'picture');
    return this.http.post<VisualSearchResult>(`${this.baseUrl}/api/search/by-image`, form, {
      params: new HttpParams().set('culture', culture),
    });
  }
}
