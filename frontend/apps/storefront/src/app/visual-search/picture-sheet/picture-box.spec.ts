import { describe, expect, it } from 'vitest';
import { clampBox, MinimumSide, nudgeBox, scaleBox, share, whole } from './picture-box';

/** The geometry of a picture's boxes: the server's, the shopper's mark, and the crop cut from either. */
describe('a box over a picture', () => {
  const picture = { width: 1200, height: 1600 };

  it('is scaled from the space the server read to the picture the browser holds', () => {
    // The server read a copy at 600 × 800; the browser holds the original at twice that.
    const box = scaleBox({ left: 30, top: 55, width: 230, height: 530 }, { width: 600, height: 800 }, picture);

    expect(box).toEqual({ left: 60, top: 110, width: 460, height: 1060 });
  });

  it('is kept inside the picture, and never smaller than a part worth searching', () => {
    expect(clampBox({ left: -40, top: 1500, width: 300, height: 300 }, picture)).toEqual({
      left: 0,
      top: 1300,
      width: 300,
      height: 300,
    });
    expect(clampBox({ left: 10, top: 10, width: 3, height: 2 }, picture)).toEqual({
      left: 10,
      top: 10,
      width: MinimumSide,
      height: MinimumSide,
    });
    expect(clampBox({ left: 0, top: 0, width: 5000, height: 9000 }, picture)).toEqual(whole(picture));
  });

  it('is whole numbers of pixels, as a canvas cuts them', () => {
    expect(clampBox({ left: 10.4, top: 10.6, width: 100.5, height: 99.4 }, picture)).toEqual({
      left: 10,
      top: 11,
      width: 101,
      height: 99,
    });
  });

  it('moves with the arrows and grows or shrinks with Shift and the arrows, inside the picture', () => {
    const box = { left: 100, top: 100, width: 200, height: 200 };

    expect(nudgeBox(box, 'ArrowRight', false, picture)).toEqual({ ...box, left: 100 + 24 });
    expect(nudgeBox(box, 'ArrowUp', false, picture)).toEqual({ ...box, top: 100 - 24 });
    expect(nudgeBox(box, 'ArrowRight', true, picture)).toEqual({ ...box, width: 200 + 24 });
    expect(nudgeBox(box, 'ArrowUp', true, picture)).toEqual({ ...box, height: 200 - 24 });
    // At the edge it stops there, and a key that is not an arrow changes nothing.
    expect(nudgeBox({ ...box, left: 10 }, 'ArrowLeft', false, picture).left).toBe(0);
    expect(nudgeBox(box, 'Enter', false, picture)).toBe(box);
  });

  it('is said as shares of the picture, for drawing over it at any size', () => {
    expect(share({ left: 300, top: 400, width: 600, height: 800 }, picture)).toEqual({
      left: 25,
      top: 25,
      width: 50,
      height: 50,
    });
  });
});
