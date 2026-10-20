/** A rectangle over a picture, in that picture's own pixels from its top-left corner, upright. */
export interface PictureBox {
  readonly left: number;
  readonly top: number;
  readonly width: number;
  readonly height: number;
}

/** A picture's size, upright. */
export interface PictureSize {
  readonly width: number;
  readonly height: number;
}

/** The smallest side a part may have: smaller is a few pixels nobody meant to search. */
export const MinimumSide = 32;

/** How far one press of an arrow moves or resizes a box, in the picture's pixels. */
export const Step = 24;

/** The whole picture, as a box. */
export function whole(size: PictureSize): PictureBox {
  return { left: 0, top: 0, width: size.width, height: size.height };
}

/**
 * A box read in one copy of a picture, said in another: the server reads the
 * smaller copy that was sent, and the crop is cut from the picture the browser holds.
 */
export function scaleBox(box: PictureBox, from: PictureSize, to: PictureSize): PictureBox {
  const x = to.width / from.width;
  const y = to.height / from.height;
  return { left: box.left * x, top: box.top * y, width: box.width * x, height: box.height * y };
}

/**
 * A box kept inside its picture, at least {@link MinimumSide} a side, in whole
 * pixels: what a canvas cuts. Too large, it is the whole picture; past an edge,
 * it is moved back rather than cut, so the part keeps the size it was given.
 */
export function clampBox(box: PictureBox, within: PictureSize): PictureBox {
  const width = Math.round(Math.min(within.width, Math.max(MinimumSide, box.width)));
  const height = Math.round(Math.min(within.height, Math.max(MinimumSide, box.height)));
  const left = Math.round(Math.min(within.width - width, Math.max(0, box.left)));
  const top = Math.round(Math.min(within.height - height, Math.max(0, box.top)));
  return { left, top, width, height };
}

/**
 * A box moved by an arrow, or resized from its bottom-right corner with Shift
 * held (CS-8: a box can be drawn, moved and resized without a pointer). Any
 * other key leaves the same box, so a caller can tell nothing changed.
 */
export function nudgeBox(box: PictureBox, key: string, resize: boolean, within: PictureSize): PictureBox {
  const [x, y] = Arrows[key] ?? [0, 0];
  if (x === 0 && y === 0) return box;
  const moved = resize
    ? { ...box, width: box.width + x * Step, height: box.height + y * Step }
    : { ...box, left: box.left + x * Step, top: box.top + y * Step };
  return clampBox(moved, within);
}

/** A box as percentages of its picture, so it is drawn over the picture at whatever size the picture is shown. */
export function share(box: PictureBox, of: PictureSize): PictureBox {
  return {
    left: (box.left / of.width) * 100,
    top: (box.top / of.height) * 100,
    width: (box.width / of.width) * 100,
    height: (box.height / of.height) * 100,
  };
}

const Arrows: Readonly<Record<string, readonly [number, number]>> = {
  ArrowLeft: [-1, 0],
  ArrowRight: [1, 0],
  ArrowUp: [0, -1],
  ArrowDown: [0, 1],
};
