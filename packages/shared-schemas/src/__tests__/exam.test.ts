import { describe, it, expect } from 'vitest';
import { examInsert, examBlockInsert, blockTypeEnum } from '../exam.js';

describe('exams', () => {
  it('valid exam insert passes', () => {
    const data = {
      code: 'MAT-101',
      title: 'Matemáticas Básicas',
      subject: 'Matemática',
    };
    const result = examInsert.parse(data);
    expect(result.code).toBe('MAT-101');
    expect(result.title).toBe('Matemáticas Básicas');
  });
});

describe('exam blocks', () => {
  const base = {
    exam_version_id: '550e8400-e29b-41d4-a716-446655440010',
    order_index: 1,
  };

  it('text block parses correctly', () => {
    const data = {
      ...base,
      block_type: 'text' as const,
      config_json: { text: 'Read the following passage' },
    };
    const result = examBlockInsert.parse(data);
    expect(result.block_type).toBe('text');
    if (result.block_type === 'text') {
      expect(result.config_json.text).toBe('Read the following passage');
    }
  });

  it('image block parses correctly', () => {
    const data = {
      ...base,
      block_type: 'image' as const,
      config_json: { asset_id: 'abc-123', alt: 'Diagram', caption: 'Fig 1' },
    };
    const result = examBlockInsert.parse(data);
    expect(result.block_type).toBe('image');
    if (result.block_type === 'image') {
      expect(result.config_json.asset_id).toBe('abc-123');
      expect(result.config_json.alt).toBe('Diagram');
    }
  });

  it('multiple_choice block parses correctly', () => {
    const data = {
      ...base,
      block_type: 'multiple_choice' as const,
      config_json: {
        question: 'What is 2+2?',
        options: ['3', '4', '5'],
      },
    };
    const result = examBlockInsert.parse(data);
    expect(result.block_type).toBe('multiple_choice');
    if (result.block_type === 'multiple_choice') {
      expect(result.config_json.options).toHaveLength(3);
    }
  });

  it('true_false block parses correctly', () => {
    const data = {
      ...base,
      block_type: 'true_false' as const,
      config_json: { statement: 'The Earth is flat' },
    };
    const result = examBlockInsert.parse(data);
    expect(result.block_type).toBe('true_false');
    if (result.block_type === 'true_false') {
      expect(result.config_json.statement).toBe('The Earth is flat');
    }
  });

  it('short_answer block parses correctly', () => {
    const data = {
      ...base,
      block_type: 'short_answer' as const,
      config_json: {
        question: 'Capital of France?',
        max_length: 50,
        case_sensitive: false,
        trim: true,
      },
    };
    const result = examBlockInsert.parse(data);
    expect(result.block_type).toBe('short_answer');
    if (result.block_type === 'short_answer') {
      expect(result.config_json.max_length).toBe(50);
    }
  });

  it('invalid block_type fails', () => {
    const data = {
      ...base,
      block_type: 'essay',
      config_json: { text: 'some text' },
    };
    expect(() => examBlockInsert.parse(data)).toThrow();
  });

  it('wrong config_json shape for block_type fails', () => {
    const data = {
      ...base,
      block_type: 'text' as const,
      config_json: { asset_id: 'abc', alt: 'wrong shape' },
    };
    expect(() => examBlockInsert.parse(data)).toThrow();
  });
});

describe('blockTypeEnum', () => {
  it('accepts exactly the 5 V1 block types', () => {
    expect(blockTypeEnum.options).toEqual([
      'text',
      'image',
      'multiple_choice',
      'true_false',
      'short_answer',
    ]);
  });
});
