import { InjectionToken } from '@angular/core';

/**
 * Base de la API. En desarrollo va vacia y el dev-server hace de proxy hacia la
 * URL que inyecta Aspire (ver proxy.conf.mjs): asi no hay ni un localhost
 * escrito en el codigo de la aplicacion.
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => '',
});
