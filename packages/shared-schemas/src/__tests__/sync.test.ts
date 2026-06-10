import { describe, it, expect } from 'vitest';
import { registeredNodeInsert, syncInboxInsert, nodeStatusEnum } from '../sync.js';

describe('registered nodes', () => {
  it('valid registered_node passes', () => {
    const data = {
      node_code: 'NODE-001',
    };
    const result = registeredNodeInsert.parse(data);
    expect(result.node_code).toBe('NODE-001');
  });

  it('invalid node status fails', () => {
    const data = {
      node_code: 'NODE-001',
      status: 'unknown',
    };
    expect(() => registeredNodeInsert.parse(data)).toThrow();
  });
});

describe('sync inbox', () => {
  it('inbox payload with valid JSON passes', () => {
    const data = {
      source_node_id: '550e8400-e29b-41d4-a716-446655440020',
      event_type: 'exam_created',
      aggregate_type: 'exam_version',
      aggregate_id: 'agg-001',
      idempotency_key: 'unique-key-001',
      payload_json: { exam_id: 'abc', title: 'Test' },
    };
    const result = syncInboxInsert.parse(data);
    expect(result.event_type).toBe('exam_created');
    expect(result.payload_json.title).toBe('Test');
  });
});
