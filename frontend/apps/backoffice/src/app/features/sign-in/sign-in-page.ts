import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { AuthStore, TenderoIdentity } from '@tendero/shared-auth';

/**
 * An identity picker, not a form: the development issuer signs for seeded
 * identities and there are no passwords to ask for. Asking would be theatre, and
 * the day Keycloak arrives this screen is replaced by its redirect — not adapted.
 *
 * The screen lives in the app and not in shared, per ADR 0010: the storefront
 * will ask a shopper for an email and a password, and unifying the two would
 * give a component with a variant matrix worse than two components.
 */
@Component({
  selector: 'backoffice-sign-in',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stage" *transloco="let t">
      <section class="panel card">
        <img class="card__mark" src="brand/tendero-icon-backoffice.svg" alt="" aria-hidden="true"
             width="36" height="36" />
        <h1 class="card__title">{{ t('signIn.title') }}</h1>
        <p class="muted card__hint">{{ t('signIn.hint') }}</p>

        @if (failed()) {
          <p class="failed" role="alert">{{ t('signIn.failed') }}</p>
        }

        @if (identities().length === 0) {
          <p class="failed" role="alert">{{ t('signIn.noIssuer') }}</p>
        }

        <ul class="identities">
          @for (identity of identities(); track identity.subject) {
            <li>
              <button type="button" class="identity" [disabled]="busy()" (click)="choose(identity)">
                <span class="identity__name">{{ identity.name }}</span>
                <!-- The role, not a decoration: it is what decides whether the
                     publish button will work, and saying it here saves finding
                     out by pressing it. -->
                <span class="tag tag--code">{{ identity.role }}</span>
              </button>
            </li>
          }
        </ul>
      </section>
    </div>
  `,
  styles: `
    /* Centred in what is left of the viewport under the bar, not floated in the
       middle of a dark page: with no tabs and no data, a card is the only thing
       that tells you where to look. */
    .stage {
      display: grid;
      place-items: center;
      min-height: 60dvh;
      padding-block: var(--space-6);
    }

    .card {
      width: 100%;
      max-width: 26rem;
      padding: var(--space-6);
    }
    .card__mark { display: block; width: 36px; height: 36px; margin-block-end: var(--space-4); }
    .card__title { font-size: var(--text-lg); }
    .card__hint { margin-block: var(--space-2) var(--space-5); font-size: var(--text-xs); }

    .identities { list-style: none; padding: 0; margin: 0; display: grid; gap: var(--space-2); }

    .identity {
      width: 100%;
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: var(--space-3);
      padding: var(--space-3);
      background: var(--bg-page);
      border: 1px solid var(--border);
      border-radius: var(--radius-md);
      color: inherit;
      font: inherit;
      cursor: pointer;
      transition: border-color var(--dur-fast) var(--ease);
    }
    .identity:hover:not(:disabled) { border-color: var(--accent); }
    .identity:disabled { opacity: 0.6; cursor: progress; }
    .identity__name { font-weight: 600; }
  `,
})
export class SignInPage {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  protected readonly identities = signal<TenderoIdentity[]>([]);
  protected readonly busy = signal(false);
  protected readonly failed = signal(false);

  constructor() {
    void this.auth.identities().then((found) => this.identities.set(found));
  }

  protected async choose(identity: TenderoIdentity): Promise<void> {
    this.busy.set(true);
    this.failed.set(false);
    try {
      await this.auth.signIn(identity);
      await this.router.navigate(['/review']);
    } catch {
      this.failed.set(true);
    } finally {
      this.busy.set(false);
    }
  }
}
