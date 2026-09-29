import { describe, it, expect } from "vitest";
import { readdirSync, readFileSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { glossary, type GlossaryTerm } from "./glossary";

const appRoot = join(dirname(fileURLToPath(import.meta.url)), "..");

function listSourceFiles(dir: string): string[] {
  const files: string[] = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === "node_modules" || entry.name.startsWith(".")) {
      continue;
    }
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      files.push(...listSourceFiles(full));
    } else if (/\.(ts|tsx)$/.test(entry.name)) {
      files.push(full);
    }
  }
  return files;
}

const termAttributePattern = /term="([a-z0-9-]+)"/g;

// Cada `term="..."` estático escrito en app/ (TermHint o TermLabel) debe existir en el glosario.
function collectReferencedTerms(): Map<string, string> {
  const found = new Map<string, string>();
  for (const file of listSourceFiles(appRoot)) {
    const text = readFileSync(file, "utf8");
    for (const match of text.matchAll(termAttributePattern)) {
      if (!found.has(match[1])) {
        found.set(match[1], relative(appRoot, file));
      }
    }
  }
  return found;
}

const keys = Object.keys(glossary);

describe("glosario", () => {
  it("cada definición es corta, no vacía y tiene fuente", () => {
    for (const key of keys) {
      const entry = glossary[key as GlossaryTerm];
      expect(entry.definition.trim().length, key).toBeGreaterThan(0);
      expect(entry.definition.length, key).toBeLessThanOrEqual(140);
      expect(entry.label.trim().length, key).toBeGreaterThan(0);
      expect(entry.source.trim().length, key).toBeGreaterThan(0);
    }
  });

  it("todos los términos usados en app/ existen en el glosario", () => {
    const referenced = collectReferencedTerms();
    expect(referenced.size).toBeGreaterThan(0);
    for (const [term, file] of referenced) {
      expect(keys, `term "${term}" referenciado en ${file}`).toContain(term);
    }
  });
});
