import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { AttributeDefinitionView } from '@tendero/shared-api';
import { CatalogService } from '../../data-access/catalog.service';

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
  template: `
    <section *transloco="let t">
      <header class="page-head">
        <h1 class="page-title">{{ t('attributes.title') }}</h1>
        @if (incomplete() > 0) {
          <span class="tag tag--warn">{{ t('attributes.incomplete', { count: incomplete() }) }}</span>
        } @else {
          <span class="tag tag--ok">es · en</span>
        }
      </header>
      <p class="page-hint">{{ t('attributes.hint') }}</p>

      @if (loading()) {
        <p class="muted" role="status" aria-live="polite">{{ t('attributes.loading') }}</p>
      } @else if (failed()) {
        <p class="failed" role="alert">{{ t('attributes.failed') }}</p>
      } @else {
        <div class="table-wrap">
        <table class="table">
          <caption class="sr-only">{{ t('attributes.title') }}</caption>
          <thead>
            <tr>
              <th scope="col">{{ t('attributes.column.code') }}</th>
              <th scope="col">es</th>
              <th scope="col">en</th>
              <th scope="col">{{ t('attributes.column.kind') }}</th>
              <th scope="col">{{ t('attributes.column.options') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (definition of items(); track definition.code) {
              <tr>
                <td class="numeric code">{{ definition.code }}</td>
                <td [class.missing]="!label(definition, 'es')">{{ label(definition, 'es') }}</td>
                <td [class.missing]="!label(definition, 'en')">{{ label(definition, 'en') }}</td>
                <td><span class="tag tag--code">{{ definition.kind }}</span></td>
                <td class="options">
                  @if (definition.options.length > 0) {
                    <span class="numeric">{{ definition.options.length }}</span>
                  }
                  @if (definition.missingCultures.length > 0) {
                    <span class="tag tag--warn">{{ definition.missingCultures.join(' · ') }}</span>
                  } @else {
                    <span class="tag tag--ok">es · en</span>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
        </div>
      }
    </section>
  `,
  styles: `
    /* Only what this page adds; the rest is a primitive in styles.css. */
    .code { color: var(--text); font-weight: 500; }

    /* The option count and the completeness tag are two facts, not one word:
       without the gap they read as "5es · en". */
    .options { display: flex; align-items: center; gap: var(--space-2); }

    /* A missing label is the fact this screen exists for, so it is loud: the
       word "missing" in danger colour, not an empty cell somebody has to
       notice. */
    .missing::after {
      content: '—';
      color: var(--danger);
    }
    .missing { color: var(--danger); }
  `,
})
export class AttributesPage {
  private readonly catalog = inject(CatalogService);

  protected readonly items = signal<AttributeDefinitionView[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  /** Cuantas definiciones tienen alguna etiqueta sin traducir. */
  protected incomplete(): number {
    return this.items().filter((definition) => definition.missingCultures.length > 0).length;
  }

  /**
   * One culture's raw label, with NO fallback chain. Resolving it here would
   * show the Spanish in the English column and the row would look complete,
   * which is exactly the failure this screen exists to make visible.
   */
  protected label(definition: AttributeDefinitionView, culture: string): string {
    return definition.label[culture] ?? '';
  }

  constructor() {
    this.catalog.attributeDefinitions().subscribe({
      next: (result) => {
        this.items.set(result.items);
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }
}
