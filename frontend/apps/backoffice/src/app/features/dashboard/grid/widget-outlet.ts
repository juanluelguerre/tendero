import {
  type ComponentRef,
  DestroyRef,
  Directive,
  effect,
  inject,
  input,
  type Type,
  untracked,
  ViewContainerRef,
} from '@angular/core';

/**
 * Draws one widget where it sits and hands it its inputs (R-9). Angular's
 * `NgComponentOutlet` does the same, but it lives in `@angular/common`, which
 * esbuild keeps whole in the first bundle: using it from the dashboard's lazy
 * chunk moved 2.2 kB into every page's download. This is `ViewContainerRef`
 * from `@angular/core`, which is there already.
 */
@Directive({ selector: '[backofficeWidgetOutlet]' })
export class WidgetOutlet {
  readonly component = input.required<Type<unknown>>({ alias: 'backofficeWidgetOutlet' });
  readonly inputs = input<Readonly<Record<string, unknown>>>({});

  private readonly container = inject(ViewContainerRef);
  private widget: ComponentRef<unknown> | undefined;

  constructor() {
    // A new component replaces the old one; the same component only gets its new inputs.
    effect(() => {
      const component = this.component();
      untracked(() => {
        this.container.clear();
        this.widget = this.container.createComponent(component);
      });
    });

    effect(() => {
      const inputs = this.inputs();
      untracked(() => {
        for (const [name, value] of Object.entries(inputs)) this.widget?.setInput(name, value);
      });
    });

    inject(DestroyRef).onDestroy(() => this.container.clear());
  }
}
