import { appConfig } from './api/config';

const GOOGLE_ORIGINS = [
  'https://accounts.google.com',
  'https://www.googleapis.com',
] as const;

export function installPreconnects() {
  const origins = new Set<string>([
    ...GOOGLE_ORIGINS,
    originOf(appConfig.identityApiBaseUrl),
    originOf(appConfig.profileApiBaseUrl),
    originOf(appConfig.partnershipApiBaseUrl),
    originOf(appConfig.cardsApiBaseUrl),
    originOf(appConfig.fixedRulesApiBaseUrl),
    originOf(appConfig.transactionsApiBaseUrl),
    originOf(appConfig.aggregatesApiBaseUrl),
  ].filter(Boolean) as string[]);

  for (const origin of origins) {
    if (document.head.querySelector(`link[rel="preconnect"][href="${origin}"]`)) {
      continue;
    }

    const link = document.createElement('link');
    link.rel = 'preconnect';
    link.href = origin;
    link.crossOrigin = 'anonymous';
    document.head.appendChild(link);
  }
}

function originOf(value: string) {
  try {
    return new URL(value).origin;
  } catch {
    return null;
  }
}
