import {
  afterNextRender,
  Component,
  computed,
  effect,
  type ElementRef,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { Icon, Notice } from '@tendero/shared-ui';
import { filter } from 'rxjs';
import { WaitingStore } from '../../../shell/waiting.store';
import { ProposalCard } from '../proposal-card/proposal-card';
import { ProposalReview } from '../proposal-review/proposal-review';
import { screenOf } from '../screen-context';
import { StewardDoorStore } from '../steward-door.store';
import { StewardStore } from '../steward.store';
import { CostLinePipe } from './cost-line.pipe';
import { StewardMarkdown } from './steward-markdown/steward-markdown';
import { StewardWait } from './steward-wait/steward-wait';

/**
 * Steward's panel (phase 12; spec, *The panel*): beside the content from 1024 px,
 * a full-screen sheet below. Its head names the screen it was opened on, which
 * each question carries; each answer shows what it read, its words, the
 * proposals as a card and what the conversation has cost; and the composer
 * carries the promise — Steward reads and proposes, nothing changes until a
 * person approves. When no model answers it says so, and offers the same answer
 * without AI and the Proposals list: the rest of the backoffice is untouched
 * (invariant 8).
 */
@Component({
  selector: 'backoffice-steward-panel',
  imports: [
    TranslocoDirective,
    RouterLink,
    Icon,
    Notice,
    ProposalCard,
    ProposalReview,
    CostLinePipe,
    StewardWait,
    StewardMarkdown,
  ],
  templateUrl: './steward-panel.html',
  host: { class: 'block' },
})
export class StewardPanel {
  protected readonly store = inject(StewardStore);
  protected readonly door = inject(StewardDoorStore);
  protected readonly culture = inject(CultureStore);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslocoService);
  private readonly waiting = inject(WaitingStore);

  /** The question while it is typed. */
  protected readonly draft = signal('');

  /** How many times the review decided something: every card reads its batch again. */
  protected readonly revision = signal(0);

  private readonly navigated = toSignal(this.router.events.pipe(filter((event) => event instanceof NavigationEnd)));

  /** The screen the panel is on, in the person's language: sent with every question. */
  protected readonly screen = computed(() => {
    this.navigated();
    this.culture.active();
    return screenOf(this.router.routerState.snapshot.root, (key) => this.translate.translate(key));
  });

  protected readonly screenLabel = computed(() => {
    const screen = this.screen();
    return screen ? [screen.page, screen.subject].filter(Boolean).join(' · ') : null;
  });

  private readonly review = viewChild.required(ProposalReview);
  private readonly composer = viewChild<ElementRef<HTMLTextAreaElement>>('composer');
  private readonly conversation = viewChild<ElementRef<HTMLElement>>('conversation');

  constructor() {
    // The panel opens to be asked: the composer has the focus.
    afterNextRender(() => this.composer()?.nativeElement.focus());

    // The newest question at the top of the view as it is asked (the DOM's, so an
    // effect): its answer, and a card that loads after it, grow below it in sight.
    // Scrolling to the bottom instead left a card that arrived later under the fold.
    // The conversation's own scroll, never scrollIntoView: that scrolls every
    // ancestor, the page included, and left the backoffice scrolled to its end.
    effect(() => {
      const asked = this.store.exchanges().length;
      const element = this.conversation()?.nativeElement;
      if (!element || asked === 0) return;
      queueMicrotask(() => {
        const questions = element.querySelectorAll('[data-question]');
        const last = questions[questions.length - 1];
        if (!last) return;
        const padding = Number.parseFloat(getComputedStyle(element).paddingTop) || 0;
        element.scrollTop += last.getBoundingClientRect().top - element.getBoundingClientRect().top - padding;
      });
    });

    // The composer is for the next question: once an answer is in, it has the focus
    // again unless the person went somewhere else (the review's dialog has its own).
    effect(() => {
      if (this.store.busy()) return;
      const composer = this.composer()?.nativeElement;
      untracked(() => {
        const active = document.activeElement;
        if (composer && (!active || active === document.body)) composer.focus();
      });
    });
  }

  protected send(): void {
    if (!this.draft().trim() || this.store.busy()) return;
    this.store.ask(this.draft(), this.screen());
    this.draft.set('');
  }

  /** Enter sends; Shift+Enter is a new line. */
  protected keyed(event: KeyboardEvent): void {
    if (event.key !== 'Enter' || event.shiftKey) return;
    event.preventDefault();
    this.send();
  }

  protected again(question: string): void {
    this.store.ask(question, this.screen());
  }

  protected openReview(ids: readonly string[]): void {
    this.review().open(ids);
  }

  protected decided(): void {
    this.revision.update((count) => count + 1);
    this.waiting.refresh();
  }
}
