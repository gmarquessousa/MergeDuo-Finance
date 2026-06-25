import { ApiError, apiFetch, type RequestControlOptions } from './http';
import { appConfig } from './config';

const CSRF_STORAGE_KEY = 'mergeduo.identity.csrf';
const INSTALL_ID_KEY = 'mergeduo.identity.installId';
const GOOGLE_CALLBACK_TIMEOUT_MS = 30_000;

export const identityApiBaseUrl = appConfig.identityApiBaseUrl;
export const googleClientId = appConfig.googleClientId;

export interface GoogleChallengeResponse {
  nonce: string;
  csrfToken: string;
  expiresIn: number;
  challengeToken: string;
}

export interface GoogleRedirectStartResponse {
  nonce: string;
  state: string;
  loginUri: string;
  expiresIn: number;
}

export interface GoogleRedirectStartInput {
  rememberMe: boolean;
  returnPath?: string;
}

export interface UserSummaryResponse {
  id: string;
  name: string;
  handle: string;
  email: string;
  avatarUrl: string | null;
}

export interface UserPreferences {
  darkMode: boolean;
  weeklyReport: boolean;
}

export interface UserFinancial {
  startingBalance: number;
  currency: string;
}

export interface UserStats {
  transactionsTracked: number;
  activeMonths: number;
  lastRecomputedAt: string | null;
}

export interface UserMeResponse extends UserSummaryResponse {
  phone: string | null;
  avatarInitials: string;
  memberSince: string;
  registeredAt: string;
  financial: UserFinancial;
  preferences: UserPreferences;
  stats: UserStats;
  createdAt: string;
  updatedAt: string;
  deletedAt: string | null;
}

export interface AuthSession {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
  csrfToken: string;
  user: UserMeResponse;
  deviceId: string;
}

export interface StoredAuthSession {
  csrfToken: string;
  rememberSession: boolean;
}

interface DeviceRequest {
  installId: string;
  platform: string;
  userAgent: string;
  model: string;
  osVersion: string;
  appVersion: string;
}

interface GoogleCallbackInput {
  idToken: string;
  csrfToken: string;
  challengeToken: string;
  rememberMe: boolean;
}

/** @deprecated Prefer the shared {@link ApiError} from `./http`; kept for compatibility. */
export class IdentityApiError extends ApiError {
  constructor(status: number, code: string, message: string) {
    super(status, code, message);
    this.name = 'IdentityApiError';
  }
}

export function loadStoredAuthSession(): StoredAuthSession | null {
  if (typeof window === 'undefined') return null;

  const sessionCsrf = window.sessionStorage.getItem(CSRF_STORAGE_KEY);
  if (sessionCsrf) {
    return { csrfToken: sessionCsrf, rememberSession: false };
  }

  const localCsrf = window.localStorage.getItem(CSRF_STORAGE_KEY);
  return localCsrf ? { csrfToken: localCsrf, rememberSession: true } : null;
}

export function persistAuthSession(session: AuthSession, rememberSession: boolean) {
  if (typeof window === 'undefined') return;

  if (rememberSession) {
    window.localStorage.setItem(CSRF_STORAGE_KEY, session.csrfToken);
    window.sessionStorage.removeItem(CSRF_STORAGE_KEY);
    return;
  }

  window.sessionStorage.setItem(CSRF_STORAGE_KEY, session.csrfToken);
  window.localStorage.removeItem(CSRF_STORAGE_KEY);
}

export function clearStoredAuthSession() {
  if (typeof window === 'undefined') return;
  window.localStorage.removeItem(CSRF_STORAGE_KEY);
  window.sessionStorage.removeItem(CSRF_STORAGE_KEY);
}

export interface AuthRedirectHandoff {
  csrfToken: string;
  rememberSession: boolean;
}

export function readAuthRedirectHandoff(hash = typeof window === 'undefined' ? '' : window.location.hash): AuthRedirectHandoff | null {
  const raw = hash.startsWith('#') ? hash.slice(1) : hash;
  if (!raw) return null;

  const params = new URLSearchParams(raw);
  if (params.get('auth_redirect') !== '1') return null;

  const csrfToken = params.get('csrf');
  if (!csrfToken) return null;

  const remember = params.get('remember');
  return {
    csrfToken,
    rememberSession: remember === '1' || remember === 'true',
  };
}

export function clearAuthRedirectHandoff() {
  if (typeof window === 'undefined') return;
  window.history.replaceState(null, document.title, `${window.location.pathname}${window.location.search}`);
}

export async function beginGoogleLogin(options?: RequestControlOptions): Promise<GoogleChallengeResponse> {
  const response = await request<unknown>({
    path: '/auth/google/challenge',
    method: 'GET',
    suppressUnauthorizedHandler: true,
    ...options,
  });

  return parseGoogleChallengeResponse(response);
}

