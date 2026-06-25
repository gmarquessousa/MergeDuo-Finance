import { ApiError, TimeoutError } from './api/http';

export function authErrorMessage(error: unknown): string {
  if (error instanceof TimeoutError || isApiCode(error, 'request_timeout')) {
    return 'O serviço de login demorou para responder. Tente novamente em alguns segundos.';
  }

  if (isApiCode(error, 'identity_dependency_unavailable')) {
    return 'O serviço de login está indisponível no momento. Tente novamente em alguns segundos.';
  }

  if (isApiCode(error, 'invalid_challenge')) {
    return 'Sua tentativa expirou. Tente novamente.';
  }

  if (isApiCode(error, 'invalid_google_token')) {
    return 'Login Google inválido. Tente novamente.';
  }

  if (isApiCode(error, 'invalid_identity_proxy_response')) {
    return 'A rota de login está respondendo com o app em vez do serviço Identity. Publique o proxy /auth e tente novamente.';
  }

  if (error instanceof ApiError) {
    return error.message;
  }

  return error instanceof Error ? error.message : 'Não foi possível iniciar o login.';
}

function isApiCode(error: unknown, code: string): boolean {
  return error instanceof ApiError && error.code === code;
}
