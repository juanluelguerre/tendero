import { Component, computed, input } from '@angular/core';

/**
 * Both apps' line icons, drawn here rather than installed: a few dozen strokes
 * on a 24-unit grid do not earn a dependency. Each is one path, stroked in
 * `currentColor`, so an icon takes the colour of the text beside it and never
 * carries meaning alone: every use has a label next to it or as its accessible
 * name. Circles and rounded boxes are written as arcs, so one path is enough.
 *
 * Its size is the caller's (`class="size-[22px]"`; 1.125rem when nothing says),
 * and so is its weight: most are drawn at 1.6, the shop's chevrons at 1.8, its
 * small marks at 2.
 */
const Paths = {
  // ---------- The backoffice's screens
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
  /** A picture to search with: a camera, its lens a circle written as two arcs. */
  camera: 'M4 8h3.5l1.5-2h6l1.5 2H20v11H4z M12 10.5a3 3 0 1 0 0 6a3 3 0 1 0 0-6',
  /** A question spoken instead of typed (phase 14): a capsule on its stand. */
  microphone: 'M12 3a3 3 0 0 0-3 3v5a3 3 0 0 0 6 0V6a3 3 0 0 0-3-3z M6 11a6 6 0 0 0 12 0 M12 17v3 M9 20h6',
  /** What Steward proposed: a list with its ticks. */
  proposals: 'M4 7l1.5 1.5L8 6 M4 13l1.5 1.5L8 12 M11 7h9 M11 13h9 M11 19h9 M4 19h4',

  // ---------- Steward (phase 12)
  /** Steward itself: a spark and its small one, the mark agent-driven things wear. */
  steward: 'M10 4l1.6 4.4L16 10l-4.4 1.6L10 16l-1.6-4.4L4 10l4.4-1.6z M18 3v4 M16 5h4',
  /** What a turn read: the shop's records. */
  records:
    'M5 6c0-1.7 3.1-3 7-3s7 1.3 7 3-3.1 3-7 3-7-1.3-7-3z M5 6v12c0 1.7 3.1 3 7 3s7-1.3 7-3V6 M5 12c0 1.7 3.1 3 7 3s7-1.3 7-3',
  /** The same answer, as a filter on a list. */
  filter: 'M4 5h16l-6 7.5V19l-4-2v-4.5z',
  /** Send the question. */
  send: 'M12 19V5 M6 11l6-6 6 6',
  /** Stop the turn on its way: a player's square, and the one icon that is filled (`Filled`). */
  stop: 'M8.5 7h7a1.5 1.5 0 0 1 1.5 1.5v7a1.5 1.5 0 0 1-1.5 1.5h-7A1.5 1.5 0 0 1 7 15.5v-7A1.5 1.5 0 0 1 8.5 7z',
  /** No model answered. */
  unavailable: 'M4 4l16 16 M9.5 5.5A6 6 0 0 1 18 11a4 4 0 0 1 1.5 7.6 M16 19H7a4 4 0 0 1-1.3-7.8',

  // ---------- Both apps' controls
  menu: 'M4 6h16 M4 12h16 M4 18h16',
  close: 'M6 6l12 12 M18 6L6 18',
  /** A smaller cross, for removing one thing from a row of them (the listing's chips). */
  remove: 'M7 7l10 10 M17 7L7 17',
  /** Points down; turned half round (`rotate-180`) it points up. */
  chevron: 'M6 9l6 6 6-6',

  // ---------- The theme: the storefront's drawing, which the backoffice now shares
  system:
    'M5 4.5h14a1.5 1.5 0 0 1 1.5 1.5v8.5a1.5 1.5 0 0 1-1.5 1.5H5a1.5 1.5 0 0 1-1.5-1.5V6A1.5 1.5 0 0 1 5 4.5z M9 20h6 M12 16v4',
  light:
    'M8 12a4 4 0 1 0 8 0a4 4 0 1 0-8 0 M12 2.5v2.5 M12 19v2.5 M2.5 12H5 M19 12h2.5 M5.3 5.3l1.8 1.8 M16.9 16.9l1.8 1.8 M5.3 18.7l1.8-1.8 M16.9 7.1l1.8-1.8',
  dark: 'M19.5 14.5A8 8 0 0 1 9.5 4.5a8 8 0 1 0 10 10z',

  // ---------- The shop's promises and its agents
  shipping:
    'M3 7h11v9H3z M14 10h4l3 3v3h-7 M5.5 17.5a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0-3 0 M15.5 17.5a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0-3 0',
  /** Coming back: a return within its days. */
  reload: 'M4 12a8 8 0 1 0 2.3-5.7 M4 4v4h4',
  /** Coming back, with the clock's hands: what was seen before. */
  history: 'M4 12a8 8 0 1 0 2.3-5.7 M4 4v4h4 M12 8v4l3 2',
  lock: 'M7 11h10a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2z M8 11V8a4 4 0 0 1 8 0v3',
  /** An agent, the shop's: the backoffice's register draws its own (`agents`). */
  bot: 'M8 8h8a3 3 0 0 1 3 3v5a3 3 0 0 1-3 3H8a3 3 0 0 1-3-3v-5a3 3 0 0 1 3-3z M12 4v4 M9 13h.01 M15 13h.01 M9 16h6',
} as const;

export type IconName = keyof typeof Paths;

/**
 * The icons that are a solid shape and not a line. Stop is: as an outline its
 * square reads as an empty box, and a square in a ring as nothing in particular;
 * solid, it is the stop of every player and every assistant.
 */
const Filled: ReadonlySet<IconName> = new Set<IconName>(['stop']);

@Component({
  selector: 'lib-icon',
  templateUrl: './icon.html',
  styleUrl: './icon.css',
})
export class Icon {
  readonly name = input.required<IconName>();
  /** The line's weight, in the grid's units. */
  readonly stroke = input(1.6);
  protected readonly path = computed(() => Paths[this.name()]);
  protected readonly fill = computed(() => (Filled.has(this.name()) ? 'currentColor' : 'none'));
}
