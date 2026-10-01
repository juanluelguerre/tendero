import { provideReadableResources } from '@tendero/shared-util';

/** What every unit test gets, as the app does (app.config.ts): a failed resource reads as no value. */
export default [provideReadableResources()];
