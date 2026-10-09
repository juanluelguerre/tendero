import { execFileSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import type { FullConfig } from '@playwright/test';

/**
 * The shop these specs were written against: its whole catalogue on sale.
 *
 * They search for shoes, a backpack and a pan, and on a stack brought up from
 * nothing — CI's, a fresh clone's — the catalogue is drafts: importing never
 * publishes (ADR 0012). Publishing is the shopkeeper's, so it is asked of the
 * suite where a shopkeeper signs in, the backoffice's, as a run of its own:
 * one way of preparing a laboratory, in one place, and no second copy of the
 * sign-in here. A shop that already has its window is left as it is.
 */
export default function globalSetup(config: FullConfig): void {
  const backoffice = join(dirname(config.configFile ?? process.cwd()), '..', 'backoffice-e2e');

  execFileSync('npx', ['playwright', 'test', '--config=playwright.shop-window.config.ts'], {
    cwd: backoffice,
    stdio: 'inherit',
    // `npx` is a script on Windows, and a script needs a shell to be run.
    shell: true,
  });
}
