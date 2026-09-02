import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CatalogService } from '../../data-access/catalog.service';

interface AxisDraft {
  code: string;
  /** Comma-separated options, which is how they get pasted from a spreadsheet. */
  options: string;
}

/**
 * Declaring which axes a product varies by, and generating the matrix.
 *
 * It is a shopkeeper's screen, not an import one: a source may bring variants or
 * not, but deciding that a shirt sells in three colours by four sizes is a
 * catalogue decision.
 *
 * The whole cartesian product is generated on purpose. Retiring the three
 * combinations that do not exist afterwards costs less than creating the
 * twenty-one that do.
 */
@Component({
  selector: 'backoffice-define-variants',
  imports: [FormsModule, RouterLink, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="variants" *transloco="let t">
      <h1>{{ t('variants.title') }}</h1>
      <p class="hint">{{ t('variants.hint') }}</p>

      @for (axis of axes(); track $index) {
        <div class="axis">
          <label>
            <span>{{ t('variants.axisCode') }}</span>
            <input [(ngModel)]="axis.code" [name]="'code' + $index" placeholder="COLOR" />
          </label>
          <label class="grow">
            <span>{{ t('variants.axisOptions') }}</span>
            <input [(ngModel)]="axis.options" [name]="'options' + $index" placeholder="NAVY, BLACK" />
          </label>
        </div>
      }

      <div class="actions">
        <button type="button" class="secondary" (click)="addAxis()">{{ t('variants.addAxis') }}</button>
        <span class="preview numeric">{{ t('variants.willCreate', { count: combinations() }) }}</span>
        <button type="button" [disabled]="busy() || combinations() === 0" (click)="generate()">
          {{ busy() ? t('variants.generating') : t('variants.generate') }}
        </button>
      </div>

      @if (created() !== null) {
        <p class="done" role="status">{{ t('variants.created', { count: created() }) }}</p>
      }
      @if (failed()) {
        <p class="failed" role="alert">{{ failed() }}</p>
      }

      <a routerLink="/review">{{ t('variants.back') }}</a>
    </section>
  `,
  styles: `
    .variants { display: flex; flex-direction: column; gap: var(--space-3); max-width: 40rem; }
    h1 { font-family: var(--font-display); font-size: var(--text-lg); margin: 0; }
    .hint, .muted { color: var(--text-muted); font-size: var(--text-xs); margin: 0; }
    .axis { display: flex; gap: var(--space-2); }
    label { display: flex; flex-direction: column; gap: var(--space-1); font-size: var(--text-xs); }
    label.grow { flex: 1; }
    input {
      padding: var(--space-2); background: var(--bg-surface); color: inherit;
      border: 1px solid var(--border); border-radius: var(--radius-sm); font: inherit;
    }
    .actions { display: flex; align-items: center; gap: var(--space-3); }
    .preview { color: var(--text-muted); font-size: var(--text-xs); margin-inline-start: auto; }
    button {
      padding: var(--space-2) var(--space-3); border-radius: var(--radius-sm);
      border: 1px solid var(--accent); background: var(--accent); color: var(--on-accent);
      font: inherit; cursor: pointer;
    }
    button.secondary { background: none; color: inherit; border-color: var(--border); }
    button:disabled { opacity: 0.6; cursor: not-allowed; }
    .done { color: var(--positive); font-size: var(--text-sm); margin: 0; }
    .failed { color: var(--danger); font-size: var(--text-sm); margin: 0; }
  `,
})
export class DefineVariantsPage {
  private readonly catalog = inject(CatalogService);
  private readonly route = inject(ActivatedRoute);

  protected readonly axes = signal<AxisDraft[]>([{ code: '', options: '' }]);
  protected readonly busy = signal(false);
  protected readonly created = signal<number | null>(null);
  protected readonly failed = signal<string | null>(null);

  /** How many variants would come out. It is shown BEFORE you press, because the
   *  cartesian product grows faster than intuition expects. */
  protected combinations(): number {
    const counts = this.axes()
      .map((axis) => this.parse(axis.options).length)
      .filter((count) => count > 0);

    return counts.length === 0 ? 0 : counts.reduce((total, count) => total * count, 1);
  }

  protected addAxis(): void {
    this.axes.update((axes) => [...axes, { code: '', options: '' }]);
  }

  protected async generate(): Promise<void> {
    const productId = this.route.snapshot.paramMap.get('id');
    if (!productId) return;

    this.busy.set(true);
    this.created.set(null);
    this.failed.set(null);

    const axes = this.axes()
      .filter((axis) => axis.code.trim() && this.parse(axis.options).length > 0)
      .map((axis) => ({ code: axis.code.trim(), options: this.parse(axis.options) }));

    try {
      const response = await new Promise<{ created: number }>((resolve, reject) =>
        this.catalog.defineVariants(productId, axes).subscribe({ next: resolve, error: reject }),
      );
      this.created.set(response.created);
    } catch (error: unknown) {
      // The server's 409 carries the domain's reason; showing it is more useful
      // than a "something went wrong" that forces you to open the console.
      const conflict = error as { error?: string };
      this.failed.set(typeof conflict.error === 'string' ? conflict.error : 'Could not define the variants.');
    } finally {
      this.busy.set(false);
    }
  }

  private parse(options: string): string[] {
    return options
      .split(',')
      .map((option) => option.trim())
      .filter((option) => option.length > 0);
  }
}
