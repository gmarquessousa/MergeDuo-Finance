/**
 * Centralised resolution and validation of public runtime configuration.
 *
 * Vite exposes `VITE_*` variables to the browser; none of them must contain
 * secrets. Production defaults intentionally keep APIs under the app origin so
 * iOS standalone PWAs receive first-party auth cookies after Google redirect.
 */

import { sanitizeBaseUrl } from './http';

const FALLBACKS = {
  identity: 'https://localhost:7211',
  profile: 'https://localhost:7212',
  partnership: 'https://localhost:7085',
  cards: 'https://localhost:7182',
  fixedRules: 'https://localhost:7129',
  transactions: 'https://localhost:7282',
  aggregates: 'https://localhost:7036',
} as const;

const PRODUCTION_DEFAULTS = {
  identity: '/',
  profile: '/api/profile',
  partnership: '/api/partnership',
  cards: '/api',
  fixedRules: '/api',
  transactions: '/api',
  aggregates: '/api',
} as const;

const USE_CROSS_ORIGIN_APIS_ENV = 'VITE_USE_CROSS_ORIGIN_APIS';

export interface AppConfig {
  identityApiBaseUrl: string;
  profileApiBaseUrl: string;
  partnershipApiBaseUrl: string;
  cardsApiBaseUrl: string;
  fixedRulesApiBaseUrl: string;
  transactionsApiBaseUrl: string;
  aggregatesApiBaseUrl: string;
  googleClientId: string;
  appVersion: string;
  isProduction: boolean;
}

function readEnv(name: string): string | undefined {
  const value = (import.meta.env as Record<string, string | undefined>)[name];
  return typeof value === 'string' && value.trim().length > 0 ? value : undefined;
}

const isProduction = import.meta.env.PROD === true;
const useCrossOriginApis = isProduction && readBooleanEnv(USE_CROSS_ORIGIN_APIS_ENV);

export const appConfig: AppConfig = {
  identityApiBaseUrl: resolveApiBaseUrl('VITE_IDENTITY_API_BASE_URL', 'identity'),
  profileApiBaseUrl: resolveApiBaseUrl('VITE_PROFILE_API_BASE_URL', 'profile'),
  partnershipApiBaseUrl: resolveApiBaseUrl('VITE_PARTNERSHIP_API_BASE_URL', 'partnership'),
  cardsApiBaseUrl: resolveApiBaseUrl('VITE_CARDS_API_BASE_URL', 'cards'),
  fixedRulesApiBaseUrl: resolveApiBaseUrl('VITE_FIXED_RULES_API_BASE_URL', 'fixedRules'),
  transactionsApiBaseUrl: resolveApiBaseUrl('VITE_TRANSACTIONS_API_BASE_URL', 'transactions'),
  aggregatesApiBaseUrl: resolveApiBaseUrl('VITE_AGGREGATES_API_BASE_URL', 'aggregates'),
  googleClientId: readEnv('VITE_GOOGLE_CLIENT_ID') ?? '',
  appVersion: readEnv('VITE_APP_VERSION') ?? '0.1.0',
  isProduction,
};

function readBooleanEnv(name: string): boolean {
  const value = readEnv(name)?.toLowerCase();
  return value === 'true' || value === '1' || value === 'yes';
}

function resolveApiBaseUrl(name: string, key: keyof typeof FALLBACKS): string {
  if (isProduction && !useCrossOriginApis) {
    return PRODUCTION_DEFAULTS[key];
  }

  const fallback = isProduction ? PRODUCTION_DEFAULTS[key] : FALLBACKS[key];
  return sanitizeBaseUrl(readEnv(name), fallback);
}

export interface ConfigValidationResult {
  ok: boolean;
  missing: string[];
}

/**
 * Returns the list of required production-only env vars that are still
 * pointing at the development fallback. Used by the bootstrap to refuse to
 * render the app when something is missing.
 */
export function validateProductionConfig(config: AppConfig = appConfig): ConfigValidationResult {
  if (!config.isProduction) {
    return { ok: true, missing: [] };
  }

  const missing: string[] = [];
  const required: Array<{ name: string; value: string; fallback: string }> = [
    { name: 'VITE_IDENTITY_API_BASE_URL', value: config.identityApiBaseUrl, fallback: FALLBACKS.identity },
    { name: 'VITE_PROFILE_API_BASE_URL', value: config.profileApiBaseUrl, fallback: FALLBACKS.profile },
    { name: 'VITE_PARTNERSHIP_API_BASE_URL', value: config.partnershipApiBaseUrl, fallback: FALLBACKS.partnership },
    { name: 'VITE_CARDS_API_BASE_URL', value: config.cardsApiBaseUrl, fallback: FALLBACKS.cards },
    { name: 'VITE_FIXED_RULES_API_BASE_URL', value: config.fixedRulesApiBaseUrl, fallback: FALLBACKS.fixedRules },
    { name: 'VITE_TRANSACTIONS_API_BASE_URL', value: config.transactionsApiBaseUrl, fallback: FALLBACKS.transactions },
    { name: 'VITE_AGGREGATES_API_BASE_URL', value: config.aggregatesApiBaseUrl, fallback: FALLBACKS.aggregates },
  ];

  for (const entry of required) {
    if (entry.value === entry.fallback || isLocalhostUrl(entry.value)) {
      missing.push(entry.name);
    }
  }

  if (!config.googleClientId) {
    missing.push('VITE_GOOGLE_CLIENT_ID');
  }

  return { ok: missing.length === 0, missing };
}

function isLocalhostUrl(value: string): boolean {
  try {
    if (value.startsWith('/')) return false;
    const host = new URL(value).hostname.toLowerCase();
    return host === 'localhost' || host === '127.0.0.1' || host === '[::1]';
  } catch {
    return true;
  }
}
