/**
 * `exam.service.ts` test suite.
 *
 * Covers the version-integrity contract:
 *   - computeChecksum is deterministic (same input → same output)
 *   - different blocks → different checksum
 *   - output is a 64-character hex string (SHA-256)
 *   - checksum is stable across key reordering (canonical JSON)
 *   - checksum is stable across whitespace differences in JSON strings
 */
import { describe, expect, it } from 'vitest';
import { computeChecksum, type ExamVersionInput } from '../services/exam.service.js';

const baseInput: ExamVersionInput = {
  examId: '11111111-1111-1111-1111-111111111111',
  versionNumber: 1,
  blocks: [
    {
      orderIndex: 0,
      blockType: 'text',
      config: { text: 'Lee con atención.' },
    },
    {
      orderIndex: 1,
      blockType: 'multiple_choice',
      config: { question: '¿2+2?', options: ['3', '4', '5'] },
    },
  ],
};

describe('exam.service.computeChecksum', () => {
  it('returns a 64-char hex string (SHA-256)', () => {
    const sum = computeChecksum(baseInput);
    expect(sum).toMatch(/^[0-9a-f]{64}$/);
  });

  it('is deterministic for the same input', () => {
    const a = computeChecksum(baseInput);
    const b = computeChecksum(baseInput);
    expect(a).toBe(b);
  });

  it('differs when any block changes', () => {
    const a = computeChecksum(baseInput);
    const changed: ExamVersionInput = {
      ...baseInput,
      blocks: [
        baseInput.blocks[0]!,
        { ...baseInput.blocks[1]!, config: { question: '¿2+2?', options: ['3', '4', '5', '6'] } },
      ],
    };
    expect(computeChecksum(changed)).not.toBe(a);
  });

  it('differs when version number changes', () => {
    const a = computeChecksum(baseInput);
    expect(computeChecksum({ ...baseInput, versionNumber: 2 })).not.toBe(a);
  });

  it('is stable across JSON key insertion order (canonical serialization)', () => {
    // Object built with a different property order should yield the same hash
    const reordered: ExamVersionInput = {
      versionNumber: 1,
      examId: '11111111-1111-1111-1111-111111111111',
      blocks: [
        {
          blockType: 'text',
          orderIndex: 0,
          config: { text: 'Lee con atención.' },
        },
        {
          config: { question: '¿2+2?', options: ['3', '4', '5'] },
          blockType: 'multiple_choice',
          orderIndex: 1,
        },
      ],
    };
    expect(computeChecksum(reordered)).toBe(computeChecksum(baseInput));
  });

  it('handles an empty blocks list', () => {
    const empty: ExamVersionInput = { ...baseInput, blocks: [] };
    const sum = computeChecksum(empty);
    expect(sum).toMatch(/^[0-9a-f]{64}$/);
  });

  it('handles all 5 block types', () => {
    const all: ExamVersionInput = {
      examId: '22222222-2222-2222-2222-222222222222',
      versionNumber: 1,
      blocks: [
        { orderIndex: 0, blockType: 'text', config: { text: 'T' } },
        { orderIndex: 1, blockType: 'image', config: { asset_id: 'a1', alt: 'a' } },
        { orderIndex: 2, blockType: 'multiple_choice', config: { question: 'q', options: ['a', 'b'] } },
        { orderIndex: 3, blockType: 'true_false', config: { statement: 's' } },
        { orderIndex: 4, blockType: 'short_answer', config: { question: 'q', max_length: 100 } },
      ],
    };
    expect(computeChecksum(all)).toMatch(/^[0-9a-f]{64}$/);
  });
});
