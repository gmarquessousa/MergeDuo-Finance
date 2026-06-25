import { ApiError, NetworkError, TimeoutError } from './api/http';

export type RestoreSessionErrorKind = 'terminal' | 'transient';

const TERMINAL_REFRESH_CODES = new Set([
  'invalid_refresh_token',
  'device_revoked',
  'user_deleted',
]);

export function classifyRestoreSessionError(error: unknown): RestoreSessionErrorKind {
  if (error instanceof TimeoutError || error instanceof NetworkError) {
    return 'transient';
  }

  if (error instanceof ApiError) {
    if (TERMINAL_REFRESH_CODES.has(error.code)) {
      return 'terminal';
    }

    if (
      error.code === 'identity_dependency_unavailable' ||
      error.status === 0 ||
      error.status === 408 ||
      error.status >= 500
    ) {
      return 'transient';
    }

    return 'terminal';
  }

  return 'transient';
}

export function restoreSessionErrorMessage(error: unknown): string {
  if (error instanceof TimeoutError || isApiCode(error, 'request_timeout')) {
    return 'O serviço de login demorou para responder. Tente novamente em alguns segundos.';
  }

  if (error instanceof NetworkError || isApiCode(error, 'network_error')) {
    return 'Não foi possível entrar direto agora. Verifique sua conexão e tente novamente.';
  }

  if (isApiCode(error, 'identity_dependency_unavailable')) {
    return 'O serviço de login está indisponível no momento. Tente novamente em alguns segundos.';
  }

  return 'Não foi possível entrar direto agora. Você pode tentar novamente ou continuar com Google.';
}

function isApiCode(error: unknown, code: string): boolean {
  return error instanceof ApiError && error.code === code;
}
