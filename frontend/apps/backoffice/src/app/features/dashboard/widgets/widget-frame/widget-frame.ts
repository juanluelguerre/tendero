import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, contentChild, input, type Resource, TemplateRef, ViewEncapsulation } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * The frame every dashboard widget sits in (R-9; ADR 0049, point 4): the panel,
 * the head with its title and the way to the list behind it, and the read's
 * loading and failed states — written once, where seventeen widgets each wrote
 * it. The figures and the chart are the widget's, in an `<ng-template>`, drawn
 * only once the read holds something: a resource that failed throws when its
 * value is read, so what reads it must not run before the frame has said so.
 *
 * Its stylesheet is not scoped (`ViewEncapsulation.None`): it holds what the
 * dashboard's widgets share — the size contract, the figures and the ranking —
 * and a widget's elements carry its own scope, not this one's. It loads with
 * the dashboard, and sits in the `components` layer, so a utility still wins.
 */
@Component({
  selector: 'backoffice-widget-frame',
  imports: [RouterLink, NgTemplateOutlet],
  encapsulation: ViewEncapsulation.None,
  templateUrl: './widget-frame.html',
  styleUrl: './widget-frame.css',
})
export class WidgetFrame {
  readonly heading = input.required<string>();
  /** The list behind the widget, opened from its head. */
  readonly link = input<string | null>(null);
  readonly linkText = input('');

  /** The read the widget shows: loading until it first holds a value, failed when it errs. */
  readonly resource = input<Resource<unknown> | null>(null);
  /** For a widget that reads more than one resource and says their state itself. */
  readonly loading = input(false);
  readonly failed = input(false);
  readonly loadingText = input('');
  readonly failedText = input('');

  protected readonly body = contentChild.required(TemplateRef);

  protected readonly isLoading = computed(() => {
    const read = this.resource();
    return this.loading() || (read !== null && read.isLoading() && !read.hasValue());
  });

  protected readonly isFailed = computed(() => this.failed() || !!this.resource()?.error());
}