export async function beginGoogleRedirectLogin(
  input: GoogleRedirectStartInput,
  options?: RequestControlOptions,
): Promise<GoogleRedirectStartResponse> {
  const response = await request<unknown>({
    path: '/auth/google/redirect/start',
    method: 'POST',
    json: {
      rememberMe: input.rememberMe,
      returnPath: input.returnPath ?? '/',
      device: buildDeviceRequest(),
    },
    suppressUnauthorizedHandler: true,
    ...options,
  });

  return parseGoogleRedirectStartResponse(response);
}

export async function completeGoogleLogin(
  input: GoogleCallbackInput,
  options?: RequestControlOptions,
): Promise<AuthSession> {
  return request<AuthSession>({
    path: '/auth/google/callback',
    method: 'POST',
    headers: { 'x-csrf-token': input.csrfToken },
    json: {
      idToken: input.idToken,
      rememberMe: input.rememberMe,
      challengeToken: input.challengeToken,
      device: buildDeviceRequest(),
    },
    suppressUnauthorizedHandler: true,
    timeoutMs: GOOGLE_CALLBACK_TIMEOUT_MS,
    ...options,
  });
}

export async function refreshAuthSession(
  csrfToken: string,
  options?: RequestControlOptions,
): Promise<AuthSession> {
  return request<AuthSession>({
    path: '/auth/refresh',
    method: 'POST',
    headers: { 'x-csrf-token': csrfToken },
    suppressUnauthorizedHandler: true,
    ...options,
  });
}

export async function logoutAuthSession(
  accessToken: string,
  options?: RequestControlOptions,
): Promise<void> {
  await request<void>({
    path: '/auth/logout',
    method: 'POST',
    accessToken,
    suppressUnauthorizedHandler: true,
    ...options,
  });
}

export async function getCurrentUser(
  accessToken: string,
  options?: RequestControlOptions,
): Promise<UserMeResponse> {
  return request<UserMeResponse>({
    path: '/users/me',
    method: 'GET',
    accessToken,
    ...options,
  });
}

export async function uploadUserAvatar(
  accessToken: string,
  file: File,
  options?: RequestControlOptions,
): Promise<{ avatarUrl: string }> {
  const form = new FormData();
  form.append('avatar', file);

  return request<{ avatarUrl: string }>({
    path: '/users/me/avatar',
    method: 'POST',
    accessToken,
    formData: form,
    ...options,
  });
}

interface IdentityRequestOptions {
  path: string;
  method: string;
  accessToken?: string;
  headers?: Record<string, string>;
  json?: unknown;
  formData?: FormData;
  suppressUnauthorizedHandler?: boolean;
  signal?: AbortSignal;
  timeoutMs?: number;
}

function request<T>(options: IdentityRequestOptions): Promise<T> {
  return apiFetch<T>({
    baseUrl: identityApiBaseUrl,
    defaultErrorCode: 'identity_api_error',
    ...options,
  });
}

function parseGoogleChallengeResponse(value: unknown): GoogleChallengeResponse {
  const data = readObject(value);
  const nonce = readString(data.nonce);
  const csrfToken = readString(data.csrfToken);
  const challengeToken = readString(data.challengeToken);
  const expiresIn = readPositiveSeconds(data.expiresIn);

  if (!nonce || !csrfToken || !challengeToken || expiresIn === null) {
    throw invalidIdentityProxyResponse();
  }

  return { nonce, csrfToken, challengeToken, expiresIn };
}

function parseGoogleRedirectStartResponse(value: unknown): GoogleRedirectStartResponse {
  const data = readObject(value);
  const nonce = readString(data.nonce);
  const state = readString(data.state);
  const loginUri = readString(data.loginUri);
  const expiresIn = readPositiveSeconds(data.expiresIn);

  if (!nonce || !state || !loginUri || expiresIn === null) {
    throw invalidIdentityProxyResponse();
  }

  return { nonce, state, loginUri, expiresIn };
}

function readObject(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {};
}

function readString(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value : null;
}

function readPositiveSeconds(value: unknown): number | null {
  if (typeof value !== 'number' || !Number.isFinite(value) || value <= 0) return null;
  return value;
}

function invalidIdentityProxyResponse(): ApiError {
  return new ApiError(
    502,
    'invalid_identity_proxy_response',
    'A rota de login retornou uma resposta inválida. Verifique se /auth está sendo encaminhado para o Identity.',
  );
}

function buildDeviceRequest(): DeviceRequest {
  return {
    installId: getInstallId(),
    platform: 'web',
    userAgent: navigator.userAgent,
    model: navigator.platform || 'browser',
    osVersion: navigator.userAgent,
    appVersion: appConfig.appVersion,
  };
}

function getInstallId() {
  const stored = window.localStorage.getItem(INSTALL_ID_KEY);
  if (stored) return stored;

  const generated = crypto.randomUUID();
  window.localStorage.setItem(INSTALL_ID_KEY, generated);
  return generated;
}
