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
  templateUrl: './define-variants-page.html',
  styleUrl: './define-variants-page.css',
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
