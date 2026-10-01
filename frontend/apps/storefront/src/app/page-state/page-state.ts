import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type PageStateTone = 'info' | 'bad';

/**
 * What a page shows in place of its content when it has none to show: nothing
 * matched, the link is wrong, the engine did not answer. A titled panel, centred,
 * in one shape across the shop (ADR 0049); the words below the title and any way
 * out are the caller's.
 *
 * An empty result and a broken engine are different events and must not look
 * the same: `info` is the shop saying "not here", `bad` is the shop saying "ask
 * me again" — and the title says which, so the colour never carries it alone
 * (design/DESIGN.md).
 *
 * `heading` rather than `title`, because `title` on the host would also be the
 * element's tooltip. `level` is the page's outline: a page this replaces
 * entirely names itself with it (1), one that keeps its own heading does not (2).
 * Whether it interrupts is the caller's too: `role="alert"` on the host.
 */
@Component({
  selector: 'storefront-page-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './page-state.html',
  // Its margins are its place's: whoever puts it on a page gives it room.
  host: { class: 'block' },
})
export class PageState {
  readonly heading = input<string>();
  readonly tone = input<PageStateTone>('info');
  readonly level = input<1 | 2>(2);
}
