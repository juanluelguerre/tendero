/**
 * El dev-server reenvia /api al backend. La URL la inyecta Aspire como
 * `services__api__http__0` cuando se arranca con `dotnet run --project
 * src/AppHost`; el valor de reserva solo sirve si alguien levanta el frontend
 * suelto. Es el UNICO sitio del repo donde aparece un localhost del backend.
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
