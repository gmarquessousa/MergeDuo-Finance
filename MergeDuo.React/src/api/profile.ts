import { ApiError, apiFetch, type RequestControlOptions } from './http';
import { appConfig } from './config';

export const profileApiBaseUrl = appConfig.profileApiBaseUrl;

export interface PublicStatsResponse {
  transactionsTracked: number;
  activeMonths: number;
  lastRecomputedAt: string | null;
  isStale: boolean;
}

export interface UserStatsResponse extends PublicStatsResponse {
  source: 'cache' | 'recomputed' | string;
}

export interface RelationshipResponse {
  status: 'active' | string;
  mergedSince: string;
}

export interface PublicProfileResponse {
  id: string;
  name: string;
  handle: string;
  avatarInitials: string;
  avatarUrl: string | null;
  memberSince: string;
  stats: PublicStatsResponse | null;
  relationship: RelationshipResponse | null;
}

/** @deprecated Prefer the shared {@link ApiError} from `./http`; kept for compatibility. */
export class ProfileApiError extends ApiError {
  constructor(status: number, code: string, message: string) {
    super(status, code, message);
    this.name = 'ProfileApiError';
  }
}

export async function getPublicProfile(
  accessToken: string,
  userId: string,
  options?: RequestControlOptions,
): Promise<PublicProfileResponse> {
  return request<PublicProfileResponse>({
    path: `/users/${encodeURIComponent(userId)}`,
    method: 'GET',
    accessToken,
    ...options,
  });
}

export async function findProfileByHandle(
  accessToken: string,
  handle: string,
  options?: RequestControlOptions,
): Promise<PublicProfileResponse> {
  return request<PublicProfileResponse>({
    path: `/users/by-handle/${encodeURIComponent(handle)}`,
    method: 'GET',
    accessToken,
    ...options,
  });
}

export async function getCurrentStats(
  accessToken: string,
  fresh = false,
  options?: RequestControlOptions,
): Promise<UserStatsResponse> {
  return request<UserStatsResponse>({
    path: '/me/stats',
    method: 'GET',
    accessToken,
    query: fresh ? { fresh: true } : undefined,
    ...options,
  });
}

interface ProfileRequestOptions {
  path: string;
  method: string;
  accessToken?: string;
  query?: Record<string, string | number | boolean | null | undefined>;
  signal?: AbortSignal;
  timeoutMs?: number;
}

function request<T>(options: ProfileRequestOptions): Promise<T> {
  return apiFetch<T>({
    baseUrl: profileApiBaseUrl,
    defaultErrorCode: 'profile_api_error',
    ...options,
  });
}
