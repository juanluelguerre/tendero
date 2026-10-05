import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { Composer, ConversationScroll, Icon, Notice } from '@tendero/shared-ui';
import { filter } from 'rxjs';
import { WaitingStore } from '../../../shell/waiting.store';
import { ProposalCard } from '../proposal-card/proposal-card';
import { ProposalReview } from '../proposal-review/proposal-review';
import { screenOf } from '../screen-context';
import { StewardDoorStore } from '../steward-door.store';
import { StewardStore } from '../steward.store';
import { CostLinePipe } from './cost-line.pipe';
import { StewardMarkdown } from './steward-markdown/steward-markdown';
import { StewardTable } from './steward-table/steward-table';
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
    Composer,
    ConversationScroll,
    ProposalCard,
    ProposalReview,
    CostLinePipe,
    StewardWait,
    StewardMarkdown,
    StewardTable,
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

  protected ask(question: string): void {
    this.store.ask(question, this.screen());
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
