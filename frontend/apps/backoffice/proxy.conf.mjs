/**
 * Mismo puente que el del storefront, y duplicado a proposito: es tres lineas de
 * configuracion por aplicacion, y una version compartida tendria que aprender
 * que existen dos apps para poder servirlas (docs/adr/0010).
 *
 * El dev-server reenvia /api al backend. La URL la inyecta Aspire como
 * `services__api__http__0` al arrancar con `dotnet run --project src/AppHost`;
 * el valor de reserva solo sirve si alguien levanta el frontend suelto.
 *
 * Faltaba: hasta ahora el backoffice no declaraba proxy ninguno, asi que no
 * podia hablar con la API en absoluto. No se noto porque su unica pagina era un
 * estado vacio sin peticiones.
 */
const api =
  process.env['services__api__http__0'] ??
  process.env['services__api__https__0'] ??
  'http://localhost:5130';

export default [
  {
    // /dev-issuer tambien: el login pide el token al emisor de desarrollo, que
    // vive dentro de la API. Cuando entre Keycloak sera otro origen y esta
    // entrada desaparece.
    context: ['/api', '/dev-issuer'],
    target: api,
    secure: false,
    changeOrigin: true,
  },
];
