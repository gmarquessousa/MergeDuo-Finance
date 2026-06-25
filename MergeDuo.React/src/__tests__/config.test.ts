import { describe, expect, it } from 'vitest';
import { validateProductionConfig, type AppConfig } from '../api/config';

const baseConfig: AppConfig = {
  identityApiBaseUrl: '/',
  profileApiBaseUrl: '/api/profile',
  partnershipApiBaseUrl: '/api/partnership',
  cardsApiBaseUrl: '/api',
  fixedRulesApiBaseUrl: '/api',
  transactionsApiBaseUrl: '/api',
  aggregatesApiBaseUrl: '/api',
  googleClientId: 'google-client.apps.googleusercontent.com',
  appVersion: '0.1.0',
  isProduction: true,
};

describe('validateProductionConfig', () => {
  it('accepts same-origin production API bases', () => {
    expect(validateProductionConfig(baseConfig)).toEqual({ ok: true, missing: [] });
  });

  it('accepts relative same-origin API bases', () => {
    expect(validateProductionConfig({
      ...baseConfig,
      identityApiBaseUrl: '/',
      profileApiBaseUrl: '/api/profile',
    })).toEqual({ ok: true, missing: [] });
  });

  it('rejects localhost production API bases', () => {
    const result = validateProductionConfig({
      ...baseConfig,
      transactionsApiBaseUrl: 'https://localhost:7282',
    });

    expect(result.ok).toBe(false);
    expect(result.missing).toContain('VITE_TRANSACTIONS_API_BASE_URL');
  });
});
