import { afterNextRender, Component, DestroyRef, ElementRef, inject, output, signal, viewChild } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Icon } from '@tendero/shared-ui';

/** Whether this browser can show the camera in the page at all; without it, the phone's own camera app is opened. */
export function canShowCamera(): boolean {
  return typeof navigator !== 'undefined' && typeof navigator.mediaDevices?.getUserMedia === 'function';
}

/**
 * The camera, in the page (the camera search, CS-6; the mock-up, screen 13):
 * full screen, the rear camera, a shutter, a close button and a hint line. The
 * shutter **freezes the frame**, and that frame is the picture — objects are
 * found on it, never live: a detector on every frame is a server's worth of
 * CPU per shopper.
 *
 * Permission refused, no camera, or the stream failing: it says so by
 * `unavailable`, and the panel opens the phone's own camera app instead, so
 * the button never simply fails. The stream is stopped the moment the frame
 * is taken or the viewfinder closes.
 */
@Component({
  selector: 'storefront-viewfinder',
  imports: [TranslocoDirective, Icon],
  templateUrl: './viewfinder.html',
})
export class Viewfinder {
  /** The frozen frame, taken. */
  readonly taken = output<Blob>();
  readonly closed = output<void>();
  /** The camera could not be shown: refused, absent or failed. */
  readonly unavailable = output<void>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly video = viewChild.required<ElementRef<HTMLVideoElement>>('video');
  private stream: MediaStream | null = null;

  /** The frame the shutter froze, until it is used or taken again. */
  protected readonly frozen = signal<{ readonly blob: Blob; readonly url: string } | null>(null);

  constructor() {
    afterNextRender(() => {
      this.dialog().nativeElement.showModal();
      void this.start();
    });
    inject(DestroyRef).onDestroy(() => {
      this.stop();
      this.thaw();
    });
  }

  protected async shoot(): Promise<void> {
    const video = this.video().nativeElement;
    if (!video.videoWidth) return;
    const canvas = document.createElement('canvas');
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    canvas.getContext('2d')?.drawImage(video, 0, 0);
    const blob = await new Promise<Blob | null>((done) => canvas.toBlob(done, 'image/jpeg', 0.92));
    if (!blob) return;
    this.stop();
    this.frozen.set({ blob, url: URL.createObjectURL(blob) });
  }

  protected retake(): void {
    this.thaw();
    void this.start();
  }

  protected use(): void {
    const frozen = this.frozen();
    if (frozen) this.taken.emit(frozen.blob);
  }

  private async start(): Promise<void> {
    try {
      this.stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: { ideal: 'environment' } },
        audio: false,
      });
      const video = this.video().nativeElement;
      video.srcObject = this.stream;
      await video.play();
    } catch {
      this.stop();
      this.unavailable.emit();
    }
  }

  private stop(): void {
    for (const track of this.stream?.getTracks() ?? []) track.stop();
    this.stream = null;
  }

  private thaw(): void {
    const frozen = this.frozen();
    if (frozen) URL.revokeObjectURL(frozen.url);
    this.frozen.set(null);
  }
}
