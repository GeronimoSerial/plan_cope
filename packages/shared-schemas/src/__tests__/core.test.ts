import { describe, it, expect } from 'vitest';
import {
  userInsert,
  userSelect,
  roleInsert,
  roleSelect,
  provinceInsert,
  provinceSelect,
} from '../core.js';

describe('users', () => {
  it('valid user insert passes', () => {
    const data = {
      email: 'test@example.com',
      password_hash: 'hashed-password',
      full_name: 'Test User',
    };
    const result = userInsert.parse(data);
    expect(result.email).toBe('test@example.com');
    // status should default to active
    expect(result.status).toBeUndefined();
  });

  it('invalid email fails', () => {
    const data = {
      email: 'not-an-email',
      password_hash: 'hashed-password',
      full_name: 'Test User',
    };
    expect(() => userInsert.parse(data)).toThrow();
  });

  it('invalid status enum fails', () => {
    const data = {
      email: 'test@example.com',
      password_hash: 'hashed-password',
      full_name: 'Test User',
      status: 'banned',
    };
    expect(() => userInsert.parse(data)).toThrow();
  });

  it('user select parses full row', () => {
    const data = {
      id: '550e8400-e29b-41d4-a716-446655440000',
      email: 'test@example.com',
      password_hash: 'hash',
      full_name: 'Test User',
      status: 'active',
      last_login_at: null,
      deleted_at: null,
      created_at: '2024-01-15T10:30:00Z',
      updated_at: '2024-01-15T10:30:00Z',
    };
    const result = userSelect.parse(data);
    expect(result.id).toBe('550e8400-e29b-41d4-a716-446655440000');
    expect(result.status).toBe('active');
  });
});

describe('roles', () => {
  it('role insert parses correctly', () => {
    const data = {
      code: 'admin',
      name: 'Administrator',
    };
    const result = roleInsert.parse(data);
    expect(result.code).toBe('admin');
  });

  it('role select parses full row', () => {
    const data = {
      id: '550e8400-e29b-41d4-a716-446655440001',
      code: 'admin',
      name: 'Administrator',
      description: null,
      created_at: '2024-01-15T10:30:00Z',
    };
    const result = roleSelect.parse(data);
    expect(result.code).toBe('admin');
    expect(result.description).toBeNull();
  });
});

describe('provinces', () => {
  it('province with all required fields passes', () => {
    const data = {
      code: 'BA',
      name: 'Buenos Aires',
    };
    const result = provinceInsert.parse(data);
    expect(result.code).toBe('BA');
    expect(result.name).toBe('Buenos Aires');
  });

  it('province select parses full row', () => {
    const data = {
      id: '550e8400-e29b-41d4-a716-446655440002',
      code: 'BA',
      name: 'Buenos Aires',
      created_at: '2024-01-15T10:30:00Z',
    };
    const result = provinceSelect.parse(data);
    expect(result.id).toBe('550e8400-e29b-41d4-a716-446655440002');
  });
});
