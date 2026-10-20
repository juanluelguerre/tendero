import { type PictureBox, type PictureSize, scaleBox } from './picture-sheet/picture-box';

/**
 * The longest side a picture is sent at (the camera search, CS-6): the detector
 * reads 960 pixels and SigLIP far fewer, and a phone's photograph is four
 * thousand — megabytes of upload for what the shop reads at a fraction of it.
 * A part is sent at its own size, scaled down only past this.
 */
export const LongestSide = 1536;

/** A part of a picture: a box, and the size of the picture it was drawn over. */
export interface PicturePart {
  readonly box: PictureBox;
  readonly of: PictureSize;
}

/**
 * A picture made small before it leaves the browser — or only a part of it,
 * cut out, so that the rest never leaves (CS-1.2).
 *
 * Drawing it onto a canvas also leaves behind everything the file carried
 * beside its pixels — where and when it was taken — before it travels at all.
 * The browser turns the picture the way up its orientation says as it decodes
 * it, which is the way up the shopper saw it and marked it.
 *
 * **It never refuses.** A browser without the pieces, or a file it cannot
 * decode, sends the file as it is — the whole of it, even when a part was
 * asked for: what is and is not a picture is the server's to say, in one place,
 * with one set of words.
 */
export async function shrinkPicture(file: Blob, part?: PicturePart): Promise<Blob> {
  if (typeof createImageBitmap !== 'function' || typeof OffscreenCanvas !== 'function') return file;

  try {
    const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
    try {
      const region = part
        ? scaleBox(part.box, part.of, { width: bitmap.width, height: bitmap.height })
        : { left: 0, top: 0, width: bitmap.width, height: bitmap.height };
      const scale = Math.min(1, LongestSide / Math.max(region.width, region.height));
      const canvas = new OffscreenCanvas(
        Math.max(1, Math.round(region.width * scale)),
        Math.max(1, Math.round(region.height * scale)),
      );
      const context = canvas.getContext('2d');
      if (!context) return file;

      // White under it: a JPEG has no transparency, and the model lays a see-through picture over white too.
      context.fillStyle = 'white';
      context.fillRect(0, 0, canvas.width, canvas.height);
      context.drawImage(
        bitmap,
        region.left,
        region.top,
        region.width,
        region.height,
        0,
        0,
        canvas.width,
        canvas.height,
      );
      return await canvas.convertToBlob({ type: 'image/jpeg', quality: 0.9 });
    } finally {
      bitmap.close();
    }
  } catch {
    return file;
  }
}
