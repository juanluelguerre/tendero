import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { AgentsStore, AgentTrusts } from '../agents.store';

/**
 * Vouching for a new agent — the view's one clay action. The draft lives in the
 * page's store, which clears it and reads the registry again once the server
 * has registered it.
 */
@Component({
  selector: 'backoffice-agent-register',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './agent-register.html',
  styleUrl: './agent-register.css',
})
export class AgentRegister {
  protected readonly store = inject(AgentsStore);
  protected readonly trusts = AgentTrusts;
}
