import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { parseMarkdown } from './markdown';

/**
 * A steward answer drawn from its Markdown: bold, italic, code, lists and tables.
 * The text is parsed to data (`markdown.ts`) and drawn by this template with
 * Angular's own escaping, so a model cannot put markup in the page.
 */
@Component({
  selector: 'backoffice-steward-markdown',
  imports: [NgTemplateOutlet],
  templateUrl: './steward-markdown.html',
  host: { class: 'block' },
})
export class StewardMarkdown {
  readonly text = input.required<string>();

  protected readonly blocks = computed(() => parseMarkdown(this.text()));
}
