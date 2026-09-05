import { inject, Injectable } from '@angular/core';
import { API_BASE_URL } from './api-base-url';

/**
 * Where a product image is served from.
 *
 * The catalogue stores an `ImageId` — the SHA-256 of the bytes — and the API
 * serves it at `/api/images/{id}` (ADR 0011). Four components had written that
 * path by hand, three of them relative and one prefixed with the API base, which
 * is the kind of disagreement that works in development, where the base is
 * empty and the dev server proxies, and breaks the first time the API lives on
 * another origin.
 *
 * One place, so putting a CDN in front is one edit here and none in a template.
 */
@Injectable({ providedIn: 'root' })
export class ProductImageUrls {
  private readonly baseUrl = inject(API_BASE_URL);

  of(imageId: string): string {
    return `${this.baseUrl}/api/images/${encodeURIComponent(imageId)}`;
  }
}
