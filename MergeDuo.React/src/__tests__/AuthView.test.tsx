import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthView } from '../components/AuthView';

const mocks = vi.hoisted(() => ({
  beginGoogleLogin: vi.fn(),
  beginGoogleRedirectLogin: vi.fn(),
  completeGoogleLogin: vi.fn(),
  initialize: vi.fn(),
  renderButton: vi.fn(),
  cancel: vi.fn(),
}));

vi.mock('../api/identity', () => ({
  beginGoogleLogin: mocks.beginGoogleLogin,
  beginGoogleRedirectLogin: mocks.beginGoogleRedirectLogin,
  completeGoogleLogin: mocks.completeGoogleLogin,
  googleClientId: 'google-client-id',
  identityApiBaseUrl: '/',
}));

describe('AuthView restore session UI', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.beginGoogleLogin.mockResolvedValue({
      nonce: 'nonce',
      csrfToken: 'csrf-token',
      expiresIn: 600,
      challengeToken: 'challenge-token',
    });
    mocks.beginGoogleRedirectLogin.mockResolvedValue({
      nonce: 'redirect-nonce',
      state: 'redirect-state',
      loginUri: 'https://aca-mergeduo-web.example.test/auth/google/redirect-callback',
      expiresIn: 600,
    });

    Object.defineProperty(window, 'google', {
      configurable: true,
      value: {
        accounts: {
          id: {
            initialize: mocks.initialize,
            renderButton: mocks.renderButton,
            cancel: mocks.cancel,
          },
        },
      },
    });
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn().mockReturnValue({ matches: false }),
    });
    Object.defineProperty(window.navigator, 'userAgent', {
      configurable: true,
      value: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)',
    });
    Object.defineProperty(window.navigator, 'standalone', {
      configurable: true,
      value: false,
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    delete window.google;
    window.history.replaceState(null, document.title, '/');
  });

  it('shows restore warning and calls retry', async () => {
    const user = userEvent.setup();
    const onRetryRestore = vi.fn();

    render(
      <AuthView
        onAuthenticated={vi.fn()}
        restoreError="Nao foi possivel entrar direto agora."
        onRetryRestore={onRetryRestore}
      />,
    );

    expect(screen.getByText('Nao foi possivel entrar direto agora.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /tentar entrar direto/i }));

    expect(onRetryRestore).toHaveBeenCalledTimes(1);
  });

  it('disables retry button while restore is running', () => {
    render(
      <AuthView
        onAuthenticated={vi.fn()}
        restoreError="O servico de login demorou para responder."
        restoreSubmitting
        onRetryRestore={vi.fn()}
      />,
    );

    expect(screen.getByRole('button', { name: /tentando entrar/i })).toBeDisabled();
  });

  it('labels the persistent-login option clearly', () => {
    render(<AuthView onAuthenticated={vi.fn()} />);

    expect(screen.getByText('Entrar direto neste dispositivo')).toBeInTheDocument();
  });

  it('uses redirect mode for installed iOS web apps', async () => {
    Object.defineProperty(window.navigator, 'userAgent', {
      configurable: true,
      value: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)',
    });
    Object.defineProperty(window.navigator, 'standalone', {
      configurable: true,
      value: true,
    });
    window.history.replaceState(null, document.title, '/invites/inv_123');

    render(<AuthView onAuthenticated={vi.fn()} />);

    await waitFor(() => expect(mocks.beginGoogleRedirectLogin).toHaveBeenCalled());
    expect(mocks.beginGoogleRedirectLogin).toHaveBeenCalledWith({
      rememberMe: true,
      returnPath: '/invites/inv_123',
    });
    expect(mocks.beginGoogleLogin).not.toHaveBeenCalled();
    expect(mocks.initialize).toHaveBeenCalledWith(expect.objectContaining({
      ux_mode: 'redirect',
      login_uri: 'https://aca-mergeduo-web.example.test/auth/google/redirect-callback',
      nonce: 'redirect-nonce',
    }));
    expect(mocks.renderButton).toHaveBeenCalledWith(
      expect.any(HTMLElement),
      expect.objectContaining({ state: 'redirect-state' }),
    );
  });

  it('keeps popup callback mode outside installed iOS web apps', async () => {
    render(<AuthView onAuthenticated={vi.fn()} />);

    await waitFor(() => expect(mocks.beginGoogleLogin).toHaveBeenCalled());
    expect(mocks.beginGoogleRedirectLogin).not.toHaveBeenCalled();
    expect(mocks.initialize).toHaveBeenCalledWith(expect.objectContaining({
      callback: expect.any(Function),
      nonce: 'nonce',
    }));
  });
});
