# shared-api

The API contract's TypeScript types. `src/lib/generated/schema.ts` is generated
from `docs/openapi/tendero.json` (`npm run generate:api-types`, never edited by
hand); the `*-contracts.ts` files beside it are the stable aliases the apps
import. Types are behaviour and are generated; clients are identity and stay
hand-written in each app's `data-access/` (ADR 0010).
