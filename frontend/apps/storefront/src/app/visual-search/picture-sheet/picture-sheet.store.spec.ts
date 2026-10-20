import { ApplicationRef, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, type TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { PictureObjectView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { VisualSearchStore } from '../visual-search.store';
import { PictureSheetStore } from './picture-sheet.store';

const Status = 'http://api/api/search/by-image/status';
const Objects = 'http://api/api/search/by-image/objects';
const ByImage = 'http://api/api/search/by-image';

const match = (code: string) => ({
  product: { productId: `p-${code}`, code },
  picture: `img-${code}`,
  similarity: 0.6,
});

/** The author's photograph with a backpack, as the detector read it from a copy of 600 × 800. */
const read = (...objects: PictureObjectView[]) => ({ width: 600, height: 800, objects });
const backpack: PictureObjectView = {
  number: 1,
  kind: 'BACKPACK',
  sold: true,
  name: 'Mochilas',
  box: { left: 30, top: 110, width: 230, height: 530 },
  score: 0.71,
};
const tshirt: PictureObjectView = {
  number: 2,
  kind: 'SHIRT',
  sold: true,
  name: 'Camisetas',
  box: { left: 120, top: 0, width: 280, height: 530 },
  score: 0.55,
};
const watch: PictureObjectView = {
  number: 3,
  kind: 'watch',
  sold: false,
  name: 'reloj',
  box: { left: 365, top: 432, width: 45, height: 40 },
  score: 0.48,
};

/**
 * The picture sheet (the camera search, CS-1 to CS-4): a picture given, a part
 * marked or not, its objects found, a block an object, the line of what is not
 * sold, the words that narrow, and every way a part of it can be missing (CS-9).
 */
describe('the picture sheet', () => {
  let http: HttpTestingController;
  const telemetry = { trackEvent: vi.fn() };
  let store: InstanceType<typeof PictureSheetStore>;
  const picture = new Blob(['a picture'], { type: 'image/jpeg' });

  beforeEach(() => {
    telemetry.trackEvent.mockClear();
    let made = 0;
    // jsdom has neither: the address is the browser's own, and so is taking it back.
    URL.createObjectURL = vi.fn(() => `blob:${++made}`);
    URL.revokeObjectURL = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', children: [] }]),
        { provide: API_BASE_URL, useValue: 'http://api' },
        { provide: Telemetry, useValue: telemetry },
        { provide: CultureStore, useValue: { active: signal('es') } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(PictureSheetStore);
  });

  afterEach(() => {
    http.match(Status);
    http.verify();
  });

  /** The shop says what it can do with a picture. */
  async function shopCan(objects: boolean) {
    TestBed.inject(VisualSearchStore).available();
    TestBed.tick();
    http.expectOne(Status).flush({ available: true, objects });
    await TestBed.inject(ApplicationRef).whenStable();
  }

  /** A picture given and shown, its size known, as the sheet's picture reports it once drawn. */
  function given(how: 'chosen' | 'viewfinder' = 'chosen') {
    store.start(picture, how);
    store.loaded({ width: 1200, height: 1600 });
  }

  /** The requests to one address, once the browser has had its turn at making the picture smaller. */
  async function asked(url: string, count = 1) {
    let found: TestRequest[] = [];
    await vi.waitFor(() => {
      found = http.match((request) => request.url === url);
      expect(found.length).toBe(count);
    });
    return found;
  }

  const block = (number: number) => store.blocks()[number];

  it('opens on the picture given, to search it whole or mark a part, and lets go of the last picture', () => {
    given();
    expect([store.isOpen(), store.step(), store.preview(), store.choice()]).toEqual([
      true,
      'compose',
      'blob:1',
      'whole',
    ]);

    store.start(picture, 'pasted');
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:1');
    expect(store.preview()).toBe('blob:2');
  });

  it('finds the objects, then searches each one the shop sells within its category, and names the rest', async () => {
    await shopCan(true);
    given();

    store.search();
    const [objects] = await asked(Objects);
    expect(objects.request.params.get('culture')).toBe('es');
    expect((objects.request.body as FormData).get('picture')).toBeInstanceOf(Blob);
    objects.flush(read(backpack, tshirt, watch));

    const crops = await asked(ByImage, 2);
    expect(crops.map((crop) => crop.request.params.get('category'))).toEqual(['BACKPACK', 'SHIRT']);
    expect(crops.every((crop) => crop.request.params.has('q'))).toBe(false);
    expect(store.step()).toBe('answered');
    expect(block(1).phase).toBe('looking');

    crops[0].flush({ items: [match('OFICINA'), match('CITYPACK')] });
    crops[1].flush({ items: [match('DIARIO')] });

    expect(store.searched().map((object) => object.number)).toEqual([1, 2]);
    expect([block(1).phase, block(1).matches.length, block(2).matches.length]).toEqual(['answered', 2, 1]);
    expect(store.notSold()).toEqual(['reloj']);
  });

  it('draws the objects over the picture it holds, not the smaller copy the server read', async () => {
    await shopCan(true);
    given();

    store.search();
    (await asked(Objects))[0].flush(read(backpack));
    (await asked(ByImage))[0].flush({ items: [] });

    expect(store.objects()[0].box).toEqual({ left: 60, top: 220, width: 460, height: 1060 });
  });

  it('a marked part is what is sent, and the objects found in it are drawn where they are in the picture', async () => {
    await shopCan(true);
    given();

    store.choose('part');
    store.markWith({ left: 100, top: 200, width: 600, height: 800 });
    store.search();
    // The part was sent at 300 × 400: everything the server read is inside it.
    (await asked(Objects))[0].flush({
      ...read({ ...backpack, box: { left: 30, top: 40, width: 150, height: 200 } }),
      width: 300,
      height: 400,
    });
    (await asked(ByImage))[0].flush({ items: [match('OFICINA')] });

    expect(store.marked()).toBe(true);
    expect(store.objects()[0].box).toEqual({ left: 160, top: 280, width: 300, height: 400 });
  });

  it('marking starts from a box in the middle of the picture, and the box stays inside it', () => {
    given();

    store.choose('part');
    expect(store.mark()).toEqual({ left: 300, top: 400, width: 600, height: 800 });

    store.markWith({ left: 1100, top: -50, width: 400, height: 400 });
    expect(store.mark()).toEqual({ left: 800, top: 0, width: 400, height: 400 });

    store.nudge('ArrowLeft', false);
    expect(store.mark()?.left).toBe(800 - 24);
  });

  it('goes back to the picture to mark a part after the answer, abandoning what was still asked', async () => {
    await shopCan(true);
    given();
    store.search();
    (await asked(Objects))[0].flush(read(backpack, tshirt));
    const crops = await asked(ByImage, 2);

    store.reopen();

    expect(crops.every((crop) => crop.cancelled)).toBe(true);
    expect([store.step(), store.choice(), store.objects(), store.preview()]).toEqual(['compose', 'part', [], 'blob:1']);
    expect(store.mark()).not.toBeNull();
  });

  describe('when a part is missing', () => {
    it('without the detector, what was sent is one object of no kind, searched by likeness', async () => {
      await shopCan(false);
      given();

      store.search();
      const [crop] = await asked(ByImage);
      expect(crop.request.params.has('category')).toBe(false);
      crop.flush({ items: [match('OFICINA')] });
      http.expectNone(Objects);

      expect(store.objects()).toEqual([
        { number: 1, kind: null, sold: true, name: null, box: { left: 0, top: 0, width: 1200, height: 1600 } },
      ]);
      expect(block(1).matches.length).toBe(1);
    });

    it('without the detector, a picture the search refuses is refused, not a block that failed', async () => {
      await shopCan(false);
      given();

      store.search();
      (await asked(ByImage))[0].flush({ code: 'PictureNotTaken' }, { status: 400, statusText: 'Bad Request' });

      expect(store.step()).toBe('refused');
      store.close();
      expect(telemetry.trackEvent).toHaveBeenCalledWith(
        'search_by_image_submitted',
        expect.objectContaining({ outcome: 'refused' }),
      );
    });

    it('a detector that does not answer is the same: one object, searched whole', async () => {
      await shopCan(true);
      given();

      store.search();
      (await asked(Objects))[0].flush({ code: 'ObjectsNotAnswering' }, { status: 503, statusText: 'Unavailable' });
      (await asked(ByImage))[0].flush({ items: [] });

      expect(store.objects().map((object) => object.kind)).toEqual([null]);
    });

    it('with no object found at all, what was sent is one object too (CS-2)', async () => {
      await shopCan(true);
      given();

      store.search();
      (await asked(Objects))[0].flush(read());
      (await asked(ByImage))[0].flush({ items: [match('OFICINA')] });

      expect(store.objects().map((object) => object.kind)).toEqual([null]);
    });

    it('with only objects the shop does not sell, nothing is searched and the line is all there is', async () => {
      await shopCan(true);
      given();

      store.search();
      (await asked(Objects))[0].flush(read(watch));

      http.expectNone(ByImage);
      expect([store.step(), store.searched().length, store.notSold(), store.nothingSold()]).toEqual([
        'answered',
        0,
        ['reloj'],
        true,
      ]);
    });

    it.each([
      [400, 'refused', 'refused'],
      [413, 'refused', 'refused'],
      [429, 'tooMany', 'too_many'],
    ] as const)(
      'a %i on the picture says %s in the sheet, where another can be given',
      async (status, step, outcome) => {
        await shopCan(true);
        given();

        store.search();
        (await asked(Objects))[0].flush({ code: 'PictureNotTaken' }, { status, statusText: 'No' });

        expect(store.step()).toBe(step);
        store.close();
        expect(telemetry.trackEvent).toHaveBeenCalledWith(
          'search_by_image_submitted',
          expect.objectContaining({ how: 'chosen', outcome, results: 0 }),
        );
      },
    );

    it('a block whose search fails says so, the others stand, and only it is asked again', async () => {
      await shopCan(true);
      given();

      store.search();
      (await asked(Objects))[0].flush(read(backpack, tshirt));
      const [first, second] = await asked(ByImage, 2);
      first.flush({ items: [match('OFICINA')] });
      second.flush('down', { status: 500, statusText: 'Down' });

      expect([block(1).phase, block(2).phase]).toEqual(['answered', 'failed']);

      store.retry(2);
      const [again] = await asked(ByImage);
      expect(again.request.params.get('category')).toBe('SHIRT');
      again.flush({ items: [match('DIARIO')] });
      expect(block(2).phase).toBe('answered');
    });
  });

  describe('narrowed with words', () => {
    async function answered() {
      await shopCan(true);
      given();
      store.search();
      (await asked(Objects))[0].flush(read(backpack, tshirt, watch));
      for (const crop of await asked(ByImage, 2)) crop.flush({ items: [match('X'), match('Y')] });
    }

    it('asks every block again with the words, within its category, and keeps the line of what is not sold', async () => {
      await answered();

      store.narrow('  gris ');
      const crops = await asked(ByImage, 2);
      expect(crops.map((crop) => [crop.request.params.get('category'), crop.request.params.get('q')])).toEqual([
        ['BACKPACK', 'gris'],
        ['SHIRT', 'gris'],
      ]);
      crops[0].flush({ items: [match('OFICINA')] });
      crops[1].flush({ items: [] });

      expect([store.words(), block(1).matches.length, block(2).matches.length]).toEqual(['gris', 1, 0]);
      expect(store.notSold()).toEqual(['reloj']);
    });

    /** Each crop spends from the minute's twelve: a question replaced is abandoned, not left to answer. */
    it('words typed again abandon the blocks still asked for the last ones', async () => {
      await answered();

      store.narrow('gris');
      const stale = await asked(ByImage, 2);
      store.narrow('negro');
      const fresh = await asked(ByImage, 2);

      expect(stale.every((crop) => crop.cancelled)).toBe(true);
      expect(fresh.map((crop) => crop.request.params.get('q'))).toEqual(['negro', 'negro']);
      for (const crop of fresh) crop.flush({ items: [] });
      expect(block(1).matches.length).toBe(0);
    });

    it('a new picture abandons everything still asked for the last one', async () => {
      await answered();
      store.narrow('gris');
      const stale = await asked(ByImage, 2);

      given();

      expect(stale.every((crop) => crop.cancelled)).toBe(true);
      expect([store.step(), store.objects()]).toEqual(['compose', []]);
    });

    it('cleared, every block is asked again without them', async () => {
      await answered();
      store.narrow('gris');
      for (const crop of await asked(ByImage, 2)) crop.flush({ items: [] });

      store.narrow('');
      const crops = await asked(ByImage, 2);
      expect(crops.every((crop) => !crop.request.params.has('q'))).toBe(true);
      for (const crop of crops) crop.flush({ items: [] });
    });
  });

  it('is counted once, when it closes: how it was given, what was found, and never what', async () => {
    await shopCan(true);
    given('viewfinder');
    store.choose('part');
    store.search();
    (await asked(Objects))[0].flush(read(backpack, tshirt, watch));
    for (const crop of await asked(ByImage, 2)) crop.flush({ items: [match('X'), match('Y')] });
    store.narrow('gris');
    for (const crop of await asked(ByImage, 2)) crop.flush({ items: [match('X')] });

    store.close();
    store.close();

    expect(telemetry.trackEvent).toHaveBeenCalledTimes(1);
    expect(telemetry.trackEvent).toHaveBeenCalledWith('search_by_image_submitted', {
      how: 'viewfinder',
      outcome: 'answered',
      results: 4,
      marked: true,
      objects: 3,
      not_sold: 1,
      words: true,
    });
    expect([store.isOpen(), URL.revokeObjectURL]).toEqual([false, expect.any(Function)]);
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:1');
  });

  it('a picture closed before it was searched is not counted', () => {
    given();
    store.close();

    expect(telemetry.trackEvent).not.toHaveBeenCalled();
  });

  it("opens a block's whole list on the page of its own, and closes", async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    await shopCan(true);
    given();
    store.search();
    (await asked(Objects))[0].flush(read(backpack, tshirt));
    const crops = await asked(ByImage, 2);
    crops[0].flush({ items: [match('A')] });
    crops[1].flush({ items: [match('B'), match('C')] });

    store.seeAll(2);

    const visual = TestBed.inject(VisualSearchStore);
    expect([visual.matches()?.length, visual.block(), store.isOpen()]).toEqual([2, 2, false]);
    expect(navigate).toHaveBeenCalledWith(['/', 'es', 'search', 'picture']);
  });
});
