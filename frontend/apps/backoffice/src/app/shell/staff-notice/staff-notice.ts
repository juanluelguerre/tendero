import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { StaffUsage } from '@tendero/shared-telemetry';

/**
 * The notice to staff (ADR 0041): the backoffice measures which screens are opened
 * and what is done on them, never who does it, to learn whether the tools work.
 * Shown once; the person can switch it off here or, later, from the footer.
 */
@Component({
  selector: 'backoffice-staff-notice',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './staff-notice.html',
  host: { class: 'block' },
})
export class StaffNotice {
  protected readonly usage = inject(StaffUsage);
}
