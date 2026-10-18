import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { SpeechAvailability, TranscriptionView } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * The only place in the storefront that knows what speaking to the shop is
 * called on the server (phase 14, ADR 0061).
 */
@Injectable({ providedIn: 'root' })
export class SpeechService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** Whether the shop has an engine. Configured on the server, not probed: nothing is transcribed. */
  status(): Observable<SpeechAvailability> {
    return this.http.get<SpeechAvailability>(`${this.baseUrl}/api/speech/status`);
  }

  /** The words of a question. The WAV is the body; the server keeps nothing of it. */
  transcribe(wav: Uint8Array, culture: string): Observable<TranscriptionView> {
    return this.http.post<TranscriptionView>(
      `${this.baseUrl}/api/speech/transcriptions`,
      new Blob([wav as BlobPart], { type: 'audio/wav' }),
      { params: new HttpParams().set('culture', culture), headers: new HttpHeaders({ 'Content-Type': 'audio/wav' }) },
    );
  }
}
