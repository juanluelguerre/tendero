# frontend

Workspace Nx con las dos aplicaciones Angular 22 de Tendero.

```bash
nvm use                      # Node 24: Angular 22 exige >= 24.15 (ver .nvmrc)
npm install

npx nx serve storefront      # http://localhost:4200
npx nx serve backoffice      # http://localhost:4201
```

Normalmente no hace falta arrancarlas a mano: `dotnet run --project src/AppHost`
levanta también estas dos, con la URL de la API inyectada.

## Estructura

```
apps/
  storefront/    tienda. Espaciosa, clara, una accion clay por vista
  backoffice/    denso, oscuro por defecto, filas de 36px
libs/shared/
  tokens/        puente a design/tokens.css + el bloque @theme de Tailwind
  ui/            primitivas SIN identidad: inputs, tablas, anillo de foco
  util/          pipes y helpers (Money, resolucion de LocalizedText)
  api/           tipos del contrato de la API
  i18n/          cableado de Transloco (no las traducciones)
```

Dentro de cada app: `core/` (interceptores, config), `layout/` (su shell, que
**no** se comparte), `features/` (una carpeta por feature, gemelas de los
vertical slices del backend) y `data-access/`.

## Las dos apps no se conocen

No por convencion: por regla de lint que rompe el build.

| desde | puede importar de |
|---|---|
| `scope:storefront` | `scope:shared` — y de nada mas |
| `scope:backoffice` | `scope:shared` — y de nada mas |
| `scope:shared` | `scope:shared` — no sabe que las apps existen |

Es el gemelo en TypeScript de los tests de NetArchTest del backend. Verificado
por mutacion: un import cruzado hace fallar `nx lint`.

**Que se comparte y que se duplica**: se comparte lo dificil por su
*comportamiento* (un DatePicker es logica de calendario, teclado y
accesibilidad); se duplica lo dificil por su *identidad* (los dos layouts no
comparten mas que la palabra). Razonado en `docs/adr/0010`.

## Reglas que no se negocian

- **Ni un hex fuera de `design/tokens.css`.** Los componentes leen tokens.
- **Ni un literal de cara al usuario fuera de los ficheros de Transloco**, y
  siempre en es *y* en en. Cada app tiene los suyos: la voz de la tienda y la
  del backoffice no son la misma.
- **Ni una URL del backend en el codigo.** El dev-server hace de proxy hacia la
  direccion que inyecta Aspire; `proxy.conf.mjs` es el unico sitio con un
  localhost de reserva.

## Comandos

```bash
npx nx run-many -t lint      # incluye las reglas de frontera
npx nx run-many -t test
npx nx run-many -t build
npx nx affected -t build     # solo lo que toco tu cambio
```
