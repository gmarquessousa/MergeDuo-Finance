import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  beginGoogleRedirectLogin,
  clearAuthRedirectHandoff,
  clearStoredAuthSession,
  loadStoredAuthSession,
  persistAuthSession,
  readAuthRedirectHandoff,
  type AuthSession,
} from '../api/identity';

const session: AuthSession = {
  accessToken: 'access-token',
  tokenType: 'Bearer',
  expiresIn: 900,
  csrfToken: 'csrf-token',
  deviceId: 'device-1',
  user: {
    id: 'user-1',
    name: 'Ana',
    handle: 'ana',
    email: 'ana@example.test',
    avatarUrl: null,
    phone: null,
    avatarInitials: 'A',
    memberSince: '2026-01-01',
    registeredAt: '2026-01-01T00:00:00.000Z',
    financial: {
      startingBalance: 0,
      currency: 'BRL',
    },
    preferences: {
      darkMode: false,
      weeklyReport: false,
    },
    stats: {
      transactionsTracked: 0,
      activeMonths: 0,
      lastRecomputedAt: null,
    },
    createdAt: '2026-01-01T00:00:00.000Z',
    updatedAt: '2026-01-01T00:00:00.000Z',
    deletedAt: null,
  },
};

describe('auth session storage helpers', () => {
  beforeEach(() => {
    clearStoredAuthSession();
  });

  afterEach(() => {
    clearStoredAuthSession();
  });

  it('stores remembered sessions in localStorage', () => {
    persistAuthSession(session, true);

    expect(loadStoredAuthSession()).toEqual({
      csrfToken: 'csrf-token',
      rememberSession: true,
    });
    expect(window.localStorage.getItem('mergeduo.identity.csrf')).toBe('csrf-token');
    expect(window.sessionStorage.getItem('mergeduo.identity.csrf')).toBeNull();
  });

  it('stores non-remembered sessions in sessionStorage', () => {
    persistAuthSession(session, false);

    expect(loadStoredAuthSession()).toEqual({
      csrfToken: 'csrf-token',
      rememberSession: false,
    });
    expect(window.sessionStorage.getItem('mergeduo.identity.csrf')).toBe('csrf-token');
    expect(window.localStorage.getItem('mergeduo.identity.csrf')).toBeNull();
  });

  it('prioritizes sessionStorage over localStorage when both exist', () => {
    window.localStorage.setItem('mergeduo.identity.csrf', 'local-csrf');
    window.sessionStorage.setItem('mergeduo.identity.csrf', 'session-csrf');

    expect(loadStoredAuthSession()).toEqual({
      csrfToken: 'session-csrf',
      rememberSession: false,
    });
  });

  it('clears both storage locations', () => {
    window.localStorage.setItem('mergeduo.identity.csrf', 'local-csrf');
    window.sessionStorage.setItem('mergeduo.identity.csrf', 'session-csrf');

    clearStoredAuthSession();

    expect(loadStoredAuthSession()).toBeNull();
    expect(window.localStorage.getItem('mergeduo.identity.csrf')).toBeNull();
    expect(window.sessionStorage.getItem('mergeduo.identity.csrf')).toBeNull();
  });

  it('reads redirect auth handoff from the URL fragment', () => {
    expect(readAuthRedirectHandoff('#auth_redirect=1&csrf=csrf-token&remember=1')).toEqual({
      csrfToken: 'csrf-token',
      rememberSession: true,
    });
  });

  it('clears only the redirect fragment from the current URL', () => {
    window.history.replaceState(null, document.title, '/invites/inv_123?x=1#auth_redirect=1&csrf=csrf-token&remember=1');

    clearAuthRedirectHandoff();

    expect(window.location.pathname).toBe('/invites/inv_123');
    expect(window.location.search).toBe('?x=1');
    expect(window.location.hash).toBe('');
  });
});

describe('google redirect login response validation', () => {
  beforeEach(() => {
    clearStoredAuthSession();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    clearStoredAuthSession();
  });

  it('rejects an HTML fallback returned from /auth instead of JSON', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('<!doctype html>', {
      status: 200,
      headers: { 'content-type': 'text/html' },
    }));

    await expect(beginGoogleRedirectLogin({ rememberMe: true, returnPath: '/' }))
      .rejects
      .toMatchObject({ code: 'invalid_identity_proxy_response' });
  });
});
