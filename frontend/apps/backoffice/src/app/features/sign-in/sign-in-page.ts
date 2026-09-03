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
  templateUrl: './sign-in-page.html',
  styleUrl: './sign-in-page.css',
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
