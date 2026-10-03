import { NgTemplateOutlet } from '@angular/common';
import { Component, inject } from '@angular/core';
import { SidePane } from './side-pane';

/**
 * What the shell draws, for the specs of a screen that has a pane: a screen declares
 * its pane with `*backofficeSidePane` and the shell's slot draws it, so a spec that
 * mounts the screen alone mounts this beside it to find the pane.
 */
@Component({
  selector: 'backoffice-side-pane-slot-for-specs',
  imports: [NgTemplateOutlet],
  template: '<ng-container *ngTemplateOutlet="pane.template()" />',
})
export class SidePaneSlotForSpecs {
  protected readonly pane = inject(SidePane);
}
