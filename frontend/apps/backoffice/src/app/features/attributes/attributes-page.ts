import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { AttributeDefinitionView } from '@tendero/shared-api';
import { AttributesStore } from './attributes.store';

/**
 * The attribute definitions, with one column per language.
 *
 * It is read-only, and that is not a silent cut: today the definitions are
 * served from the repository's file, where they get reviewed in a diff and
 * translated. Editing them from here needs somewhere to persist them, and that
 * table was created and dropped the next day for having no reader. It comes back
 * when editing is the feature, not the excuse.
 *
 * What this screen DOES answer is the question that was worth 0.091 of NDCG:
 * which labels are untranslated. An option with no English is exactly what made
 * "navy blue shoes" find nothing.
 */
@Component({
  selector: 'backoffice-attributes',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './attributes-page.html',
  styleUrl: './attributes-page.css',
  providers: [AttributesStore],
})
export class AttributesPage {
  protected readonly store = inject(AttributesStore);

  /**
   * One culture's raw label, with NO fallback chain. Resolving it here would
   * show the Spanish in the English column and the row would look complete,
   * which is exactly the failure this screen exists to make visible.
   */
  protected label(definition: AttributeDefinitionView, culture: string): string {
    return definition.label[culture] ?? '';
  }
}
