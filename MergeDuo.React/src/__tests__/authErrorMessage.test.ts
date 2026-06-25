import { describe, expect, it } from 'vitest';
import { ApiError, TimeoutError } from '../api/http';
import { authErrorMessage } from '../authErrorMessage';

describe('authErrorMessage', () => {
  it('maps request timeouts to a login-specific message', () => {
    expect(authErrorMessage(new TimeoutError())).toBe(
      'O serviço de login demorou para responder. Tente novamente em alguns segundos.',
    );
  });

  it('maps identity dependency failures to a service message', () => {
    expect(authErrorMessage(new ApiError(
      503,
      'identity_dependency_unavailable',
      'Dependency unavailable.',
    ))).toBe(
      'O serviço de login está indisponível no momento. Tente novamente em alguns segundos.',
    );
  });

  it('maps invalid challenge errors', () => {
    expect(authErrorMessage(new ApiError(400, 'invalid_challenge', 'Invalid challenge.'))).toBe(
      'Sua tentativa expirou. Tente novamente.',
    );
  });

  it('maps invalid Google token errors', () => {
    expect(authErrorMessage(new ApiError(401, 'invalid_google_token', 'Invalid token.'))).toBe(
      'Login Google inválido. Tente novamente.',
    );
  });
});
