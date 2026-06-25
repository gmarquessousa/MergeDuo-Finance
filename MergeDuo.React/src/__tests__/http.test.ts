import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  ApiError,
  TimeoutError,
  apiFetch,
  clearAuthDiagnostics,
  getAuthDiagnostics,
  newIdempotencyKey,
  sanitizeBaseUrl,
  setUnauthorizedHandler,
} from '../api/http';

const ORIGINAL_FETCH = globalThis.fetch;

afterEach(() => {
  globalThis.fetch = ORIGINAL_FETCH;
  clearAuthDiagnostics();
  setUnauthorizedHandler(null);
  vi.restoreAllMocks();
});

function jsonResponse(body: unknown, init: ResponseInit = {}) {
  return new Response(JSON.stringify(body), {
    ...init,
    headers: { 'content-type': 'application/json', ...(init.headers ?? {}) },
  });
}

describe('apiFetch', () => {
  it('parses ProblemDetails into ApiError', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(
      jsonResponse(
        { code: 'not_found', detail: 'no card', title: 'Not Found' },
        { status: 404, statusText: 'Not Found' },
      ),
    );

    await expect(
      apiFetch({ baseUrl: 'https://test', path: '/cards/1', defaultErrorCode: 'cards_api_error' }),
    ).rejects.toMatchObject({ status: 404, code: 'not_found', message: 'no card' });
  });

  it('sends Idempotency-Key for non-GET when provided', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }));
    globalThis.fetch = fetchMock;

    await apiFetch({
      baseUrl: 'https://test',
      path: '/goals',
      method: 'POST',
      json: { title: 'x' },
      idempotencyKey: 'abc-123',
    });

    const init = fetchMock.mock.calls[0][1] as RequestInit;
    const headers = init.headers as Headers;
    expect(headers.get('idempotency-key')).toBe('abc-123');
    expect(headers.get('content-type')).toBe('application/json');
  });

  it('does not send Idempotency-Key on GET', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }));
    globalThis.fetch = fetchMock;

    await apiFetch({
      baseUrl: 'https://test',
      path: '/goals',
      idempotencyKey: 'abc-123',
    });

    const headers = (fetchMock.mock.calls[0][1] as RequestInit).headers as Headers;
    expect(headers.get('idempotency-key')).toBeNull();
  });

  it('sends If-Match header when provided', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({}));
    globalThis.fetch = fetchMock;

    await apiFetch({
      baseUrl: 'https://test',
      path: '/goals/1',
      method: 'PATCH',
      json: {},
      ifMatch: 'W/"etag-1"',
    });

    const headers = (fetchMock.mock.calls[0][1] as RequestInit).headers as Headers;
    expect(headers.get('if-match')).toBe('W/"etag-1"');
  });

  it('invokes the unauthorized handler on 401 unless suppressed', async () => {
    const handler = vi.fn();
    setUnauthorizedHandler(handler);
    globalThis.fetch = vi.fn().mockResolvedValue(
      jsonResponse({ code: 'unauthorized', detail: 'no' }, { status: 401 }),
    );

    await expect(
      apiFetch({ baseUrl: 'https://test', path: '/me' }),
    ).rejects.toBeInstanceOf(ApiError);

    await new Promise((r) => queueMicrotask(() => r(null)));
    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('does not invoke the unauthorized handler when suppressed', async () => {
    const handler = vi.fn();
    setUnauthorizedHandler(handler);
    globalThis.fetch = vi.fn().mockResolvedValue(
      jsonResponse({ code: 'unauthorized', detail: 'no' }, { status: 401 }),
    );

    await expect(
      apiFetch({
        baseUrl: 'https://test',
        path: '/auth/refresh',
        suppressUnauthorizedHandler: true,
      }),
    ).rejects.toBeInstanceOf(ApiError);

    await new Promise((r) => queueMicrotask(() => r(null)));
    expect(handler).not.toHaveBeenCalled();
  });

  it('translates AbortController timeouts into TimeoutError', async () => {
    globalThis.fetch = vi.fn().mockImplementation((_url, init: RequestInit) => {
      return new Promise((_resolve, reject) => {
        init.signal?.addEventListener('abort', () => {
          const err = new DOMException('aborted', 'AbortError');
          reject(err);
        });
      });
    });

    await expect(
      apiFetch({ baseUrl: 'https://test', path: '/slow', timeoutMs: 5 }),
    ).rejects.toBeInstanceOf(TimeoutError);
    expect(getAuthDiagnostics().at(-1)?.message).toBe('GET test/slow -> timeout 5ms');
  });

  it('records elapsed time in diagnostics for successful responses', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(jsonResponse({ ok: true }));

    await apiFetch({ baseUrl: 'https://identity.test', path: '/auth/google/challenge' });

    expect(getAuthDiagnostics().at(-1)?.message).toMatch(
      /^GET identity\/auth\/google\/challenge -> 200 \d+ms$/,
    );
  });

  it('propagates external AbortSignal cancellation without converting it to timeout', async () => {
    globalThis.fetch = vi.fn().mockImplementation((_url, init: RequestInit) => {
      return new Promise((_resolve, reject) => {
        init.signal?.addEventListener('abort', () => {
          reject(new DOMException('aborted', 'AbortError'));
        });
      });
    });

    const controller = new AbortController();
    const request = apiFetch({
      baseUrl: 'https://test',
      path: '/cancelled',
      signal: controller.signal,
      timeoutMs: 1000,
    });
    controller.abort();

    await expect(request).rejects.toMatchObject({ name: 'AbortError' });
  });
});

describe('helpers', () => {
  beforeEach(() => {
    setUnauthorizedHandler(null);
  });

  it('sanitizeBaseUrl trims trailing slashes and falls back when empty', () => {
    expect(sanitizeBaseUrl('https://x.com/', 'fb')).toBe('https://x.com');
    expect(sanitizeBaseUrl('   ', 'https://fb.com/')).toBe('https://fb.com');
  });

  it('newIdempotencyKey returns a non-empty unique-ish string', () => {
    const a = newIdempotencyKey();
    const b = newIdempotencyKey();
    expect(typeof a).toBe('string');
    expect(a.length).toBeGreaterThan(8);
    expect(a).not.toBe(b);
  });
});
