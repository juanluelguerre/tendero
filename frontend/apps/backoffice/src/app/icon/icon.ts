import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * The backoffice's line icons, drawn here rather than installed: a dozen
 * strokes on a 24-unit grid do not earn a dependency, and the storefront
 * already draws its account and basket icons the same way. Each is one path,
 * stroked in `currentColor`, so an icon takes the colour of the text beside it
 * and never carries meaning alone: every use has a label next to it or as its
 * accessible name.
 */
const Paths = {
  dashboard: 'M4 4h7v7H4z M13 4h7v4h-7z M13 10h7v10h-7z M4 13h7v7H4z',
  review: 'M4 13l2.5-8h11L20 13v6H4z M4 13h5l1 2h4l1-2h5',
  products: 'M12 3l8 4.5v9L12 21l-8-4.5v-9z M4 7.5l8 4.5 8-4.5 M12 12v9',
  claims: 'M12 3v5 M12 16v5 M3 12h5 M16 12h5 M6.5 6.5l2.5 2.5 M15 15l2.5 2.5 M6.5 17.5L9 15 M15 9l2.5-2.5',
  attributes: 'M3 12V4h8l10 10-8 8z M7.5 7.5h.01',
  orders: 'M6 3h12v18l-3-2-3 2-3-2-3 2z M9 8h6 M9 12h6',
  returns: 'M9 14L4 9l5-5 M4 9h11a5 5 0 0 1 0 10h-3',
  promotions: 'M19 5L5 19 M7 5a2 2 0 1 0 0 4a2 2 0 1 0 0-4 M17 15a2 2 0 1 0 0 4a2 2 0 1 0 0-4',
  campaigns: 'M4 10v4h3l9 5V5l-9 5z M7 14l1.5 6h2.5l-1.2-5 M19 9.5a3 3 0 0 1 0 5',
  stock: 'M3 10h18v10H3z M7 10V5h10v5 M3 15h18',
  agents: 'M5 9h14v10H5z M12 5v4 M9 14h.01 M15 14h.01 M12 3a1 1 0 1 0 0 2a1 1 0 1 0 0-2',
  audit: 'M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z M9 12l2 2 4-4',
  search: 'M4 16a8 8 0 1 1 16 0 M12 16l4-5',
  menu: 'M4 6h16 M4 12h16 M4 18h16',
  close: 'M6 6l12 12 M18 6L6 18',
  chevron: 'M6 9l6 6 6-6',
  system: 'M3 5h18v11H3z M8 20h8 M12 16v4',
  light: 'M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8 M12 2v2 M12 20v2 M2 12h2 M20 12h2 M5 5l1.5 1.5 M17.5 17.5L19 19 M5 19l1.5-1.5 M17.5 6.5L19 5',
  dark: 'M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z',
} as const;

export type IconName = keyof typeof Paths;

@Component({
  selector: 'backoffice-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './icon.html',
  styleUrl: './icon.css',
})
export class Icon {
  readonly name = input.required<IconName>();
  protected readonly path = computed(() => Paths[this.name()]);
}
