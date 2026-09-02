import { HttpClient, HttpInterceptorFn } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { API_BASE_URL } from '@tendero/shared-util';
import { firstValueFrom } from 'rxjs';

/**
 * Quien puede iniciar sesion. Lo sirve el emisor, no lo escribe el cliente: el
 * dia que el emisor sea Keycloak esta lista vendra de un realm y aqui no cambia
 * nada.
 */
export interface TenderoIdentity {
  subject: string;
  name: string;
  role: string;
  isAgent: boolean;
}

interface TokenResponse {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
}

const STORAGE_KEY = 'tendero.token';

/**
 * El token y quien es su dueno.
 *
 * Vive en shared porque es COMPORTAMIENTO —guardar, adjuntar, cerrar sesion— y
 * eso se escribe una vez (ADR 0010). La PANTALLA de login es identidad: el
 * storefront acabara pidiendo email y contrasena a un comprador, y el backoffice
 * elige entre identidades sembradas; compartir esa vista produciria un
 * componente con una matriz de variantes peor que dos componentes.
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private readonly token = signal<string | null>(readStoredToken());

  readonly accessToken = this.token.asReadonly();
  readonly isSignedIn = computed(() => this.token() !== null);

  /**
   * Las identidades sembradas del emisor de desarrollo. Devuelve lista vacia si
   * no responde: sin emisor no hay login, y una pantalla vacia se explica mejor
   * que una excepcion.
   */
  async identities(): Promise<TenderoIdentity[]> {
    try {
      return await firstValueFrom(
        this.http.get<TenderoIdentity[]>(`${this.baseUrl}/dev-issuer/identities`),
      );
    } catch {
      return [];
    }
  }

  /**
   * `password` para personas y `client_credentials` para agentes: son los dos
   * flujos que Keycloak servira despues, asi que usarlos ya evita que el cliente
   * cambie cuando cambie el emisor.
   */
  async signIn(identity: TenderoIdentity): Promise<void> {
    const body = new URLSearchParams(
      identity.isAgent
        ? { grant_type: 'client_credentials', client_id: identity.subject }
        : { grant_type: 'password', username: identity.subject },
    );

    const response = await firstValueFrom(
      this.http.post<TokenResponse>(`${this.baseUrl}/dev-issuer/connect/token`, body.toString(), {
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      }),
    );

    this.token.set(response.accessToken);
    write(response.accessToken);
  }

  signOut(): void {
    this.token.set(null);
    write(null);
  }
}

/**
 * Adjunta el token a las llamadas a NUESTRA API y a nada mas. Sin ese filtro, un
 * dia alguien anade una llamada a un tercero y le manda nuestro token de paso.
 */
export const tenderoAuthInterceptor: HttpInterceptorFn = (request, next) => {
  const token = inject(AuthStore).accessToken();
  const isOurs = request.url.startsWith('/api') || request.url.startsWith('/dev-issuer');

  return token && isOurs
    ? next(request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }))
    : next(request);
};

/**
 * localStorage puede lanzar (modo privado, cookies bloqueadas) y no arrancar la
 * aplicacion por no poder recordar una sesion seria desproporcionado.
 */
function readStoredToken(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

function write(token: string | null): void {
  try {
    if (token) localStorage.setItem(STORAGE_KEY, token);
    else localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Sin persistencia la sesion dura lo que la pestana. Es aceptable.
  }
}
