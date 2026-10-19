import { HttpErrorResponse } from '@angular/common/http';
import { computed, inject, signal } from '@angular/core';
import { form, maxLength } from '@angular/forms/signals';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import type { PictureObjectsView, VisualMatch } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import {
  catchError,
  EMPTY,
  from,
  groupBy,
  map,
  mergeMap,
  type Observable,
  of,
  pipe,
  Subject,
  switchMap,
  takeUntil,
  tap,
  throwError,
} from 'rxjs';
import { VisualSearchService } from '../../data-access/visual-search.service';
import { shrinkPicture } from '../shrink-picture';
import { type PictureGiven, VisualSearchStore } from '../visual-search.store';
import { clampBox, nudgeBox, type PictureBox, type PictureSize, scaleBox, whole } from './picture-box';

/**
 * Where the sheet is: the picture shown before it is sent, being looked at,
 * answered with its blocks, or not taken — the file refused, or too many
 * pictures in a minute.
 */
/** As long as the by-image search takes its words (its validator: 200). */
export const WordsMaxLength = 200;

export type SheetStep = 'compose' | 'looking' | 'answered' | 'refused' | 'tooMany';

/** What is sent: the whole picture, or the part the shopper marked (CS-1.2). */
export type SheetChoice = 'whole' | 'part';

/**
 * An object in the picture, its box in the picture the browser holds. One of
 * **no kind** — `kind` and `name` null — is what was sent, searched whole by
 * likeness: when nothing was found in it, or the shop cannot look (CS-2, CS-9).
 */
export interface SheetObject {
  readonly number: number;
  readonly kind: string | null;
  readonly sold: boolean;
  readonly name: string | null;
  readonly box: PictureBox;
}

/** One object's block: being asked, answered — with none, perhaps — or failed. */
export interface SheetBlock {
  readonly phase: 'looking' | 'answered' | 'failed';
  readonly matches: readonly VisualMatch[];
}

/**
 * The picture sheet (the camera search, CS-1 to CS-4, CS-9): one picture, a
 * part of it marked or not, its objects, **a block an object** and the line of
 * what the shop does not sell, narrowed with words.
 *
 * A root store, because the panel and the viewfinder give it the picture, the
 * shell draws the sheet and *See all* hands one block to the page — and it
 * reads `VisualSearchStore` for what the shop can do, never the reverse.
 *
 * **The server holds nothing between calls.** The browser keeps the picture,
 * asks for its objects, cuts each one out and asks for that crop's nearest
 * products within the object's category, all at once. A new picture, or the
 * sheet closed, abandons every request still out: each crop spends from the
 * minute's budget.
 */
