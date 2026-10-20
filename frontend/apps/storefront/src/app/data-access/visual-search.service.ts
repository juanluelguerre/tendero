import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { PictureObjectsView, VisualSearchAvailability, VisualSearchResult } from '@tendero/shared-api';
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
  byPicture(picture: Blob, culture: string, within: Within = {}): Observable<VisualSearchResult> {
    let params = new HttpParams().set('culture', culture);
    if (within.category) params = params.set('category', within.category);
    if (within.words) params = params.set('q', within.words);
    return this.http.post<VisualSearchResult>(`${this.baseUrl}/api/search/by-image`, form(picture), { params });
  }

  /**
   * The objects in a picture (the camera search, CS-2): where each is, its kind,
   * its name in the culture asked and whether the shop sells it, in the
   * picture's reading order. Nothing is searched; the browser asks for each
   * object's crop afterwards.
   */
  objects(picture: Blob, culture: string): Observable<PictureObjectsView> {
    return this.http.post<PictureObjectsView>(`${this.baseUrl}/api/search/by-image/objects`, form(picture), {
      params: new HttpParams().set('culture', culture),
    });
  }
}

/** What narrows a search with a picture: one object's category, and the words typed after it (CS-4). */
export interface Within {
  readonly category?: string | null;
  readonly words?: string | null;
}

function form(picture: Blob): FormData {
  const body = new FormData();
  body.append('picture', picture, 'picture');
  return body;
}
