/**
 * validate-schemas.test.ts — wraps the validate-schemas.ts script in a
 * vitest test so CI can run it as part of `pnpm test`. The script is
 * invoked via `tsx` (which is already a devDep of apps/central).
 *
 * If the script exits non-zero, the test fails. This guarantees the
 * script is run on every PR, not only on the `validate:schemas` hook.
 */
import { spawnSync } from 'node:child_process';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const SCRIPT = resolve(__dirname, '../../../../scripts/validate-schemas.ts');

describe('validate-schemas script (Zod ↔ Drizzle drift detection)', () => {
  it('exits 0 with no drift', () => {
    // Run from the shared-domain package root so it can resolve `tsx`
    // from its node_modules. The script is repo-relative internally.
    const cwd = resolve(__dirname, '../../..');
    const result = spawnSync('npx', ['tsx', SCRIPT], {
      cwd,
      encoding: 'utf8',
      stdio: 'pipe',
      // The script can be slow on cold start; give it 60s.
      timeout: 60_000,
    });
    if (result.status !== 0) {
      // Surface the script output in the test failure for easy debugging.
      const out = (result.stdout ?? '') + (result.stderr ?? '');
      throw new Error(
        `validate-schemas exited with code ${result.status}\n--- output ---\n${out}`,
      );
    }
    expect(result.status).toBe(0);
  }, 60_000);
});
