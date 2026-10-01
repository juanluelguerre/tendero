import { computed, inject, signal } from '@angular/core';
import { form } from '@angular/forms/signals';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { isSessionExpired } from '@tendero/shared-auth';
import { CatalogService } from '../../data-access/catalog.service';

interface AxisDraft {
  readonly code: string;
  /** Comma-separated options, which is how they get pasted from a spreadsheet. */
  readonly options: string;
}

function optionsOf(options: string): string[] {
  return options
    .split(',')
    .map((option) => option.trim())
    .filter((option) => option.length > 0);
}

/**
 * Defining a product's variants (ADR 0047): the axes being drafted, how many
 * variants they would make, and what the server answered.
 *
 * The axes are a Signal Form (ADR 0051): the draft is input that lives while it
 * is typed, so its model is a writable signal in the store's props — a form
 * writes its model, which the store's frozen state is not for — and the inputs
 * bind to `axesForm` with `[formField]`. The count reads the model, so it
 * follows every keystroke.
 */
export const DefineVariantsStore = signalStore(
  withState({
    busy: false,
    created: null as number | null,
    failed: null as string | null,
  }),
  withProps(() => {
    const axes = signal<AxisDraft[]>([{ code: '', options: '' }]);
    return { _axes: axes, axesForm: form(axes) };
  }),
  withComputed(({ _axes }) => ({
    /** How many variants would come out. Shown BEFORE you press, because the
     *  cartesian product grows faster than intuition expects. */
    combinations: computed(() => {
      const counts = _axes()
        .map((axis) => optionsOf(axis.options).length)
        .filter((count) => count > 0);
      return counts.length === 0 ? 0 : counts.reduce((total, count) => total * count, 1);
    }),
  })),
  withMethods((store, catalog = inject(CatalogService)) => ({
    addAxis(): void {
      store._axes.update((axes) => [...axes, { code: '', options: '' }]);
    },
    generate(productId: string): void {
      const axes = store
        ._axes()
        .filter((axis) => axis.code.trim() && optionsOf(axis.options).length > 0)
        .map((axis) => ({ code: axis.code.trim(), options: optionsOf(axis.options) }));

      patchState(store, { busy: true, created: null, failed: null });
      catalog.defineVariants(productId, axes).subscribe({
        next: (response) => patchState(store, { busy: false, created: response.created }),
        error: (error: unknown) => {
          // A session that ended is not a refusal from the domain, and the shell is
          // already sending the person to the door. Saying anything here would put
          // an error over a screen that is about to be replaced.
          if (isSessionExpired(error)) {
            patchState(store, { busy: false });
            return;
          }
          // The server's 409 carries the domain's reason; showing it is more useful
          // than a "something went wrong" that forces you to open the console.
          const conflict = error as { error?: unknown };
          patchState(store, {
            busy: false,
            failed: typeof conflict.error === 'string' ? conflict.error : 'Could not define the variants.',
          });
        },
      });
    },
  })),
);
