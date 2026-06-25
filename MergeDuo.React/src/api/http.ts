/**
 * Shared HTTP client used by every microservice client.
 *
 * Centralizes:
 *  - Base URL composition and trailing-slash sanitisation
 *  - JSON / FormData body handling
 *  - `credentials: 'include'` for cookie-based CSRF
 *  - `Accept: application/json`
 *  - `Idempotency-Key` for non-idempotent writes
 *  - `If-Match` ETag preconditions
 *  - Request timeout via `AbortController`
 *  - Typed `ProblemDetails` parsing
 *  - Centralised `401` notification (session expired)
 */

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  instance?: string;
  [extension: string]: unknown;
}

export class ApiError extends Error {
  public readonly status: number;
  public readonly code: string;
  public readonly problem: ProblemDetails | null;

  constructor(
    status: number,
    code: string,
    message: string,
    problem: ProblemDetails | null = null,
  ) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.problem = problem;
  }
}

export class TimeoutError extends ApiError {
  constructor(message = 'A conexão está lenta demais. Tente novamente.') {
    super(408, 'request_timeout', message);
    this.name = 'TimeoutError';
  }
}

export class NetworkError extends ApiError {
  constructor(message = 'Falha de rede ao chamar o serviço.') {
    super(0, 'network_error', message);
    this.name = 'NetworkError';
  }
}

export type UnauthorizedHandler = (error: ApiError) => void;

let unauthorizedHandler: UnauthorizedHandler | null = null;

/**
 * Lightweight in-memory ring buffer of recent API events. Used by the
 * AuthView diagnostic panel so users on devices without remote DevTools
 * (e.g. iOS PWA without a Mac) can inspect what happened around a 401.
 */
export interface AuthDiagnosticEvent {
  ts: number;
  message: string;
}

const DIAG_LIMIT = 60;
const diagBuffer: AuthDiagnosticEvent[] = [];
const diagListeners = new Set<() => void>();

export function recordAuthDiagnostic(message: string): void {
  diagBuffer.push({ ts: Date.now(), message });
  if (diagBuffer.length > DIAG_LIMIT) diagBuffer.splice(0, diagBuffer.length - DIAG_LIMIT);
  for (const listener of diagListeners) {
    try { listener(); } catch { /* ignore */ }
  }
}

export function getAuthDiagnostics(): AuthDiagnosticEvent[] {
  return diagBuffer.slice();
}

export function subscribeAuthDiagnostics(listener: () => void): () => void {
  diagListeners.add(listener);
  return () => { diagListeners.delete(listener); };
}

export function clearAuthDiagnostics(): void {
  diagBuffer.length = 0;
  for (const listener of diagListeners) {
    try { listener(); } catch { /* ignore */ }
  }
}

function shortPath(baseUrl: string, path: string): string {
  // Try to extract host short tag + path so we can tell which microservice
  // produced the event without leaking full base URLs.
  try {
    const u = new URL(path, baseUrl.endsWith('/') ? baseUrl : baseUrl + '/');
    const host = u.host.split('.')[0] || u.host;
    return `${host}${u.pathname}`;
  } catch {
    return path;
  }
}

/**
 * Register a global handler invoked whenever any client receives a 401 response.
 *
 * The handler is responsible for clearing in-memory tokens, clearing financial
 * caches and routing the user back to the login screen. It is invoked
 * asynchronously on a microtask so callers can still observe the rejected
 * promise themselves.
 */
export function setUnauthorizedHandler(handler: UnauthorizedHandler | null): void {
  unauthorizedHandler = handler;
}

export interface RequestOptions extends Omit<RequestInit, 'body' | 'headers'> {
  baseUrl: string;
  path: string;
  method?: string;
  query?: Record<string, string | number | boolean | null | undefined>;
  headers?: Record<string, string | undefined>;
  /** Plain JS value JSON-encoded into the body. Cannot be combined with formData. */
  json?: unknown;
  /** FormData payload. Cannot be combined with json. */
  formData?: FormData;
  /** Bearer access token to authenticate the request. */
  accessToken?: string | null;
  /** ETag to send as `If-Match` for optimistic concurrency. */
  ifMatch?: string | null;
  /** Send an `Idempotency-Key` header for safe retries (writes only). */
  idempotencyKey?: string | null;
  /** Timeout in milliseconds. Defaults to 15s. */
  timeoutMs?: number;
  /** Default error code prefix used when the server omits a `code`. */
  defaultErrorCode?: string;
  /** Disable invoking the unauthorized handler for this request (auth flows). */
  suppressUnauthorizedHandler?: boolean;
}

export interface RequestControlOptions {
  signal?: AbortSignal;
  timeoutMs?: number;
}

const DEFAULT_TIMEOUT_MS = 15_000;

