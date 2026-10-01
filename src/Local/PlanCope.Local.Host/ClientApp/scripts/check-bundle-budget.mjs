import { readdirSync, statSync } from "node:fs";
import { join } from "node:path";

const DIST_DIR = join(import.meta.dirname, "..", "dist");
// The bundle budget applies to executable code and CSS (plus the HTML entry point).
// Public static assets such as the locally bundled typefaces and institutional logos
// are required for offline use, but are reported separately from the JS/CSS budget.
const DEFAULT_BUDGET_BYTES = 350_000;

function totalSize(dir, include = () => true) {
  let total = 0;
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (!include(entry, full)) continue;
    const stat = statSync(full);
    total += stat.isDirectory() ? totalSize(full) : stat.size;
  }
  return total;
}

const budget = Number(process.env.BUNDLE_BUDGET_BYTES ?? DEFAULT_BUDGET_BYTES);

let bundleBytes;
let staticBytes;
try {
  bundleBytes = totalSize(DIST_DIR, entry => entry !== "static");
  const staticDir = join(DIST_DIR, "static");
  staticBytes = statSync(staticDir).isDirectory() ? totalSize(staticDir) : 0;
} catch (err) {
  console.error(`check-bundle-budget: cannot measure ${DIST_DIR}: ${err.message}`);
  process.exit(1);
}

console.log(`check-bundle-budget: HTML/JS/CSS = ${bundleBytes} bytes (${(bundleBytes / 1024).toFixed(1)} KiB)`);
console.log(`check-bundle-budget: static assets = ${staticBytes} bytes (${(staticBytes / 1024).toFixed(1)} KiB; outside JS/CSS budget)`);
console.log(`check-bundle-budget: budget = ${budget} bytes (${(budget / 1024).toFixed(1)} KiB)`);

if (bundleBytes > budget) {
  console.error(
    `check-bundle-budget: FAIL — HTML/JS/CSS exceeds budget by ${bundleBytes - budget} bytes`
  );
  process.exit(1);
}

console.log("check-bundle-budget: OK");
