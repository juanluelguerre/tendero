import { computed, inject } from '@angular/core';
import { patchState, signalMethod, signalStore, withComputed, withLinkedState, withMethods, withProps, withState } from '@ngrx/signals';
import type { ManagedCampaignView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { blankForm, type CampaignForm, codeFrom, differs, formOf, toggled } from '../campaign-form';
import { type CampaignWork, CampaignsStore, NewCampaign } from '../campaigns.store';

type Culture = 'es' | 'en';

/**
 * The campaign editor's own state (ADR 0047): the form. It starts again
 * whenever another campaign — or a newer answer about this one — arrives
 * (`withLinkedState`), and is compared with the campaign as saved to know
 * whether Save or Schedule is the view's one clay action. What the editor
 * shares with the list — the work under way, the refusal, the changes — is the
 * page's store, injected here.
 */
export const CampaignEditorStore = signalStore(
  withState<{ campaign: ManagedCampaignView | null }>({ campaign: null }),
  withProps(() => ({
    _calendar: inject(CampaignsStore),
    _culture: inject(CultureStore),
  })),
  withComputed(({ campaign }) => ({
    /** The campaign as saved, in the form's shape: what the form is compared with. */
    _saved: computed<CampaignForm>(() => {
      const current = campaign();
      return current ? formOf(current) : blankForm();
    }),
  })),
  withLinkedState(({ campaign, _saved }) => ({
    /** The form, starting again whenever another campaign — or a newer answer about this one — arrives. */
    form: () => _saved(),
    /** While a new campaign's code is not typed by hand, it follows the name. */
    codeFollowsName: () => campaign() === null,
    /** The name of the file just uploaded; a saved picture is known by its id alone. */
    fileName: (): string | null => {
      campaign();
      return null;
    },
  })),
  withComputed((store) => {
    const key = computed(() => store.campaign()?.code ?? NewCampaign);
    const isNew = computed(() => store.campaign() === null);
    return {
      _key: key,
      isNew,
      editable: computed(() => isNew() || store.campaign()?.status === 'Draft'),
      dirty: computed(() => isNew() || differs(store.form(), store._saved())),
      endable: computed(() => {
        const status = store.campaign()?.status;
        return status === 'Draft' || status === 'Scheduled';
      }),
      /** What is being done to this campaign, if anything: its buttons wait for it. */
      working: computed<CampaignWork['what'] | null>(() => {
        const work = store._calendar.work();
        return work?.code === key() ? work.what : null;
      }),
      refusal: computed(() => {
        const refusal = store._calendar.refusal();
        return refusal?.code === key() ? refusal.message : null;
      }),
      /** The promotions a campaign can carry, named in the reader's language. */
      options: computed(() => {
        const culture = store._culture.active();
        return (store._calendar.promotions.value() ?? []).map((promotion) => ({
          code: promotion.code,
          name: promotion.name[culture] ?? promotion.name['en'] ?? Object.values(promotion.name)[0] ?? promotion.code,
        }));
      }),
      /** The categories a campaign can be scoped to, each with its trail. */
      scopes: computed(() =>
        (store._calendar.categories.value() ?? []).map((category) => {
          const trail = category.path[category.path.length - 1] === category.name ? category.path : [...category.path, category.name];
          return { code: category.code, label: trail.join(' › ') };
        }),
      ),
    };
  }),
  withMethods((store) => {
    const edit = (change: Partial<CampaignForm>) => patchState(store, (state) => ({ form: { ...state.form, ...change } }));

    return {
      /** Pass the component's input signal: null is a new campaign. */
      follow: signalMethod<ManagedCampaignView | null>((campaign) => patchState(store, { campaign })),
      edit,
      editName(culture: Culture, value: string): void {
        patchState(store, (state) => {
          const name = { ...state.form.name, [culture]: value };
          return { form: { ...state.form, name, code: state.codeFollowsName ? codeFrom(name.en || name.es) : state.form.code } };
        });
      },
      editLine(culture: Culture, value: string): void {
        patchState(store, (state) => ({ form: { ...state.form, line: { ...state.form.line, [culture]: value } } }));
      },
      editCode(value: string): void {
        patchState(store, { codeFollowsName: false });
        edit({ code: value.toUpperCase() });
      },
      editPriority(value: string): void {
        edit({ priority: Number(value) || 0 });
      },
      editScope(value: string): void {
        edit({ categoryCode: value || null });
      },
      choose(code: string, on: boolean): void {
        patchState(store, (state) => ({ form: { ...state.form, promotionCodes: toggled(state.form.promotionCodes, code, on) } }));
      },
      editAlt(culture: Culture, value: string): void {
        patchState(store, (state) =>
          state.form.image ? { form: { ...state.form, image: { ...state.form.image, alt: { ...state.form.image.alt, [culture]: value } } } } : {},
        );
      },
      removeImage(): void {
        patchState(store, { fileName: null });
        edit({ image: null });
      },
      upload(file: File): void {
        store._calendar.upload(store._key(), file, (imageId) =>
          patchState(store, (state) => ({ fileName: file.name, form: { ...state.form, image: { imageId, alt: state.form.image?.alt ?? { es: '', en: '' } } } })),
        );
      },
      save(): void {
        const campaign = store.campaign();
        if (campaign) store._calendar.update(campaign.code, store.form());
        else store._calendar.create(store.form());
      },
      move(to: 'schedule' | 'unschedule'): void {
        const campaign = store.campaign();
        if (campaign) store._calendar.move(campaign, to);
      },
      end(): void {
        const campaign = store.campaign();
        if (campaign) store._calendar.end(campaign);
      },
    };
  }),
);
