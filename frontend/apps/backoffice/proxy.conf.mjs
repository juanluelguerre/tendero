/**
 * The same bridge as the storefront's, duplicated on purpose: it is three lines
 * of configuration per application, and a shared version would have to learn
 * that two apps exist in order to serve them (docs/adr/0010).
 *
 * The dev server forwards /api to the backend. Aspire injects the URL as
 * `services__api__http__0` on starting with `dotnet run --project src/AppHost`;
 * the fallback value only matters if somebody brings the frontend up on its own.
 *
 * It was missing: until now the backoffice declared no proxy at all, so it could
 * not talk to the API whatsoever. Nobody noticed because its only page was an
 * empty state that made no requests.
 */
const api =
  process.env['services__api__http__0'] ??
  process.env['services__api__https__0'] ??
  'http://localhost:5130';

export default [
  {
    // /dev-issuer too: the login asks the development issuer for the token, and
    // that lives inside the API. When Keycloak arrives it will be another origin
    // and this entry disappears.
    context: ['/api', '/dev-issuer'],
    target: api,
    secure: false,
    changeOrigin: true,
  },
];
