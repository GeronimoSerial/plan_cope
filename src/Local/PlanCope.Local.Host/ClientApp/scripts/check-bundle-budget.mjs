import { readdirSync, statSync } from "node:fs";
import { join } from "node:path";

const DIST_DIR = join(import.meta.dirname, "..", "dist");
// Measured dist/ = 250_159 bytes on 2026-09-15, after B8's dependency cleanup and
// virtualization landed. Budget set ~40% above that (not pinned to it) so the gate
// survives a legitimate feature addition instead of becoming the first thing deleted
// under deadline pressure; still tight enough to catch an accidental heavy dependency.
const DEFAULT_BUDGET_BYTES = 350_000;

function totalSize(dir) {
  let total = 0;
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    const stat = statSync(full);
    total += stat.isDirectory() ? totalSize(full) : stat.size;
  }
  return total;
}

const budget = Number(process.env.BUNDLE_BUDGET_BYTES ?? DEFAULT_BUDGET_BYTES);

let total;
try {
  total = totalSize(DIST_DIR);
} catch (err) {
  console.error(`check-bundle-budget: cannot measure ${DIST_DIR}: ${err.message}`);
  process.exit(1);
}

console.log(`check-bundle-budget: dist/ total = ${total} bytes (${(total / 1024).toFixed(1)} KiB)`);
console.log(`check-bundle-budget: budget = ${budget} bytes (${(budget / 1024).toFixed(1)} KiB)`);

if (total > budget) {
  console.error(
    `check-bundle-budget: FAIL — dist/ exceeds budget by ${total - budget} bytes`
  );
  process.exit(1);
}

console.log("check-bundle-budget: OK");