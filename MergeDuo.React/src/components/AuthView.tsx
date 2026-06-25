import { useEffect, useRef, useState } from 'react';
import {
  beginGoogleRedirectLogin,
  beginGoogleLogin,
  completeGoogleLogin,
  googleClientId,
  identityApiBaseUrl,
  type AuthSession,
  type GoogleChallengeResponse,
  type GoogleRedirectStartResponse,
} from '../api/identity';
import {
  clearAuthDiagnostics,
  getAuthDiagnostics,
  subscribeAuthDiagnostics,
  type AuthDiagnosticEvent,
} from '../api/http';
import { authErrorMessage } from '../authErrorMessage';
import { BrandMark } from './BrandMark';

export type AuthMode = 'login';

export function AuthView({
  onAuthenticated,
  restoreError,
  restoreSubmitting = false,
  onRetryRestore,
}: {
  mode?: AuthMode;
  onModeChange?: (mode: AuthMode) => void;
  onAuthenticated: (session: AuthSession, rememberSession: boolean) => void;
  restoreError?: string | null;
  restoreSubmitting?: boolean;
  onRetryRestore?: () => void;
}) {
  const googleButtonRef = useRef<HTMLDivElement>(null);
  const challengeRef = useRef<GoogleChallengeResponse | GoogleRedirectStartResponse | null>(null);
  const challengeExpiresAtRef = useRef<number>(0);
  const refreshTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const rememberRef = useRef(true);
  const renderAttemptRef = useRef(0);
  const [rememberSession, setRememberSession] = useState(true);
  const [status, setStatus] = useState<'loading' | 'ready' | 'submitting'>('loading');
  const [error, setError] = useState<string | null>(null);
  const [authFlow, setAuthFlow] = useState<'popup' | 'redirect'>('popup');
  const [showDebug, setShowDebug] = useState(false);
  const [debugLog, setDebugLog] = useState<string[]>([]);
  const [apiEvents, setApiEvents] = useState<AuthDiagnosticEvent[]>(() => getAuthDiagnostics());

  useEffect(() => {
    const unsub = subscribeAuthDiagnostics(() => setApiEvents(getAuthDiagnostics()));
    return unsub;
  }, []);

  const log = (msg: string) => {
    const ts = new Date().toISOString().slice(11, 23);
    setDebugLog((prev) => [...prev.slice(-29), `${ts} ${msg}`]);
    console.log(`[Auth] ${msg}`);
  };

  useEffect(() => {
    rememberRef.current = rememberSession;
  }, [rememberSession]);

  useEffect(() => {
    let cancelled = false;

    function clearRefreshTimer() {
      if (refreshTimerRef.current !== null) {
        clearTimeout(refreshTimerRef.current);
        refreshTimerRef.current = null;
      }
    }

    function scheduleRefresh(expiresInSeconds: number) {
      clearRefreshTimer();
      // Renew 60s before expiry, with a sane floor (15s) and ceiling (~30 min).
      const targetMs = Math.min(Math.max(expiresInSeconds - 60, 15), 30 * 60) * 1000;
      refreshTimerRef.current = setTimeout(() => {
        if (cancelled) return;
        log('challenge nearing expiry, refreshing');
        challengeRef.current = null;
        void prepareGoogleButton();
      }, targetMs);
    }

    async function prepareGoogleButton() {
      if (!googleClientId) {
        setStatus('ready');
        setError('Configuração VITE_GOOGLE_CLIENT_ID ausente.');
        return;
      }

      const attempt = renderAttemptRef.current + 1;
      renderAttemptRef.current = attempt;
      clearRefreshTimer();
      setStatus('loading');
      setError(null);
      log(`prepareGoogleButton attempt #${attempt}`);

      try {
        const useRedirect = isIosStandaloneWebApp();
        setAuthFlow(useRedirect ? 'redirect' : 'popup');
        log('fetching challenge + GSI script');
        const [challenge] = await Promise.all([
          useRedirect
            ? beginGoogleRedirectLogin({
                rememberMe: rememberRef.current,
                returnPath: currentReturnPath(),
              })
            : beginGoogleLogin(),
          loadGoogleIdentityScript(),
        ]);
        log(`challenge OK, GSI script loaded (${useRedirect ? 'redirect' : 'popup'})`);

        if (cancelled || attempt !== renderAttemptRef.current) return;

        const target = googleButtonRef.current;
        if (!target || !window.google?.accounts.id) {
          throw new Error('Google Identity Services indisponível.');
        }

        challengeRef.current = challenge;
        challengeExpiresAtRef.current = Date.now() + challenge.expiresIn * 1000;
        scheduleRefresh(challenge.expiresIn);
        target.innerHTML = '';

        log('initialize() with itp_support');
        const configuration: GoogleIdConfiguration = {
          client_id: googleClientId,
          nonce: challenge.nonce,
          itp_support: true,
        };

        if (useRedirect) {
          const redirect = challenge as GoogleRedirectStartResponse;
          configuration.ux_mode = 'redirect';
          configuration.login_uri = redirect.loginUri;
        } else {
          configuration.callback = async (response) => {
            log(`callback fired (has credential: ${!!response.credential})`);
            if (!response.credential) {
              setError('Não foi possível obter o token do Google.');
              return;
            }

            const activeChallenge = challengeRef.current;
            const stale =
              !activeChallenge || Date.now() >= challengeExpiresAtRef.current - 5_000;
            if (stale) {
              log('challenge stale at callback time, re-preparing');
              setError('Sessão de login expirou. Tente novamente.');
              if (!cancelled) {
                void prepareGoogleButton();
              }
              return;
            }

            if (!('csrfToken' in activeChallenge)) {
              log('popup callback received while redirect challenge is active');
              setError('Sessão de login inválida. Tente novamente.');
              if (!cancelled) {
                void prepareGoogleButton();
              }
              return;
            }

            // Consume so a duplicate callback cannot reuse the same nonce.
            challengeRef.current = null;
            clearRefreshTimer();
            setStatus('submitting');
            setError(null);

            try {
              log('POST /auth/google/callback');
              const session = await completeGoogleLogin({
                idToken: response.credential,
                csrfToken: activeChallenge.csrfToken,
                challengeToken: activeChallenge.challengeToken,
                rememberMe: rememberRef.current,
              });
              log('session received OK');
              onAuthenticated(session, rememberRef.current);
            } catch (err) {
              log(`callback error: ${authErrorMessage(err)}`);
              setError(authErrorMessage(err));
              if (!cancelled) {
                void prepareGoogleButton();
              }
            }
          };
        }

        window.google.accounts.id.initialize(configuration);

        const width = Math.min(360, Math.max(240, Math.floor(target.clientWidth || 320)));
        log(`renderButton width=${width}`);
        const buttonConfiguration: GoogleButtonConfiguration = {
          type: 'standard',
          theme: 'outline',
          size: 'large',
          text: 'continue_with',
          shape: 'pill',
          width,
          locale: 'pt-BR',
          logo_alignment: 'left',
        };
        if (useRedirect) {
          buttonConfiguration.state = (challenge as GoogleRedirectStartResponse).state;
        }
        window.google.accounts.id.renderButton(target, buttonConfiguration);

        setStatus('ready');
        log('button rendered, status=ready');
      } catch (err) {
        if (cancelled) return;
        setStatus('ready');
        log(`prepare error: ${authErrorMessage(err)}`);
        setError(authErrorMessage(err));
      }
    }

    function handleVisibilityChange() {
      if (document.visibilityState !== 'visible') return;
      const remainingMs = challengeExpiresAtRef.current - Date.now();
      if (remainingMs <= 60_000) {
        log('tab visible, challenge near/past expiry; refreshing');
        challengeRef.current = null;
        void prepareGoogleButton();
      }
    }

    void prepareGoogleButton();
    document.addEventListener('visibilitychange', handleVisibilityChange);

    return () => {
      cancelled = true;
      clearRefreshTimer();
      document.removeEventListener('visibilitychange', handleVisibilityChange);
      window.google?.accounts.id.cancel();
    };
  }, [onAuthenticated, rememberSession]);

  return (
    <div className="relative min-h-screen flex flex-col overflow-hidden" style={{ background: 'rgb(var(--bg-app))' }}>
      {/* Ambient gradient glow backdrop */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 -z-10"
        style={{
          background:
            'radial-gradient(60% 45% at 85% -5%, rgba(10,132,255,0.16), transparent 70%), radial-gradient(55% 40% at 0% 105%, rgba(94,92,230,0.14), transparent 70%)',
        }}
      />
      {/* Top brand bar */}
      <div className="px-6 pt-10 pb-2 flex items-center gap-2">
        <BrandMark />
        <span className="text-sm font-semibold tracking-tight text-ink">Merge Duo</span>
      </div>

      <div className="flex-1 flex flex-col justify-center px-5 pb-10">
        {/* Hero mark + text */}
        <div className="mb-8">
          <div className="mb-5 inline-flex h-16 w-16 items-center justify-center rounded-3xl hero-surface shadow-hero animate-scale-in">
            <span className="scale-125">
              <BrandMark />
            </span>
          </div>
          <h1 className="text-[30px] font-bold tracking-tight text-ink leading-[1.1]">
            Bem-vindo de volta
          </h1>
          <p className="mt-2.5 text-[15px] text-ink-muted leading-relaxed">
            Acesse sua visão financeira e os dados compartilhados do Merge.
          </p>
        </div>

        {/* Login card */}
        <div className="rounded-4xl bg-paper-card/90 backdrop-blur-xl border border-paper-line/70 p-6 shadow-elevated animate-slide-up">
          <div
            ref={googleButtonRef}
            className={`min-h-11 grid place-items-center ${status === 'submitting' ? 'pointer-events-none opacity-60' : ''}`}
          />

          {status === 'loading' && (
            <div className="mt-3 flex items-center justify-center gap-2 text-xs text-ink-muted">
              <span className="w-3 h-3 rounded-full border-2 border-paper-line border-t-ink-muted animate-spin" />
              Preparando login com Google...
            </div>
          )}

          {restoreError && (
            <div className="mt-4 rounded-2xl border border-amber-400/30 bg-amber-50 px-4 py-3 text-center text-xs text-amber-900">
              <div className="leading-relaxed">{restoreError}</div>
              {onRetryRestore && (
                <button
                  type="button"
                  onClick={onRetryRestore}
                  disabled={restoreSubmitting}
                  className="mt-3 h-8 px-4 rounded-full bold-surface text-[11px] font-semibold disabled:opacity-50 disabled:cursor-not-allowed tap-surface"
                >
                  {restoreSubmitting ? 'Tentando entrar...' : 'Tentar entrar direto'}
                </button>
              )}
            </div>
          )}

          <label className="mt-5 flex items-center gap-3 cursor-pointer tap-surface p-2 rounded-xl hover:bg-paper-line/50 -mx-2 transition-colors">
            <input
              type="checkbox"
              checked={rememberSession}
              onChange={(event) => setRememberSession(event.target.checked)}
              className="sr-only"
            />
            <span
              className={`w-5 h-5 rounded-lg border-2 grid place-items-center shrink-0 transition-all ${
                rememberSession ? 'bold-surface border-ink' : 'border-paper-line bg-paper'
              }`}
            >
              {rememberSession && <IconCheck />}
            </span>
            <span className="text-xs text-ink-muted leading-tight">Entrar direto neste dispositivo</span>
          </label>

          {status === 'submitting' && (
            <div className="mt-3 flex items-center justify-center gap-2 text-xs text-ink-muted">
              <span className="w-3 h-3 rounded-full border-2 border-paper-line border-t-ink-muted animate-spin" />
              Entrando...
            </div>
          )}

          {error && (
            <div className="mt-4 rounded-2xl border border-accent-neg/20 bg-accent-neg/8 px-4 py-3 text-center text-xs text-accent-neg">
              {error}
            </div>
          )}

          <p className="mt-5 text-center text-[10px] text-ink-muted/70 leading-relaxed">
            Ao continuar, você concorda com os termos de uso e a política de privacidade do Merge Duo.
          </p>

          <button
            type="button"
            onClick={() => setShowDebug((v) => !v)}
            className="mt-3 w-full text-[10px] text-ink-muted/40 hover:text-ink-muted/70 transition"
          >
            {showDebug ? 'Ocultar diagnóstico' : 'Mostrar diagnóstico'}
          </button>

          {showDebug && (
            <div className="mt-2 rounded-2xl border border-paper-line bg-paper p-3">
              <div className="text-[10px] font-mono text-ink-muted leading-relaxed break-all">
                <div>UA: {navigator.userAgent.slice(0, 80)}</div>
                <div>Cookies: {navigator.cookieEnabled ? 'on' : 'off'}</div>
                <div>FedCM: {('IdentityCredential' in window) ? 'sim' : 'não'}</div>
                <div>Standalone: {isStandaloneDisplay() ? 'sim' : 'não'}</div>
                <div>Fluxo: {authFlow}</div>
                <div>Identity: {compactUrl(identityApiBaseUrl)}</div>
                <div>SW: {('serviceWorker' in navigator) ? 'sim' : 'não'}</div>
                <div>GSI: {window.google?.accounts?.id ? 'carregado' : 'não carregado'}</div>
              </div>
              <div className="mt-2 max-h-48 overflow-y-auto text-[9px] font-mono leading-tight text-ink-soft">
                {debugLog.length === 0 ? (
                  <div className="text-ink-muted">(sem eventos ainda)</div>
                ) : (
                  debugLog.map((line, i) => <div key={i}>{line}</div>)
                )}
              </div>
              <div className="mt-2 flex items-center justify-between">
                <div className="text-[10px] font-mono text-ink-muted">API ({apiEvents.length})</div>
                <button
                  type="button"
                  onClick={() => clearAuthDiagnostics()}
                  className="text-[10px] text-ink-muted/70 hover:text-ink-muted"
                >
                  limpar
                </button>
              </div>
              <div className="mt-1 max-h-48 overflow-y-auto text-[9px] font-mono leading-tight text-ink-soft">
                {apiEvents.length === 0 ? (
                  <div className="text-ink-muted">(sem chamadas ainda)</div>
                ) : (
                  apiEvents.map((evt, i) => (
                    <div key={i}>
                      {new Date(evt.ts).toISOString().slice(11, 23)} {evt.message}
                    </div>
                  ))
                )}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

let googleScriptPromise: Promise<void> | null = null;

function loadGoogleIdentityScript() {
  if (window.google?.accounts.id) {
    return Promise.resolve();
  }

  if (googleScriptPromise) {
    return googleScriptPromise;
  }

  googleScriptPromise = new Promise<void>((resolve, reject) => {
    const existing = document.querySelector<HTMLScriptElement>('script[src="https://accounts.google.com/gsi/client"]');
    if (existing) {
      existing.addEventListener('load', () => resolve(), { once: true });
      existing.addEventListener('error', () => reject(new Error('Não foi possível carregar o Google.')), { once: true });
      return;
    }

    const script = document.createElement('script');
    script.src = 'https://accounts.google.com/gsi/client';
    script.async = true;
    script.defer = true;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error('Não foi possível carregar o Google.'));
    document.head.appendChild(script);
  });

  return googleScriptPromise;
}

interface GoogleCredentialResponse {
  credential?: string;
}

function IconCheck() {
  return (
    <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3.5" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="20 6 9 17 4 12"/>
    </svg>
  );
}

interface GoogleIdConfiguration {
  client_id: string;
  nonce: string;
  callback?: (response: GoogleCredentialResponse) => void;
  itp_support?: boolean;
  ux_mode?: 'popup' | 'redirect';
  login_uri?: string;
}

interface GoogleButtonConfiguration {
  type: 'standard';
  theme: 'outline' | 'filled_blue' | 'filled_black';
  size: 'large' | 'medium' | 'small';
  text: 'continue_with' | 'signin_with' | 'signup_with';
  shape: 'pill' | 'rectangular' | 'circle' | 'square';
  width: number;
  locale: string;
  logo_alignment: 'left' | 'center';
  state?: string;
}

interface GoogleIdentityServices {
  accounts: {
    id: {
      initialize: (configuration: GoogleIdConfiguration) => void;
      renderButton: (parent: HTMLElement, options: GoogleButtonConfiguration) => void;
      cancel: () => void;
    };
  };
}

declare global {
  interface Window {
    google?: GoogleIdentityServices;
  }
}

function isIosStandaloneWebApp(): boolean {
  if (typeof navigator === 'undefined') return false;

  const userAgent = navigator.userAgent || '';
  const platform = navigator.platform || '';
  const isIos =
    /iPad|iPhone|iPod/.test(userAgent) ||
    (/MacIntel/.test(platform) && 'maxTouchPoints' in navigator && navigator.maxTouchPoints > 1);

  return isIos && isStandaloneDisplay();
}

function isStandaloneDisplay(): boolean {
  return (
    window.matchMedia?.('(display-mode: standalone)').matches === true ||
    (navigator as Navigator & { standalone?: boolean }).standalone === true
  );
}

function currentReturnPath(): string {
  const path = `${window.location.pathname}${window.location.search}`;
  if (!path.startsWith('/') || path.startsWith('//') || path.includes('\\') || path.includes('#')) {
    return '/';
  }

  return path;
}

function compactUrl(value: string): string {
  return value.replace(/^https?:\/\//, '').slice(0, 80);
}
