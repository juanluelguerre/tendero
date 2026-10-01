import { NgOptimizedImage } from '@angular/common';
import { Component, computed, inject, input, viewChild } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ManagedCampaignView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { formatDate, ProductImageUrls } from '@tendero/shared-util';
import { ConfirmDialog } from '../../../worklist/confirm-dialog/confirm-dialog';
import { CampaignKinds } from '../campaign-form';
import { inCulture } from '../campaign-state';
import { CampaignEditorStore } from './campaign-editor.store';

/** What the image field takes: the formats the server recognises by their bytes. */
const ImageTypes = 'image/webp,image/avif,image/png,image/jpeg';

let editors = 0;

/**
 * A campaign in the pane beside the calendar (ADR 0042): created and edited as
 * a draft, then scheduled, unscheduled to edit again, or ended early.
 *
 * **Only a draft is edited.** A scheduled campaign is the shop's word to its
 * customers, so it reads here and says how to change it: unschedule first.
 * Ending cannot be undone, so it asks, apart from the likely action. The
 * view's one clay action is Save while the form says something the server
 * does not know yet, and Schedule once it does — scheduling what is on the
 * server, never what is only on the screen. The form is its store's (ADR 0047);
 * dates, the picture's address and the file field are the component's.
 */
@Component({
  selector: 'backoffice-campaign-editor',
  imports: [TranslocoDirective, ConfirmDialog, NgOptimizedImage],
  templateUrl: './campaign-editor.html',
  host: { class: 'block' },
  providers: [CampaignEditorStore],
})
export class CampaignEditor {
  protected readonly store = inject(CampaignEditorStore);
  private readonly culture = inject(CultureStore);
  private readonly images = inject(ProductImageUrls);

  /** Null is a new campaign. */
  readonly campaign = input.required<ManagedCampaignView | null>();

  private readonly endDialog = viewChild.required(ConfirmDialog);

  /** The prefix of the ids that tie labels and hints to their fields. */
  protected readonly id = `campaign-${++editors}`;
  protected readonly kinds = CampaignKinds;
  protected readonly imageTypes = ImageTypes;

  constructor() {
    this.store.follow(this.campaign);
  }

  /** When it was created, changed and ended — the dates the aggregate keeps; who did it is in the audit log. */
  protected readonly history = computed(() => {
    const campaign = this.campaign();
    if (!campaign) return null;
    const culture = this.culture.active();
    return {
      created: formatDate(campaign.createdAt, culture),
      updated: campaign.updatedAt !== campaign.createdAt ? formatDate(campaign.updatedAt, culture) : null,
      ended: campaign.endedAt ? formatDate(campaign.endedAt, culture) : null,
    };
  });

  /** The campaign's name in the reader's language, as saved. */
  protected readonly displayName = computed(() => {
    const campaign = this.campaign();
    return campaign ? inCulture(campaign.name, this.culture.active()) : '';
  });

  protected readonly imageUrl = computed(() => {
    const image = this.store.form().image;
    return image ? this.images.of(image.imageId) : null;
  });

  protected readonly imageAlt = computed(() => {
    const image = this.store.form().image;
    return image ? inCulture(image.alt, this.culture.active()) : '';
  });

  protected upload(field: HTMLInputElement): void {
    const file = field.files?.[0];
    field.value = '';
    if (file) this.store.upload(file);
  }

  protected askToEnd(): void {
    this.endDialog().open();
  }
}
