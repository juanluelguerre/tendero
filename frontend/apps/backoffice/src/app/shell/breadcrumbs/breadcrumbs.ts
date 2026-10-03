import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { filter, map } from 'rxjs';
import { crumbsOf } from '../nav/navigation';

/**
 * Where the person is, at the left of the header (the prototype's): the group and
 * the screen, read from the navigation model the rail is drawn from. A group that
 * carries the same word as its only screen (Stock / Stock) says it once.
 */
@Component({
  selector: 'backoffice-breadcrumbs',
  imports: [TranslocoDirective],
  templateUrl: './breadcrumbs.html',
  host: { class: 'block min-w-0' },
})
export class Breadcrumbs {
  private readonly router = inject(Router);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  protected readonly crumbs = computed(() => crumbsOf(this.url()));
}
