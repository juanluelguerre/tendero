import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { AttributeDefinitionView } from '@tendero/shared-api';
import { CatalogService } from '../../data-access/catalog.service';

/**
 * Las definiciones de atributo, con una columna por idioma.
 *
 * Es de solo lectura, y eso no es un recorte silencioso: hoy las definiciones se
 * sirven del fichero del repositorio, donde se revisan en un diff y se traducen.
 * Editarlas desde aqui necesita persistirlas, y esa tabla se creo y se borro al
 * dia siguiente por no tener quien la leyera. Vuelve cuando editar sea la
 * funcionalidad, no la excusa.
 *
 * Lo que SI resuelve esta pantalla es la pregunta que valia 0.091 de NDCG: que
 * etiquetas no estan traducidas. Una opcion sin ingles es exactamente lo que
 * hacia que "navy blue shoes" no encontrara nada.
 */
@Component({
  selector: 'backoffice-attributes',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section *transloco="let t">
      <header class="head">
        <h1>{{ t('attributes.title') }}</h1>
        @if (incomplete() > 0) {
          <span class="tag tag--warn">{{ t('attributes.incomplete', { count: incomplete() }) }}</span>
        }
      </header>
      <p class="hint">{{ t('attributes.hint') }}</p>

      @if (loading()) {
        <p class="hint" role="status" aria-live="polite">{{ t('attributes.loading') }}</p>
      } @else if (failed()) {
        <p class="failed" role="alert">{{ t('attributes.failed') }}</p>
      } @else {
        <table>
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
                <td class="numeric">{{ definition.code }}</td>
                <td [class.missing]="!label(definition, 'es')">{{ label(definition, 'es') }}</td>
                <td [class.missing]="!label(definition, 'en')">{{ label(definition, 'en') }}</td>
                <td class="hint">{{ definition.kind }}</td>
                <td>
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
      }
    </section>
  `,
  styles: `
    .head { display: flex; align-items: baseline; gap: var(--space-3); }
    h1 { font-family: var(--font-display); font-size: var(--text-lg); margin: 0; }
    .hint { color: var(--text-muted); font-size: var(--text-xs); }
    .failed { color: var(--danger); font-size: var(--text-sm); }
    table { width: 100%; border-collapse: collapse; font-size: var(--text-xs); }
    th, td { text-align: start; padding: var(--space-2); border-block-end: 1px solid var(--border); }
    th { color: var(--text-muted); font-weight: 500; }
    .missing { color: var(--danger); }
    .tag { display: inline-block; margin-inline-start: var(--space-2); padding: 0 var(--space-2); border-radius: var(--radius-sm); font-size: var(--text-2xs); }
    .tag--ok { background: color-mix(in srgb, var(--positive) 18%, transparent); color: var(--positive); }
    .tag--warn { background: color-mix(in srgb, var(--warning) 18%, transparent); color: var(--warning); }
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
   * La etiqueta cruda de una cultura, SIN cadena de fallback. Resolverla aqui
   * mostraria el espanol en la columna inglesa y la fila parecaria completa,
   * que es justo el fallo que esta pantalla existe para hacer visible.
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
