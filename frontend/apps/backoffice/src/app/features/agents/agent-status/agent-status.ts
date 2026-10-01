import { Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { AgentView } from '@tendero/shared-api';

/** Whether an agent may act: a word with its colour, never the colour alone. No state: it says the agent it is given. */
@Component({
  selector: 'backoffice-agent-status',
  imports: [TranslocoDirective],
  templateUrl: './agent-status.html',
  // Laid out by whoever places it; the tags are the shared ones (styles.css).
  host: { class: 'contents' },
})
export class AgentStatus {
  readonly agent = input.required<AgentView>();
}
