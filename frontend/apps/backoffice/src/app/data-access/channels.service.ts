import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { HeldOrders } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * The only place in the backoffice that knows what the sales channels' endpoints are
 * called (phase 16, S3). The reader's language travels as `Accept-Language`, which
 * the culture interceptor sets for every call.
 */
@Injectable({ providedIn: 'root' })
export class ChannelsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** The orders a channel took and charged for that could not be received (P16-29), oldest first. */
  heldOrders(): Observable<HeldOrders> {
    return this.http.get<HeldOrders>(`${this.baseUrl}/api/channels/held-orders`);
  }
}
