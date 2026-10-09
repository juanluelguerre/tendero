import { test as setup } from '@playwright/test';
import { prepareShopWindow } from './laboratory';

/**
 * Run by the storefront's specs before they start (`storefront-e2e`'s
 * `global-setup.ts`, through `playwright.shop-window.config.ts`), never by this suite: the whole catalogue on sale, as the
 * shopkeeper puts it there, and found by the search.
 */
setup('the shop has its whole catalogue on sale', async ({ page }) => {
  // A hundred and twenty publications and an index that hears of each from the
  // outbox: minutes on a cold runner, a single read where it is already so.
  setup.setTimeout(480_000);
  await prepareShopWindow(page);
});
