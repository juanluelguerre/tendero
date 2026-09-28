import { computed, inject, Injectable, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import type { CampaignChangeResponse, ManagedCampaignView } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { map, type Observable } from 'rxjs';
import { CatalogService } from '../../data-access/catalog.service';
import { PricingService } from '../../data-access/pricing.service';
import { type BackofficeAction, BackofficeUsage } from '../../usage';
import { refusalOf } from '../../work/refusal';
import { Undo } from '../../work/undo';
import { type CampaignForm, createRequestOf, detailsOf } from './campaign-form';
import { bucketOf, inCulture } from './campaign-state';

/** What is being done, and to which campaign — `new` before it has a code. */
export interface CampaignWork {
  readonly code: string;
  readonly what: 'save' | 'schedule' | 'unschedule' | 'end' | 'image';
}

/** The key of a campaign not saved yet, for its work and its refusals. */
export const NewCampaign = 'new';

/**
 * The shopkeeper's campaigns and what can be done to them, for as long as the
 * Campaigns screen is open: provided by the page, not the root.
 *
 * The list, the pane and Undo all outlive one another here. The pane closes, is
 * opened on another campaign, or is replaced by a new one while an answer is on
 * its way or an Undo is on offer; the work belongs to the screen, so it lands
 * whatever the pane is showing. Every change is the server's (ADR 0042, 5): the
 * list is asked again after it, and nothing is painted before.
 */
@Injectable()
export class CampaignCalendar {
  private readonly pricing = inject(PricingService);
  private readonly catalog = inject(CatalogService);
  private readonly usage = inject(BackofficeUsage);
  private readonly culture = inject(CultureStore);
  private readonly transloco = inject(TranslocoService);
  private readonly undo = inject(Undo);
  private readonly router = inject(Router);

  readonly campaigns = rxResource({ stream: () => this.pricing.campaigns() });
  readonly promotions = rxResource({ stream: () => this.pricing.promotions().pipe(map((list) => list.items)) });
  readonly categories = rxResource({
    params: () => this.culture.active(),
    stream: ({ params }) => this.catalog.categories(params).pipe(map((list) => list.items)),
  });

  readonly work = signal<CampaignWork | null>(null);
  /** The server's words for the last refusal, and whose it was. */
  readonly refusal = signal<{ readonly code: string; readonly message: string } | null>(null);

  /** A category's name by its code, for the list's scope column. */
  readonly categoryNames = computed(() => new Map((this.categories.value() ?? []).map((category) => [category.code, category.name])));

  create(form: CampaignForm): void {
    this.run({ code: NewCampaign, what: 'save' }, 'campaign_create', this.pricing.createCampaign(createRequestOf(form)), (answer) =>
      // The new campaign opens where it now lives: among the drafts.
      void this.router.navigate([], {
        queryParams: { status: bucketOf(answer.campaign), campaign: answer.campaign.code },
        queryParamsHandling: 'merge',
      }),
    );
  }

  update(code: string, form: CampaignForm): void {
    this.run({ code, what: 'save' }, 'campaign_update', this.pricing.updateCampaign(code, detailsOf(form)));
  }

  /** Scheduling and unscheduling are each other's reverse, so neither asks, and each offers the other (R-2, P12). */
  move(campaign: ManagedCampaignView, move: 'schedule' | 'unschedule'): void {
    const reverse = move === 'schedule' ? 'unschedule' : 'schedule';
    const action: BackofficeAction = move === 'schedule' ? 'campaign_schedule' : 'campaign_unschedule';
    const name = inCulture(campaign.name, this.culture.active());

    this.run({ code: campaign.code, what: move }, action, this.pricing.moveCampaign(campaign.code, move), (answer) =>
      this.undo.offerTo(this.transloco.translate(`campaigns.moved.${move}`, { name }), () => this.move(answer.campaign, reverse)),
    );
  }

  /** Final: the pane asks first. */
  end(campaign: ManagedCampaignView): void {
    this.run({ code: campaign.code, what: 'end' }, 'campaign_end', this.pricing.moveCampaign(campaign.code, 'end'));
  }

  /** Stores the picture and hands its id to `then`; the campaign takes it when it is saved. */
  upload(code: string, file: File, then: (imageId: string) => void): void {
    this.start({ code, what: 'image' });
    this.pricing.uploadCampaignImage(file).subscribe({
      next: ({ imageId }) => {
        this.work.set(null);
        then(imageId);
      },
      error: (failure: unknown) => this.refused(code, failure),
    });
  }

  private run(
    work: CampaignWork,
    action: BackofficeAction,
    change: Observable<CampaignChangeResponse>,
    done?: (answer: CampaignChangeResponse) => void,
  ): void {
    this.start(work);

    change.pipe(this.usage.reported('campaigns', action, 'pane')).subscribe({
      next: (answer) => {
        this.work.set(null);
        done?.(answer);
        this.campaigns.reload();
      },
      error: (failure: unknown) => this.refused(work.code, failure),
    });
  }

  private start(work: CampaignWork): void {
    this.work.set(work);
    this.refusal.set(null);
  }

  private refused(code: string, failure: unknown): void {
    this.work.set(null);
    if (isSessionExpired(failure)) return;
    this.refusal.set({ code, message: refusalOf(failure) });
  }
}