export async function apiFetch<T>(options: RequestOptions): Promise<T> {
  const url = buildUrl(options.baseUrl, options.path, options.query);
  const headers = new Headers();
  headers.set('accept', 'application/json');
  const startedAt = nowMs();

  if (options.accessToken) {
    headers.set('authorization', `Bearer ${options.accessToken}`);
  }

  if (options.ifMatch) {
    headers.set('if-match', options.ifMatch);
  }

  let body: BodyInit | undefined;
  if (options.json !== undefined) {
    headers.set('content-type', 'application/json');
    body = JSON.stringify(options.json);
  } else if (options.formData) {
    body = options.formData;
  }

  const method = (options.method ?? 'GET').toUpperCase();
  if (options.idempotencyKey && method !== 'GET' && method !== 'HEAD') {
    headers.set('idempotency-key', options.idempotencyKey);
  }

  if (options.headers) {
    for (const [name, value] of Object.entries(options.headers)) {
      if (value !== undefined) headers.set(name, value);
    }
  }

  const controller = new AbortController();
  const timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  let didTimeout = false;
  const timer = setTimeout(() => {
    didTimeout = true;
    controller.abort();
  }, timeoutMs);
  const abortFromExternalSignal = () => controller.abort();
  if (options.signal?.aborted) {
    abortFromExternalSignal();
  } else {
    options.signal?.addEventListener('abort', abortFromExternalSignal, { once: true });
  }

  const tag = shortPath(options.baseUrl, options.path);
  let response: Response;
  try {
    response = await fetch(url, {
      ...options,
      method,
      headers,
      body,
      credentials: 'include',
      signal: controller.signal,
    });
  } catch (err) {
    clearTimeout(timer);
    options.signal?.removeEventListener('abort', abortFromExternalSignal);
    if (didTimeout) {
      recordAuthDiagnostic(`${method} ${tag} -> timeout ${timeoutMs}ms`);
      throw new TimeoutError();
    }
    if (err instanceof DOMException && err.name === 'AbortError') {
      throw err;
    }
    recordAuthDiagnostic(`${method} ${tag} -> network_error ${elapsedMs(startedAt)}ms`);
    throw new NetworkError(resolveNetworkErrorMessage(err));
  }
  clearTimeout(timer);
  options.signal?.removeEventListener('abort', abortFromExternalSignal);

  if (!response.ok) {
    const apiError = await toApiError(response, options.defaultErrorCode ?? 'api_error');
    recordAuthDiagnostic(`${method} ${tag} -> ${apiError.status} ${apiError.code} ${elapsedMs(startedAt)}ms`);
    if (apiError.status === 401 && !options.suppressUnauthorizedHandler) {
      const handler = unauthorizedHandler;
      if (handler) queueMicrotask(() => handler(apiError));
    }
    throw apiError;
  }

  recordAuthDiagnostic(`${method} ${tag} -> ${response.status} ${elapsedMs(startedAt)}ms`);

  if (response.status === 204) {
    return undefined as T;
  }

  const contentType = response.headers.get('content-type') ?? '';
  if (contentType.includes('application/json')) {
    return (await response.json()) as T;
  }

  // Fall back to text for non-JSON success responses; rare in this API surface.
  return (await response.text()) as unknown as T;
}

function buildUrl(
  baseUrl: string,
  path: string,
  query?: RequestOptions['query'],
): string {
  const base = baseUrl.replace(/\/+$/, '');
  const suffix = path.startsWith('/') ? path : `/${path}`;
  if (!query) return `${base}${suffix}`;

  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value === null || value === undefined) continue;
    params.set(key, String(value));
  }

  const queryString = params.toString();
  return queryString ? `${base}${suffix}?${queryString}` : `${base}${suffix}`;
}

function nowMs(): number {
  return typeof performance !== 'undefined' ? performance.now() : Date.now();
}

function elapsedMs(startedAt: number): number {
  return Math.max(0, Math.round(nowMs() - startedAt));
}

async function toApiError(response: Response, defaultCode: string): Promise<ApiError> {
  let problem: ProblemDetails | null = null;
  try {
    const text = await response.text();
    if (text) {
      problem = JSON.parse(text) as ProblemDetails;
    }
  } catch {
    problem = null;
  }

  const code = problem?.code || problem?.title || defaultCode;
  const detail = problem?.detail || problem?.title || response.statusText || code;
  return new ApiError(response.status, code, detail, problem);
}

export function newIdempotencyKey(): string {
  if (globalThis.crypto?.randomUUID) return globalThis.crypto.randomUUID();
  return `${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

export function sanitizeBaseUrl(value: string | undefined, fallback: string): string {
  return (value && value.trim().length > 0 ? value : fallback).replace(/\/+$/, '');
}

function resolveNetworkErrorMessage(err: unknown): string {
  if (typeof navigator !== 'undefined' && navigator.onLine === false) {
    return 'Sem conexão. Verifique sua internet e tente novamente.';
  }

  if (err instanceof Error && err.message.trim().length > 0) {
    return err.message;
  }

  return 'Falha de rede ao chamar o serviço.';
}
