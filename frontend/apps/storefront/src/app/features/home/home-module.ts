import { Directive, ElementRef, inject, input } from '@angular/core';
import { Telemetry, type TrackedEvents } from '@tendero/shared-telemetry';

export type HomeModuleName = TrackedEvents['home_module_opened']['module'];

/**
 * Marks one module of the home page and reports when a shopper follows a link
 * inside it: `home_module_opened {module, position}` (R-8, H-6). The decision it
 * feeds is the page's order — a module nobody opens moves down or goes.
 *
 * **The position is counted when it is opened, among the modules drawn**, so a
 * band that failed or had nothing to show does not take a place: the number is
 * where the shopper actually saw it.
 */
@Directive({
  selector: '[storefrontHomeModule]',
  host: {
    'data-home-module': '',
    '(click)': 'opened($event)',
  },
})
export class HomeModule {
  readonly storefrontHomeModule = input.required<HomeModuleName>();

  private readonly telemetry = inject(Telemetry);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected opened(event: Event): void {
    if (!(event.target instanceof Element) || !event.target.closest('a')) return;

    const element = this.host.nativeElement;
    const drawn = element.parentElement ? [...element.parentElement.querySelectorAll(':scope > [data-home-module]')] : [element];
    this.telemetry.trackEvent('home_module_opened', {
      module: this.storefrontHomeModule(),
      position: drawn.indexOf(element) + 1,
    });
  }
}
