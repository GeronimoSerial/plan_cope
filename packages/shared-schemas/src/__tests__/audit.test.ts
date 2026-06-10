import { describe, it, expect } from 'vitest';
import { auditLogInsert, auditLogSelect } from '../audit.js';

describe('audit logs', () => {
  it('valid insert with required fields passes', () => {
    const data = {
      entity_type: 'user',
      entity_id: '550e8400-e29b-41d4-a716-446655440300',
      action: 'create',
    };
    const result = auditLogInsert.parse(data);
    expect(result.entity_type).toBe('user');
    expect(result.action).toBe('create');
  });

  it('valid insert with actor_id and ip_address passes', () => {
    const data = {
      actor_id: '550e8400-e29b-41d4-a716-446655440301',
      entity_type: 'exam',
      entity_id: '550e8400-e29b-41d4-a716-446655440302',
      action: 'update',
      ip_address: '192.168.1.10',
      payload_json: { field: 'title', old: 'A', new: 'B' },
    };
    const result = auditLogInsert.parse(data);
    expect(result.ip_address).toBe('192.168.1.10');
    expect(result.payload_json).toEqual({ field: 'title', old: 'A', new: 'B' });
  });

  it('rejects missing entity_type', () => {
    const data = {
      entity_id: '550e8400-e29b-41d4-a716-446655440300',
      action: 'delete',
    };
    expect(() => auditLogInsert.parse(data)).toThrow();
  });

  it('rejects missing action', () => {
    const data = {
      entity_type: 'user',
      entity_id: '550e8400-e29b-41d4-a716-446655440300',
    };
    expect(() => auditLogInsert.parse(data)).toThrow();
  });

  it('selects full row including ip_address and created_at', () => {
    const data = {
      id: '550e8400-e29b-41d4-a716-446655440303',
      actor_id: null,
      entity_type: 'school',
      entity_id: '550e8400-e29b-41d4-a716-446655440304',
      action: 'archive',
      payload_json: null,
      ip_address: '10.0.0.1',
      created_at: '2024-06-01T12:00:00Z',
    };
    const result = auditLogSelect.parse(data);
    expect(result.id).toBe('550e8400-e29b-41d4-a716-446655440303');
    expect(result.ip_address).toBe('10.0.0.1');
    expect(result.actor_id).toBeNull();
  });
});
