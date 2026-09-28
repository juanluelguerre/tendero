import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type {
  CampaignChangeResponse,
  CampaignDetailsRequest,
  CampaignImageUploaded,
  CreateCampaignRequest,
  ManagedCampaignView,
  PromotionList,
} from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * The only place in the backoffice that knows what the pricing endpoints are
 * called. Separate from CatalogService rather than folded into it: they are two
 * bounded contexts, and one service reaching into both is how a frontend starts
 * pretending they are one.
 */
@Injectable({ providedIn: 'root' })
export class PricingService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** Every promotion, running or not, with its combination policy. */
  promotions(): Observable<PromotionList> {
    return this.http.get<PromotionList>(`${this.baseUrl}/api/pricing/promotions`);
  }

  /**
   * Every campaign the shop has, in both languages, with its stored status and
   * the phase its window is in (ADR 0044). The backoffice's calendar, not the
   * shop's public list at `/api/pricing/campaigns`.
   */
  campaigns(): Observable<ManagedCampaignView[]> {
    return this.http.get<ManagedCampaignView[]>(this.calendar());
  }

  createCampaign(request: CreateCampaignRequest): Observable<CampaignChangeResponse> {
    return this.http.post<CampaignChangeResponse>(this.calendar(), request);
  }

  updateCampaign(code: string, details: CampaignDetailsRequest): Observable<CampaignChangeResponse> {
    return this.http.put<CampaignChangeResponse>(this.calendar(code), details);
  }

  /** Scheduling, unscheduling to edit, and ending early: the lifecycle's three moves. */
  moveCampaign(code: string, move: CampaignMove): Observable<CampaignChangeResponse> {
    return this.http.post<CampaignChangeResponse>(`${this.calendar(code)}/${move}`, {});
  }

  /** Stores a picture for a campaign and answers its id; the campaign references it when saved. */
  uploadCampaignImage(file: File): Observable<CampaignImageUploaded> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<CampaignImageUploaded>(`${this.baseUrl}/api/pricing/campaign-images`, form);
  }

  private calendar(code?: string): string {
    const root = `${this.baseUrl}/api/pricing/campaign-calendar`;
    return code ? `${root}/${encodeURIComponent(code)}` : root;
  }
}

/** The moves the calendar answers, by the last segment of their path. */
export type CampaignMove = 'schedule' | 'unschedule' | 'end';
