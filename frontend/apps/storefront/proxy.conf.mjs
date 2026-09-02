/**
 * The dev server forwards /api to the backend. Aspire injects the URL as
 * `services__api__http__0` when started with `dotnet run --project
 * src/AppHost`; the fallback value only matters if somebody brings the frontend
 * up on its own. It is the ONLY place in the repo where a backend localhost
 * appears.
 */
const api =
  process.env['services__api__http__0'] ??
  process.env['services__api__https__0'] ??
  'http://localhost:5130';

export default [
  {
    context: ['/api'],
    target: api,
    secure: false,
    changeOrigin: true,
  },
];
