import { describe, expect, it } from 'vitest';
import { ApiError, NetworkError, TimeoutError } from '../api/http';
import {
  classifyRestoreSessionError,
  restoreSessionErrorMessage,
} from '../sessionRestore';

describe('classifyRestoreSessionError', () => {
  it.each([
    'invalid_refresh_token',
    'device_revoked',
    'user_deleted',
  ])('treats %s as terminal', (code) => {
    expect(classifyRestoreSessionError(new ApiError(401, code, 'terminal'))).toBe('terminal');
  });

  it('treats timeout and network failures as transient', () => {
    expect(classifyRestoreSessionError(new TimeoutError())).toBe('transient');
    expect(classifyRestoreSessionError(new NetworkError())).toBe('transient');
  });

  it('treats identity dependency failures as transient', () => {
    expect(classifyRestoreSessionError(new ApiError(
      503,
      'identity_dependency_unavailable',
      'dependency unavailable',
    ))).toBe('transient');
  });
});

describe('restoreSessionErrorMessage', () => {
  it('returns a specific timeout message', () => {
    expect(restoreSessionErrorMessage(new TimeoutError())).toBe(
      'O serviço de login demorou para responder. Tente novamente em alguns segundos.',
    );
  });
});
