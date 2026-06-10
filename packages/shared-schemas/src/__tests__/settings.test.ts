import { describe, it, expect } from 'vitest';
import { settingInsert, settingSelect } from '../settings.js';

describe('settings', () => {
  it('valid insert with key and value passes', () => {
    const data = {
      key: 'sync.interval.seconds',
      value_json: { value: 30 },
    };
    const result = settingInsert.parse(data);
    expect(result.key).toBe('sync.interval.seconds');
  });

  it('valid insert with updated_by passes', () => {
    const data = {
      key: 'central.url',
      value_json: 'https://central.example.com',
      updated_by: '550e8400-e29b-41d4-a716-446655440400',
    };
    const result = settingInsert.parse(data);
    expect(result.updated_by).toBe('550e8400-e29b-41d4-a716-446655440400');
  });

  it('rejects empty key', () => {
    const data = { key: '', value_json: {} };
    expect(() => settingInsert.parse(data)).toThrow();
  });

  it('rejects missing value_json', () => {
    const data = { key: 'foo' };
    expect(() => settingInsert.parse(data)).toThrow();
  });

  it('selects full row', () => {
    const data = {
      id: '550e8400-e29b-41d4-a716-446655440401',
      key: 'jwt.expiry.minutes',
      value_json: { value: 60 },
      updated_by: null,
      created_at: '2024-06-01T12:00:00Z',
      updated_at: '2024-06-01T12:00:00Z',
    };
    const result = settingSelect.parse(data);
    expect(result.id).toBe('550e8400-e29b-41d4-a716-446655440401');
    expect(result.updated_by).toBeNull();
  });
});