export const PictureSheetStore = signalStore(
  { providedIn: 'root' },
  withState({
    isOpen: false,
    step: 'compose' as SheetStep,
    picture: null as Blob | null,
    /** The picture, as a URL only this page can read: drawn in the sheet, never sent. */
    preview: null as string | null,
    /** Its size upright, once the sheet has drawn it: the space every box here is in. */
    size: null as PictureSize | null,
    how: 'chosen' as PictureGiven,
    choice: 'whole' as SheetChoice,
    mark: null as PictureBox | null,
    /** Whether what was sent was a marked part; set when it is sent. */
    marked: false,
    objects: [] as readonly SheetObject[],
    blocks: {} as Readonly<Record<number, SheetBlock>>,
    words: '',
    /** Words were used at some point on this picture: the tracking plan counts it, never them. */
    narrowed: false,
    /** How many products the picture itself was answered with, before any words; null until every block has answered once. */
    results: null as number | null,
    counted: false,
  }),
  withProps(() => {
    /** The words typed under the blocks, as the field holds them until they are sent. */
    const narrowing = signal({ words: '' });
    return {
      narrowing,
      narrowForm: form(narrowing, (box) => maxLength(box.words, WordsMaxLength), { name: 'pictureWords' }),
      _service: inject(VisualSearchService),
      _visual: inject(VisualSearchStore),
      _culture: inject(CultureStore),
      _telemetry: inject(Telemetry),
      /** Fires when a picture is replaced or put away: everything still asked for it is abandoned. */
      _restart: new Subject<void>(),
      /** Each block's crop, kept for *See all*; never sent again. */
      _crops: new Map<number, Blob>(),
    };
  }),
  withComputed((store) => ({
    looking: computed(() => store.step() === 'looking'),
    /** The objects searched, in reading order: the shop's kinds, and the one of no kind. */
    searched: computed(() => store.objects().filter((object) => object.sold)),
    /** The names of what the picture shows and the shop does not sell (CS-1.6). */
    notSold: computed(() =>
      store
        .objects()
        .filter((object) => !object.sold)
        .map((object) => object.name ?? ''),
    ),
    /**
     * Objects of a kind the shop sells with nothing near enough, words aside:
     * *We found nothing like: lamp*. A block the words empty says so itself.
     */
    nothingLike: computed(() =>
      store.words()
        ? []
        : store
            .objects()
            .filter((object) => object.sold && object.name && store.blocks()[object.number]?.phase === 'answered')
            .filter((object) => store.blocks()[object.number].matches.length === 0)
            .map((object) => object.name ?? ''),
    ),
  })),
  withComputed((store) => ({
    /** Answered, and nothing the shop sells in it: the line alone. */
    nothingSold: computed(() => store.step() === 'answered' && store.searched().length === 0),
  })),
  withMethods((store) => {
    const fresh = {
      step: 'compose' as SheetStep,
      size: null,
      choice: 'whole' as SheetChoice,
      mark: null,
      marked: false,
      objects: [],
      blocks: {},
      words: '',
      narrowed: false,
      results: null,
      counted: false,
    };

    /** Counted once a picture, when it is put away: how it was given and what was found, never what (ADR 0036). */
    const count = () => {
      const step = store.step();
      if (store.counted() || step === 'compose' || step === 'looking') return;
      patchState(store, { counted: true });
      const blocks = Object.values(store.blocks());
      const outcome =
        step === 'refused'
          ? 'refused'
          : step === 'tooMany'
            ? 'too_many'
            : blocks.length > 0 && blocks.every((block) => block.phase === 'failed')
              ? 'down'
              : 'answered';
      store._telemetry.trackEvent('search_by_image_submitted', {
        how: store.how(),
        outcome,
        results: store.results() ?? blocks.reduce((sum, block) => sum + block.matches.length, 0),
        marked: store.marked(),
        objects: store.objects().length,
        not_sold: store.notSold().length,
        words: store.narrowed(),
      });
    };

    const letGo = () => {
      store._restart.next();
      store._crops.clear();
      const before = store.preview();
      if (before) URL.revokeObjectURL(before);
    };

    /** Every block answered once, with no words yet: what the picture itself was answered with. */
    const settle = () => {
      if (store.results() !== null || store.words()) return;
      const blocks = store.searched().map((object) => store.blocks()[object.number]);
      if (blocks.some((block) => !block || block.phase === 'looking')) return;
      patchState(store, { results: blocks.reduce((sum, block) => sum + block.matches.length, 0) });
    };

    const block = (number: number, value: SheetBlock) =>
      patchState(store, (state) => ({ blocks: { ...state.blocks, [number]: value } }));

    /**
     * One object's crop asked for its nearest products within its category, and
     * the words. Asked again for the same object — words changed, a retry — the
     * question still out is abandoned (`switchMap` within its object).
     */
    const ask = rxMethod<SheetObject>(
      pipe(
        groupBy((object) => object.number, { duration: () => store._restart }),
        mergeMap((asked) =>
          asked.pipe(
            switchMap((object) => {
              const picture = store.picture();
              const size = store.size();
              if (!picture || !size) return EMPTY;
              block(object.number, { phase: 'looking', matches: store.blocks()[object.number]?.matches ?? [] });
              const known = store._crops.get(object.number);
              return (known ? of(known) : from(shrinkPicture(picture, { box: object.box, of: size }))).pipe(
                tap((crop) => store._crops.set(object.number, crop)),
                switchMap((crop) =>
                  store._service.byPicture(crop, store._culture.active(), {
                    category: object.kind,
                    words: store.words() || null,
                  }),
                ),
                tap((answer) => {
                  block(object.number, { phase: 'answered', matches: answer.items });
                  settle();
                }),
                catchError((failure: unknown) => {
                  const status = failure instanceof HttpErrorResponse ? failure.status : 0;
                  // The picture itself, sent whole because nothing found objects in it: the
                  // sheet says it is refused (`PictureNotTaken`), not that one block failed.
                  if (object.kind === null && (status === 400 || status === 413 || status === 415))
                    patchState(store, { step: 'refused' });
                  else block(object.number, { phase: 'failed', matches: [] });
                  return EMPTY;
                }),
                takeUntil(store._restart),
              );
            }),
          ),
        ),
      ),
    );

    /** What the detector read in what was sent, said over the picture the browser holds. */
    const placed = (view: PictureObjectsView, sent: PictureBox): SheetObject[] =>
      view.objects.map((object) => {
        const box = scaleBox(
          {
            left: object.box.left ?? 0,
            top: object.box.top ?? 0,
            width: object.box.width ?? 0,
            height: object.box.height ?? 0,
          },
          { width: view.width, height: view.height },
          sent,
        );
        return {
          number: object.number,
          kind: object.kind,
          sold: object.sold,
          name: object.name,
          box: { ...box, left: sent.left + box.left, top: sent.top + box.top },
        };
      });

    /** What was sent, as one object of no kind: searched whole, by likeness. */
    const one = (sent: PictureBox): SheetObject[] => [{ number: 1, kind: null, sold: true, name: null, box: sent }];

    /**
     * The objects in what was sent. Without the detector, or with it not
     * answering, there are none to find, and what was sent is one object
     * (invariant 8): the picture is still answered, by likeness. Only the
     * picture's own refusals stop the sheet.
     */
    const find = (sent: Blob, region: PictureBox): Observable<SheetObject[]> =>
      store._visual.objects()
        ? store._service.objects(sent, store._culture.active()).pipe(
            map((view) => placed(view, region)),
            map((objects) => (objects.length > 0 ? objects : one(region))),
            catchError((failure: unknown) => {
              const status = failure instanceof HttpErrorResponse ? failure.status : 0;
              return status === 400 || status === 413 || status === 415 || status === 429
                ? throwError(() => failure)
                : of(one(region));
            }),
          )
        : of(one(region));

    /** Words that narrow every block within its category (CS-4); empty, the blocks are the picture's alone again. */
    const narrow = (words: string): void => {
      const trimmed = words.trim();
      if (trimmed === store.words()) return;
      patchState(store, { words: trimmed, narrowed: store.narrowed() || trimmed !== '' });
      for (const object of store.searched()) ask(object);
    };

    /** The whole picture, or a part: marking starts from a box over the middle of it. */
    const choose = (choice: SheetChoice): void => {
      const size = store.size();
      const mark =
        store.mark() ??
        (size
          ? clampBox(
              { left: size.width / 4, top: size.height / 4, width: size.width / 2, height: size.height / 2 },
              size,
            )
          : null);
      patchState(store, { choice, mark: choice === 'part' ? mark : store.mark() });
    };

    /** Put away: counted, every request still out abandoned, the picture let go. */
    const close = () => {
      count();
      letGo();
      patchState(store, { ...fresh, isOpen: false, picture: null, preview: null });
    };

    return {
      /** A picture given — chosen, dropped, pasted or taken — shown, to be sent whole or a part of it. */
      start(picture: Blob, how: PictureGiven): void {
        count();
        letGo();
        store.narrowing.set({ words: '' });
        patchState(store, { ...fresh, isOpen: true, picture, how, preview: URL.createObjectURL(picture) });
      },

      /** The sheet drew the picture, and so knows its size upright. */
      loaded(size: PictureSize): void {
        patchState(store, { size });
      },

      choose,

      /**
       * Back to the picture, to mark a part of it after seeing the answer: what
       * was asked for the last answer is abandoned, and the picture is the same
       * one — counted once, when the sheet is put away.
       */
      reopen(): void {
        store._restart.next();
        store._crops.clear();
        store.narrowing.set({ words: '' });
        patchState(store, { step: 'compose', objects: [], blocks: {}, words: '', results: null });
        choose('part');
      },

      /** The part drawn, moved or resized with a pointer: kept inside the picture. */
      markWith(box: PictureBox): void {
        const size = store.size();
        if (size) patchState(store, { choice: 'part', mark: clampBox(box, size) });
      },

      /** The part moved with an arrow, or resized with Shift and an arrow (CS-8). */
      nudge(key: string, resize: boolean): void {
        const size = store.size();
        const mark = store.mark();
        if (size && mark) patchState(store, { mark: nudgeBox(mark, key, resize, size) });
      },

      /** Send it: the objects found, then a block each, asked all at once. */
      search: rxMethod<void>(
        pipe(
          switchMap(() => {
            const picture = store.picture();
            const size = store.size();
            if (!picture || !size) return EMPTY;
            const mark = store.choice() === 'part' ? store.mark() : null;
            const marked = mark !== null;
            const region = mark ?? whole(size);
            patchState(store, { step: 'looking', marked, objects: [], blocks: {}, results: null });
            store._crops.clear();
            return from(shrinkPicture(picture, marked ? { box: region, of: size } : undefined)).pipe(
              switchMap((sent) => find(sent, region)),
              tap((objects) => {
                patchState(store, { step: 'answered', objects });
                for (const object of objects) if (object.sold) ask(object);
              }),
              catchError((failure: unknown) => {
                const status = failure instanceof HttpErrorResponse ? failure.status : 0;
                patchState(store, { step: status === 429 ? 'tooMany' : 'refused' });
                return EMPTY;
              }),
              takeUntil(store._restart),
            );
          }),
        ),
      ),

      narrow,

      /** The field's words sent: the blocks asked again with them. */
      narrowWithField(): void {
        if (store.narrowForm().valid()) narrow(store.narrowing().words);
      },

      /** The words taken away, from the field too. */
      clearWords(): void {
        store.narrowing.set({ words: '' });
        narrow('');
      },

      /** One block asked again, alone. */
      retry(number: number): void {
        const object = store.searched().find((candidate) => candidate.number === number);
        if (object) ask(object);
      },

      /** One block's whole list, on the page of its own. */
      seeAll(number: number): void {
        const answered = store.blocks()[number];
        const crop = store._crops.get(number) ?? store.picture();
        if (!answered || !crop) return;
        store._visual.show(answered.matches, crop, number);
        close();
      },

      close,
    };
  }),
);
