import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { AgentView } from '@tendero/shared-api';

/** Whether an agent may act: a word with its colour, never the colour alone. No state: it says the agent it is given. */
@Component({
  selector: 'backoffice-agent-status',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './agent-status.html',
  styleUrl: './agent-status.css',
})
export class AgentStatus {
  readonly agent = input.required<AgentView>();
}
