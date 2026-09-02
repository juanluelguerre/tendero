import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { AuthStore, TenderoIdentity } from '@tendero/shared-auth';

/**
 * Un selector de identidad, no un formulario: el emisor de desarrollo firma para
 * identidades sembradas y no hay contrasenas que pedir. Pedirlas seria teatro, y
 * el dia que entre Keycloak esta pantalla se sustituye por su redireccion — no
 * se adapta.
 *
 * La pantalla vive en la app y no en shared por ADR 0010: el storefront pedira
 * email y contrasena a un comprador, y unificar las dos daria un componente con
 * una matriz de variantes peor que dos componentes.
 */
@Component({
  selector: 'backoffice-sign-in',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="sign-in" *transloco="let t">
      <h1>{{ t('signIn.title') }}</h1>
      <p class="hint">{{ t('signIn.hint') }}</p>

      @if (failed()) {
        <p class="error" role="alert">{{ t('signIn.failed') }}</p>
      }

      @if (identities().length === 0) {
        <p class="error" role="alert">{{ t('signIn.noIssuer') }}</p>
      }

      <ul class="identities">
        @for (identity of identities(); track identity.subject) {
          <li>
            <button type="button" [disabled]="busy()" (click)="choose(identity)">
              <span class="name">{{ identity.name }}</span>
              <span class="role">{{ identity.role }}</span>
            </button>
          </li>
        }
      </ul>
    </section>
  `,
  styles: `
    .sign-in {
      max-width: 28rem;
      margin: var(--space-6) auto;
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }
    h1 { font-family: var(--font-display); font-size: var(--text-lg); margin: 0; }
    .hint { color: var(--text-muted); font-size: var(--text-sm); margin: 0; }
    .error { color: var(--danger); font-size: var(--text-sm); margin: 0; }
    .identities { list-style: none; padding: 0; margin: 0; display: grid; gap: var(--space-2); }
    button {
      width: 100%;
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: var(--space-2);
      padding: var(--space-3);
      background: var(--bg-surface);
      border: 1px solid var(--border);
      border-radius: var(--radius-md);
      color: inherit;
      font: inherit;
      cursor: pointer;
    }
    button:hover:not(:disabled) { border-color: var(--accent); }
    button:disabled { opacity: 0.6; cursor: progress; }
    .name { font-weight: 600; }
    .role { font-family: var(--font-mono); font-size: var(--text-xs); color: var(--text-muted); }
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
