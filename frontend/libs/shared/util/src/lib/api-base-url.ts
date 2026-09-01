import { InjectionToken } from '@angular/core';

/**
 * Base de la API. En desarrollo va vacia y el dev-server hace de proxy hacia la
 * URL que inyecta Aspire (ver proxy.conf.mjs): asi no hay ni un localhost
 * escrito en el codigo de la aplicacion.
 *
 * Vive en shared/util y no una vez por app: era el mismo fichero byte a byte en
 * las dos. Es cableado puro, sin identidad de surface — exactamente el mismo
 * criterio por el que ya se comparte la configuracion de Transloco (ADR 0010).
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => '',
});
