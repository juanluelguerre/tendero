import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { tenderoCultureInterceptor } from './culture-interceptor';
import { CultureStore } from './culture-store';
import { provideTenderoI18n } from './provide-tendero-i18n';

/**
 * A refusal is said in the language asked (ADR 0059), and a call that names no
 * `?culture=` is asking in `Accept-Language` (ADR 0013) — which the browser
 * fills with its own languages, not the application's. Found on a screen: a
 * Spanish backoffice in an English browser said *That agent is already
 * registered*.
 */
describe('tenderoCultureInterceptor', () => {
  function client(stored: string): { http: HttpClient; sent: HttpTestingController } {
    localStorage.clear();
    localStorage.setItem('tendero.culture', stored);

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([tenderoCultureInterceptor])),
        provideHttpClientTesting(),
        provideTenderoI18n(),
      ],
    });

    return { http: TestBed.inject(HttpClient), sent: TestBed.inject(HttpTestingController) };
  }

  afterEach(() => localStorage.clear());

  it("asks our API in the application's language, whatever the browser's is", () => {
    const { http, sent } = client('es');

    http.post('/api/agents', {}).subscribe();

    expect(sent.expectOne('/api/agents').request.headers.get('Accept-Language')).toBe('es');
  });

  it('follows the language when the person changes it', () => {
    const { http, sent } = client('es');
    TestBed.inject(CultureStore).use('en');

    http.get('/api/returns').subscribe();

    expect(sent.expectOne('/api/returns').request.headers.get('Accept-Language')).toBe('en');
  });

  it('leaves a language a caller named itself', () => {
    const { http, sent } = client('es');

    http.get('/api/search', { headers: { 'Accept-Language': 'en' } }).subscribe();

    expect(sent.expectOne('/api/search').request.headers.get('Accept-Language')).toBe('en');
  });

  it('says nothing to anybody else', () => {
    const { http, sent } = client('es');

    http.get('https://issuer.example/.well-known/openid-configuration').subscribe();

    expect(
      sent.expectOne('https://issuer.example/.well-known/openid-configuration').request.headers.has('Accept-Language'),
    ).toBe(false);
  });
});
