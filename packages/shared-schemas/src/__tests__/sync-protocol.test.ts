import { describe, it, expect } from 'vitest';
import {
  SyncPullResponse,
  SyncPushRequest,
  SyncPushResponse,
  AttemptPayload,
} from '../sync.js';

describe('SyncPullResponse', () => {
  it('validates pull response with empty changes', () => {
    const data = {
      cursor: 1234567890,
      changes: {
        examVersions: [],
        examBlocks: [],
        answerKeys: [],
        assets: [],
      },
      checksums: {},
    };
    const result = SyncPullResponse.safeParse(data);
    expect(result.success).toBe(true);
  });

  it('validates pull response with populated changes', () => {
    const data = {
      cursor: 100,
      changes: {
        examVersions: [
          {
            id: '550e8400-e29b-41d4-a716-446655440100',
            exam_id: '550e8400-e29b-41d4-a716-446655440101',
            version_number: 1,
            schema_version: 1,
            status: 'published',
            created_at: '2024-06-01T12:00:00Z',
            updated_at: '2024-06-01T12:00:00Z',
          },
        ],
        examBlocks: [
          {
            id: '550e8400-e29b-41d4-a716-446655440102',
            exam_version_id: '550e8400-e29b-41d4-a716-446655440100',
            order_index: 0,
            block_type: 'text',
            config_json: { text: 'hello' },
            created_at: '2024-06-01T12:00:00Z',
            updated_at: '2024-06-01T12:00:00Z',
          },
        ],
        answerKeys: [],
        assets: [],
      },
      checksums: { v1: 'abc123' },
    };
    const result = SyncPullResponse.safeParse(data);
    expect(result.success).toBe(true);
  });

  it('rejects negative cursor', () => {
    const data = {
      cursor: -1,
      changes: { examVersions: [], examBlocks: [], answerKeys: [], assets: [] },
      checksums: {},
    };
    const result = SyncPullResponse.safeParse(data);
    expect(result.success).toBe(false);
  });

  it('rejects missing changes field', () => {
    const data = { cursor: 0, checksums: {} };
    const result = SyncPullResponse.safeParse(data);
    expect(result.success).toBe(false);
  });
});

describe('AttemptPayload', () => {
  it('validates attempt with one answer', () => {
    const data = {
      remote_local_id: 'local-001',
      student_code: 'STU-001',
      status: 'submitted' as const,
      answers: [
        {
          block_id: '550e8400-e29b-41d4-a716-446655440200',
          answer_json: { selected: 'a' },
        },
      ],
    };
    const result = AttemptPayload.safeParse(data);
    expect(result.success).toBe(true);
  });
});

describe('SyncPushRequest', () => {
  it('validates push request with multiple attempts', () => {
    const data = {
      idempotencyKey: '550e8400-e29b-41d4-a716-446655440999',
      attempts: [
        {
          remote_local_id: 'local-001',
          student_code: 'STU-001',
          status: 'submitted' as const,
          answers: [
            {
              block_id: '550e8400-e29b-41d4-a716-446655440200',
              answer_json: { selected: 'a' },
            },
          ],
        },
        {
          remote_local_id: 'local-002',
          student_code: 'STU-002',
          status: 'submitted' as const,
          answers: [],
        },
      ],
    };
    const result = SyncPushRequest.safeParse(data);
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.attempts.length).toBe(2);
    }
  });

  it('rejects push request missing idempotencyKey', () => {
    const data = {
      attempts: [
        {
          remote_local_id: 'local-001',
          student_code: 'STU-001',
          status: 'submitted' as const,
          answers: [],
        },
      ],
    };
    const result = SyncPushRequest.safeParse(data);
    expect(result.success).toBe(false);
  });

  it('rejects push request with non-UUID idempotencyKey', () => {
    const data = {
      idempotencyKey: 'not-a-uuid',
      attempts: [],
    };
    const result = SyncPushRequest.safeParse(data);
    expect(result.success).toBe(false);
  });

  it('rejects push request with empty attempts array', () => {
    const data = {
      idempotencyKey: '550e8400-e29b-41d4-a716-446655440999',
      attempts: [],
    };
    const result = SyncPushRequest.safeParse(data);
    expect(result.success).toBe(false);
  });
});

describe('SyncPushResponse', () => {
  it('validates response with all accepted', () => {
    const data = {
      accepted: 5,
      rejected: [],
      cursor: 5000,
    };
    const result = SyncPushResponse.safeParse(data);
    expect(result.success).toBe(true);
  });

  it('validates response with rejected items', () => {
    const data = {
      accepted: 2,
      rejected: [
        { remote_local_id: 'local-bad-001', reason: 'duplicate idempotency_key' },
      ],
      cursor: 5001,
    };
    const result = SyncPushResponse.safeParse(data);
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.rejected.length).toBe(1);
    }
  });

  it('rejects negative accepted count', () => {
    const data = { accepted: -1, rejected: [], cursor: 0 };
    const result = SyncPushResponse.safeParse(data);
    expect(result.success).toBe(false);
  });
});
