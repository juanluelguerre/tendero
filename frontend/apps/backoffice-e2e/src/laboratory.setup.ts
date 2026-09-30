import { test as setup } from '@playwright/test';
import { prepareLaboratory } from './laboratory';

/**
 * Runs before the specs (playwright.config.ts, the `laboratory` project): a stack
 * brought up from nothing gets the least every spec needs, and the author's
 * laboratory, which has it, is left as it is.
 */
setup('the laboratory has what the specs act on', async ({ page }) => {
  // Importing a hundred products, publishing, placing a page of orders and
  // waiting for the workers is minutes on a cold runner, not the sixty seconds
  // one spec gets; on a laboratory that has it all, it is a few reads.
  setup.setTimeout(300_000);
  await prepareLaboratory(page);
});
