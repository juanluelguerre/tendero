import { Component, computed, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { WorklistPane } from '../../worklist/worklist-pane/worklist-pane';
import { CampaignEditor } from './campaign-editor/campaign-editor';
import { CampaignList } from './campaign-list/campaign-list';
import { inCulture } from './campaign-state';
import { CampaignsStore } from './campaigns.store';

/**
 * The shop's campaigns (R-8, H-1b) — named windows over promotions, set here
 * for concrete dates (ADR 0044) — as ADR 0042's list and pane.
 *
 * The chips are the calendar a shopkeeper asks about: what runs now, what is
 * scheduled, what is still a draft, what is over. They are derived from the
 * stored status and the window's phase together (`bucketOf`), because neither
 * alone says which. The filter and the open campaign live in the URL, as every
 * list's do, so the palette and a link can open it exactly there.
 */
@Component({
  selector: 'backoffice-campaigns-page',
  imports: [TranslocoDirective, WorklistPane, CampaignList, CampaignEditor],
  providers: [CampaignsStore],
  templateUrl: './campaigns-page.html',
  // The page is a frame: the list-and-pane layout is shared by every worklist
  // (styles.css), and what the list and the editor have of their own is in their folders.
  host: { class: 'block' },
})
export class CampaignsPage {
  protected readonly store = inject(CampaignsStore);
  private readonly culture = inject(CultureStore);

  /** The open campaign's name in the reader's language: the pane's heading. */
  protected readonly selectedName = computed(() => {
    const selected = this.store.selected();
    return selected ? inCulture(selected.name, this.culture.active()) : null;
  });
}
