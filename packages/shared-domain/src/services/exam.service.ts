/**
 * Exam service — pure (no DB) helpers for `exam_versions`.
 *
 * The central concept is the **version checksum**: a deterministic SHA-256
 * hash over the exam's blocks. It lets clients verify that a downloaded
 * exam version matches the one the server published, before any student
 * touches the data.
 *
 * Design notes:
 *   - Canonical JSON serialization (sorted keys) so the hash is stable
 *     regardless of object literal property order in the caller's code.
 *   - Blocks are sorted by `orderIndex` before hashing so callers don't
 *     have to pre-sort.
 *   - Pure function — no I/O, no global state, safe to call from
 *     seeders, sync workers, and tests alike.
 */
import { createHash } from 'node:crypto';

export type ExamBlockType =
  | 'text'
  | 'image'
  | 'multiple_choice'
  | 'true_false'
  | 'short_answer';

export interface ExamBlockInput {
  orderIndex: number;
  blockType: ExamBlockType;
  config: unknown;
}

export interface ExamVersionInput {
  examId: string;
  versionNumber: number;
  blocks: readonly ExamBlockInput[];
}

/**
 * Serialize a value with sorted object keys at every level. This is the
 * cornerstone of checksum stability: two semantically-equal inputs must
 * always serialize to the same byte sequence regardless of how the
 * caller built the object.
 *
 * Arrays preserve their order (so we can sort blocks explicitly by
 * `orderIndex` first). `undefined` values are dropped; `Date` and other
 * non-JSON types are not supported (callers should pass plain JSON).
 */
const canonicalStringify = (value: unknown): string => {
  if (value === null || typeof value !== 'object') {
    return JSON.stringify(value);
  }
  if (Array.isArray(value)) {
    return `[${value.map((v) => canonicalStringify(v)).join(',')}]`;
  }
  // Plain object — sort keys lexicographically.
  const obj = value as Record<string, unknown>;
  const keys = Object.keys(obj).filter((k) => obj[k] !== undefined).sort();
  const pairs = keys.map((k) => `${JSON.stringify(k)}:${canonicalStringify(obj[k])}`);
  return `{${pairs.join(',')}}`;
};

/**
 * Compute a SHA-256 checksum (hex-encoded, 64 chars) for an exam version.
 *
 * The input is first normalized:
 *   1. blocks are deep-cloned (cheap — they're plain JSON),
 *   2. blocks are sorted by `orderIndex` ascending,
 *   3. the whole input is canonical-stringified,
 *   4. the string is fed to SHA-256.
 *
 * @example
 *   const sum = computeChecksum({ examId, versionNumber: 1, blocks: [...] });
 *   // → "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
 */
export const computeChecksum = (input: ExamVersionInput): string => {
  // Sort blocks by orderIndex. Use Array.from to avoid mutating the input.
  const sortedBlocks = Array.from(input.blocks).sort(
    (a, b) => a.orderIndex - b.orderIndex,
  );
  const normalized = {
    examId: input.examId,
    versionNumber: input.versionNumber,
    blocks: sortedBlocks,
  };
  const json = canonicalStringify(normalized);
  return createHash('sha256').update(json, 'utf8').digest('hex');
};
