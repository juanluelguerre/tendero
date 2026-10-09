import { NgOptimizedImage, PercentPipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { ProductImageUrls } from '@tendero/shared-util';
import { ListState } from '../../worklist/list-state/list-state';
import { Pager } from '../../worklist/pager/pager';
import { PictureDoubtsStore } from './picture-doubts.store';

/**
 * Pictures the model is sure show another colour than the catalogue holds
 * (phase 13a, I-4d). Each row is the picture itself beside what was seen and
 * what is held, and the three things a person can say of it. **A doubt is not a
 * claim**: no decision here changes the catalogue — replacing a picture or
 * filing it under another variant is done by whoever decided — and a doubt
 * blocks nothing while it waits. Every decision is quiet: none of the three is
 * the one a person should be led to.
 */
@Component({
  selector: 'backoffice-picture-doubts-page',
  imports: [TranslocoDirective, NgOptimizedImage, PercentPipe, ListState, Pager],
  templateUrl: './picture-doubts-page.html',
  host: { class: 'block' },
  providers: [PictureDoubtsStore],
})
export class PictureDoubtsPage {
  protected readonly store = inject(PictureDoubtsStore);
  protected readonly images = inject(ProductImageUrls);

  protected readonly pageSize = this.store.pageSize;
}
