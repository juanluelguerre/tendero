import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, viewChild } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ManagedCampaignView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { formatDate, ProductImageUrls } from '@tendero/shared-util';
import { ConfirmDialog } from '../../work/confirm-dialog';
import { CampaignCalendar, type CampaignWork, NewCampaign } from './campaign-calendar';
import { blankForm, CampaignKinds, codeFrom, differs, formOf, type CampaignForm, toggled } from './campaign-form';
import { inCulture } from './campaign-state';

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
 * server, never what is only on the screen.
 */
@Component({
  selector: 'backoffice-campaign-editor',
  imports: [TranslocoDirective, ConfirmDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './campaign-editor.html',
  styleUrl: './campaign-editor.css',
})
export class CampaignEditor {
  private readonly calendar = inject(CampaignCalendar);
  private readonly culture = inject(CultureStore);
  private readonly images = inject(ProductImageUrls);

  /** Null is a new campaign. */
  readonly campaign = input.required<ManagedCampaignView | null>();

  private readonly endDialog = viewChild.required(ConfirmDialog);

  /** The prefix of the ids that tie labels and hints to their fields. */
  protected readonly id = `campaign-${++editors}`;
  protected readonly kinds = CampaignKinds;
  protected readonly imageTypes = ImageTypes;

  /** The campaign as saved, in the form's shape: what the form is compared with. */
  private readonly saved = computed<CampaignForm>(() => {
    const campaign = this.campaign();
    return campaign ? formOf(campaign) : blankForm();
  });

  /** The form, starting again whenever another campaign — or a newer answer about this one — arrives. */
  protected readonly form = linkedSignal(() => this.saved());

  /** While a new campaign's code is not typed by hand, it follows the name. */
  private readonly codeFollowsName = linkedSignal(() => this.campaign() === null);

  /** The name of the file just uploaded; a saved picture is known by its id alone. */
  protected readonly fileName = linkedSignal<string | null>(() => {
    this.campaign();
    return null;
  });

  private readonly key = computed(() => this.campaign()?.code ?? NewCampaign);

  protected readonly isNew = computed(() => this.campaign() === null);
  protected readonly editable = computed(() => this.isNew() || this.campaign()?.status === 'Draft');
  protected readonly dirty = computed(() => this.isNew() || differs(this.form(), this.saved()));
  protected readonly endable = computed(() => {
    const status = this.campaign()?.status;
    return status === 'Draft' || status === 'Scheduled';
  });

  /** What is being done to this campaign, if anything: its buttons wait for it. */
  protected readonly working = computed<CampaignWork['what'] | null>(() => {
    const work = this.calendar.work();
    return work?.code === this.key() ? work.what : null;
  });

  protected readonly refusal = computed(() => {
    const refusal = this.calendar.refusal();
    return refusal?.code === this.key() ? refusal.message : null;
  });

  protected readonly options = computed(() => {
    const culture = this.culture.active();
    return (this.calendar.promotions.value() ?? []).map((promotion) => ({
      code: promotion.code,
      name: promotion.name[culture] ?? promotion.name['en'] ?? Object.values(promotion.name)[0] ?? promotion.code,
    }));
  });

  protected readonly scopes = computed(() =>
    (this.calendar.categories.value() ?? []).map((category) => {
      const trail = category.path[category.path.length - 1] === category.name ? category.path : [...category.path, category.name];
      return { code: category.code, label: trail.join(' › ') };
    }),
  );

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
    const image = this.form().image;
    return image ? this.images.of(image.imageId) : null;
  });

  protected readonly imageAlt = computed(() => {
    const image = this.form().image;
    return image ? inCulture(image.alt, this.culture.active()) : '';
  });

  protected edit(change: Partial<CampaignForm>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected editName(culture: 'es' | 'en', value: string): void {
    this.form.update((form) => {
      const name = { ...form.name, [culture]: value };
      return { ...form, name, code: this.codeFollowsName() ? codeFrom(name.en || name.es) : form.code };
    });
  }

  protected editLine(culture: 'es' | 'en', value: string): void {
    this.form.update((form) => ({ ...form, line: { ...form.line, [culture]: value } }));
  }

  protected editCode(value: string): void {
    this.codeFollowsName.set(false);
    this.edit({ code: value.toUpperCase() });
  }

  protected editPriority(value: string): void {
    this.edit({ priority: Number(value) || 0 });
  }

  protected editScope(value: string): void {
    this.edit({ categoryCode: value || null });
  }

  protected choose(code: string, on: boolean): void {
    this.form.update((form) => ({ ...form, promotionCodes: toggled(form.promotionCodes, code, on) }));
  }

  protected editAlt(culture: 'es' | 'en', value: string): void {
    this.form.update((form) => (form.image ? { ...form, image: { ...form.image, alt: { ...form.image.alt, [culture]: value } } } : form));
  }

  protected removeImage(): void {
    this.fileName.set(null);
    this.edit({ image: null });
  }

  protected upload(field: HTMLInputElement): void {
    const file = field.files?.[0];
    field.value = '';
    if (!file) return;

    this.calendar.upload(this.key(), file, (imageId) => {
      this.fileName.set(file.name);
      this.form.update((form) => ({ ...form, image: { imageId, alt: form.image?.alt ?? { es: '', en: '' } } }));
    });
  }

  protected save(): void {
    const campaign = this.campaign();
    if (campaign) this.calendar.update(campaign.code, this.form());
    else this.calendar.create(this.form());
  }

  protected move(move: 'schedule' | 'unschedule'): void {
    const campaign = this.campaign();
    if (campaign) this.calendar.move(campaign, move);
  }

  protected askToEnd(): void {
    this.endDialog().open();
  }

  protected end(): void {
    const campaign = this.campaign();
    if (campaign) this.calendar.end(campaign);
  }
}
