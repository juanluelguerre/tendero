import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

/** The crumb that leads somewhere: its words and where they go. */
export interface CrumbLink {
  readonly label: string;
  readonly commands: readonly unknown[];
}

/**
 * Where the shopper stands in the shop: *Tienda › Hogar › Cocina › Menaje*.
 *
 * The category page and the product page each wrote their own — one as a row of
 * spans, the other as a list with the separator drawn in CSS — and the two had
 * already drifted in markup while looking the same. One component, one list
 * (ADR 0049).
 *
 * `steps` is the whole branch, as the API returns it beside the name: a crumb
 * that walked parents itself would be the client keeping its own copy of the
 * tree. `start` is an optional first crumb that is a link (the shop's front).
 */
@Component({
  selector: 'storefront-crumbs',
  imports: [RouterLink],
  templateUrl: './crumbs.html',
  host: { class: 'block' },
})
export class Crumbs {
  /** The navigation landmark's name: *Ruta de navegación*. */
  readonly label = input.required<string>();
  readonly steps = input.required<readonly string[]>();
  readonly start = input<CrumbLink | null>(null);
}
