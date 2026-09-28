import { inject, Injectable } from '@angular/core';
import { Meta } from '@angular/platform-browser';

/**
 * The page's `<meta name="robots">`: absent, which a crawler reads as "index,
 * follow", or `noindex,follow` on a page that should not be a search result of
 * its own but whose links should still be walked.
 *
 * Absent is the default and the one to go back to: a page that leaves
 * `noindex` behind when the shopper moves on hides the next page from Google,
 * and nothing on screen would ever say so.
 */
@Injectable({ providedIn: 'root' })
export class RobotsMeta {
  private readonly meta = inject(Meta);

  noindex(): void {
    this.meta.updateTag({ name: 'robots', content: 'noindex,follow' });
  }

  clear(): void {
    this.meta.removeTag("name='robots'");
  }
}
