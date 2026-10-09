/** The longest side a picture is sent at: four times what the model looks at, and a tenth of a phone's photograph. */
export const LongestSide = 1024;

/**
 * A picture made small before it leaves the browser.
 *
 * A phone's photograph is three to six megabytes and the model reads 224
 * pixels a side; sending it whole is a slow upload for nothing. Drawing it onto
 * a canvas also leaves behind everything the file carried beside its pixels —
 * where and when it was taken — before it travels at all. The browser turns the
 * picture the way up its orientation says as it decodes it.
 *
 * **It never refuses.** A browser without the pieces, or a file it cannot
 * decode, sends the file as it is: what is and is not a picture is the
 * server's to say, in one place, with one set of words.
 */
export async function shrinkPicture(file: Blob): Promise<Blob> {
  if (typeof createImageBitmap !== 'function' || typeof OffscreenCanvas !== 'function') return file;

  try {
    const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
    try {
      const scale = Math.min(1, LongestSide / Math.max(bitmap.width, bitmap.height));
      const canvas = new OffscreenCanvas(
        Math.max(1, Math.round(bitmap.width * scale)),
        Math.max(1, Math.round(bitmap.height * scale)),
      );
      const context = canvas.getContext('2d');
      if (!context) return file;

      // White under it: a JPEG has no transparency, and the model lays a see-through picture over white too.
      context.fillStyle = 'white';
      context.fillRect(0, 0, canvas.width, canvas.height);
      context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
      return await canvas.convertToBlob({ type: 'image/jpeg', quality: 0.9 });
    } finally {
      bitmap.close();
    }
  } catch {
    return file;
  }
}
